using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Data.Configuration;
using QiadHelper.Core.Notifications;
using QiadHelper.Core.Services.Restrictions;

namespace QiadHelper.Core.Services.Cases;

public sealed class CaseService : ICaseService
{
    /// <summary>Actions any reviewer may take (claim, draft, void-request, misc-file, execute).</summary>
    private const QiadRank ReviewerMinRank = QiadRank.ProbationaryAffairs;

    private const QiadRank SealMinRank = QiadRank.Secretary;

    private readonly IChannelConfigService _channelConfig;
    private readonly QiadContext _context;
    private readonly ILogger<CaseService> _logger;
    private readonly IQiadNotificationPublisher _publisher;
    private readonly IRestrictionService _restrictionService;

    public CaseService(QiadContext context,
                       IQiadNotificationPublisher publisher,
                       IRestrictionService restrictionService,
                       IChannelConfigService channelConfig,
                       ILogger<CaseService> logger)
    {
        _context = context;
        _publisher = publisher;
        _restrictionService = restrictionService;
        _channelConfig = channelConfig;
        _logger = logger;
    }

    public Task<Case?> GetAsync(Guid caseId, CancellationToken ct = default) =>
        _context.Cases.Include(c => c.Category).FirstOrDefaultAsync(c => c.Id == caseId, ct);

    public Task<Case?> GetByThreadAsync(ulong threadId, CancellationToken ct = default) =>
        _context.Cases.Include(c => c.Category).FirstOrDefaultAsync(
            c => c.ForumThreadId == threadId
                 || c.VoidRequestThreadId == threadId
                 || c.MiscFilingThreadId == threadId, ct);

    public async Task<Case?> GetByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var prefix = reference.Trim();
        if (prefix.StartsWith("REF-", StringComparison.OrdinalIgnoreCase))
        {
            prefix = prefix[4..];
        }

        prefix = prefix.ToUpperInvariant();
        if (prefix.Length < 4)
        {
            return null;
        }

        // The reference only exists for unnumbered cases (a handful in the validation queue),
        // so we match the id prefix client-side over just those rather than casting in SQL.
        var candidates = await _context.Cases.Include(c => c.Category)
            .Where(c => c.CaseNumber == null)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(
            c => c.Id.ToString("N").StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Case> SubmitAsync(SubmitCaseRequest request, CancellationToken ct = default)
    {
        var category = await _context.CaseCategories
                           .FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.IsActive, ct)
                       ?? throw new InvalidOperationException($"Unknown or inactive case category {request.CategoryId}");

        var newCase = new Case
                      {
                          CategoryId = category.Id,
                          NumberSeries = category.NumberSeries,
                          Status = CaseStatus.Submitted,
                          IsUnfair = request.IsUnfair,
                          AppellantDiscordUserId = request.AppellantDiscordUserId,
                          AppellantDiscordUsername = Truncate(request.AppellantUsername, 64),
                          SubjectDiscordUserId = request.SubjectDiscordUserId ??
                                                 (request.AppellantDiscordUserId == 0
                                                     ? null
                                                     : request.AppellantDiscordUserId),
                          SubjectRobloxUserId = request.SubjectRobloxUserId,
                          SubjectRobloxUsername = Truncate(request.SubjectRobloxUsername, 64),
                          ReportedBanReason = Truncate(request.ReportedBanReason, 1000),
                          AppealText = Truncate(request.AppealText, 4000) ?? string.Empty,
                          ExternalEvidenceUrl = Truncate(request.ExternalEvidenceUrl, 500),
                          RequestContext = Truncate(request.RequestContext, 200),
                          SubmittedAt = DateTime.UtcNow,
                          CreatedById = request.FiledById ?? request.AppellantDiscordUserId
                      };

        if (request.IsTracking)
        {
            // Dummy/tracking case: handled outside the system, recorded for the public database.
            // The filing staff member counts as validator+claimer, so the number exists at once.
            if (request.FiledById is not { } filerId)
            {
                throw new InvalidOperationException("Tracking cases must be filed by a staff member.");
            }

            newCase.IsTracking = true;
            newCase.Status = CaseStatus.UnderReview;
            newCase.ClaimedById = filerId;
            newCase.ClaimedByUsername = Truncate(request.FiledByUsername, 64);
            newCase.ClaimedAt = DateTime.UtcNow;
        }

        _context.Cases.Add(newCase);

        if (request.IsTracking)
        {
            await SaveMintingNumberAsync(newCase, ct);
        }
        else
        {
            await _context.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Case {CaseId} submitted (category {Category}, tracking={IsTracking}) by {Actor}",
            newCase.Id, category.Name, newCase.IsTracking, newCase.CreatedById);

        await _publisher.PublishAsync(new CaseSubmittedNotification(newCase.Id), ct);
        if (request.IsTracking)
        {
            // Fires the same side effects a claim would: public "Pending" announcement etc.
            await _publisher.PublishAsync(
                new CaseStatusChangedNotification(newCase.Id, CaseStatus.Submitted, CaseStatus.UnderReview), ct);
        }

        return newCase;
    }

    public async Task<CaseActionResult> ClaimAsync(Guid caseId, StaffActor actor, CancellationToken ct = default)
    {
        if (actor.Rank < ReviewerMinRank)
        {
            return CaseActionResult.Fail("You must be QIAD staff (PA+) to claim a case.");
        }

        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        if (c.Status != CaseStatus.Submitted)
        {
            return CaseActionResult.Fail($"Only submitted cases can be claimed (case is {c.Status}).");
        }

        c.ClaimedById = actor.DiscordUserId;
        c.ClaimedByUsername = Truncate(actor.Username, 64);
        c.ClaimedAt = DateTime.UtcNow;
        c.Status = CaseStatus.UnderReview;
        c.ModifiedById = actor.DiscordUserId;

        // Mint the number at claim (design §5); the old "Temporary ID" hack disappears.
        await SaveMintingNumberAsync(c, ct);

        _logger.LogInformation("Case {CaseId} claimed by {Actor} as {Series} #{Number}",
            c.Id, actor.DiscordUserId, c.NumberSeries, c.CaseNumber);

        await _publisher.PublishAsync(
            new CaseStatusChangedNotification(c.Id, CaseStatus.Submitted, CaseStatus.UnderReview), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task<CaseActionResult> DraftVerdictAsync(Guid caseId,
                                                          StaffActor actor,
                                                          CaseVerdict verdict,
                                                          string decisionReasoning,
                                                          CancellationToken ct = default)
    {
        if (actor.Rank < ReviewerMinRank)
        {
            return CaseActionResult.Fail("You must be QIAD staff (PA+) to draft a decision.");
        }

        if (string.IsNullOrWhiteSpace(decisionReasoning))
        {
            return CaseActionResult.Fail("A decision needs its reasoning for the ratifier.");
        }

        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        if (c.Status != CaseStatus.UnderReview)
        {
            return CaseActionResult.Fail($"A decision can only be drafted while under review (case is {c.Status}).");
        }

        // The reviewer's reasoning stays internal; the public verdict text is authored by the
        // ratifier at uphold (the live process: /add-decision vs the later /issue-verdict).
        var oldStatus = c.Status;
        c.Verdict = verdict;
        c.DecisionReasoning = Truncate(decisionReasoning, 4000);
        c.Status = CaseStatus.AwaitingRatification;
        c.ModifiedById = actor.DiscordUserId;

        await _context.SaveChangesAsync(ct);

        await _publisher.PublishAsync(new CaseStatusChangedNotification(c.Id, oldStatus, c.Status), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task<CaseActionResult> RatifyAsync(Guid caseId,
                                                    StaffActor actor,
                                                    bool uphold,
                                                    string? publicVerdictText,
                                                    CancellationToken ct = default)
    {
        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        var requiredRank = c.Category?.VerdictRatifierMinRank ?? QiadRank.Secretary;
        if (actor.Rank < requiredRank)
        {
            return CaseActionResult.Fail(
                $"Ratifying a verdict for this category requires {requiredRank.ShortName()}+.");
        }

        if (c.Status != CaseStatus.AwaitingRatification)
        {
            return CaseActionResult.Fail($"Case is not awaiting ratification (case is {c.Status}).");
        }

        if (uphold && string.IsNullOrWhiteSpace(publicVerdictText))
        {
            return CaseActionResult.Fail("Upholding requires the public verdict text.");
        }

        var oldStatus = c.Status;
        c.ModifiedById = actor.DiscordUserId;

        if (uphold)
        {
            // The ratifier authors what the public sees; the reviewer's decision stays internal.
            // The case does NOT close here: the claimer executes the decision (design §5).
            c.Reasoning = Truncate(publicVerdictText, 4000);
            c.Status = CaseStatus.AwaitingExecution;
            c.RatifiedById = actor.DiscordUserId;
            c.RatifiedByUsername = Truncate(actor.Username, 64);
            c.RatifiedAt = DateTime.UtcNow;
        }
        else
        {
            // Overturned: back to review; the drafted decision stays for the reviewer to revise.
            c.Status = CaseStatus.UnderReview;
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Case {CaseId} ratification by {Actor}: {Decision}",
            c.Id, actor.DiscordUserId, uphold ? "upheld" : "overturned");

        await _publisher.PublishAsync(new CaseStatusChangedNotification(c.Id, oldStatus, c.Status), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task<CaseActionResult> RequestVoidAsync(Guid caseId,
                                                         StaffActor actor,
                                                         VoidReason reason,
                                                         string voidReasoning,
                                                         CancellationToken ct = default)
    {
        if (actor.Rank < ReviewerMinRank)
        {
            return CaseActionResult.Fail("You must be QIAD staff (PA+) to file a void request.");
        }

        if (string.IsNullOrWhiteSpace(voidReasoning))
        {
            return CaseActionResult.Fail("Void requests need a reasoning.");
        }

        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        if (c.Status is not (CaseStatus.Submitted or CaseStatus.UnderReview))
        {
            return CaseActionResult.Fail($"Only open cases can be voided (case is {c.Status}).");
        }

        var oldStatus = c.Status;
        c.VoidReason = reason;
        c.VoidReasoning = Truncate(voidReasoning, 2000);
        c.VoidRequestedById = actor.DiscordUserId;
        c.Status = CaseStatus.AwaitingVoidRatification;
        c.ModifiedById = actor.DiscordUserId;

        await _context.SaveChangesAsync(ct);

        await _publisher.PublishAsync(new CaseStatusChangedNotification(c.Id, oldStatus, c.Status), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task<CaseActionResult> RatifyVoidAsync(Guid caseId,
                                                        StaffActor actor,
                                                        bool approve,
                                                        string? dmReason,
                                                        CancellationToken ct = default)
    {
        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        var requiredRank = c.Category?.VoidRatifierMinRank ?? QiadRank.InvestigativeAffairs;
        if (actor.Rank < requiredRank)
        {
            return CaseActionResult.Fail(
                $"Ratifying a void request for this category requires {requiredRank.ShortName()}+.");
        }

        if (c.Status != CaseStatus.AwaitingVoidRatification)
        {
            return CaseActionResult.Fail($"Case has no pending void request (case is {c.Status}).");
        }

        if (approve && string.IsNullOrWhiteSpace(dmReason))
        {
            return CaseActionResult.Fail("Approving a void needs the reason shown to the appellant.");
        }

        var oldStatus = c.Status;
        c.ModifiedById = actor.DiscordUserId;

        if (approve)
        {
            // The ratifier authors the appellant-facing reason (also the public one for
            // numbered cases); the requester's original stays in the void post.
            c.VoidReasoning = Truncate(dmReason, 2000);
            c.Status = CaseStatus.Voided;
            c.ClosedAt = DateTime.UtcNow;
            c.ClosedById = actor.DiscordUserId;
            c.ClosedByUsername = Truncate(actor.Username, 64);
        }
        else
        {
            // Rejected: the case resumes where it was (claimed cases have a number).
            c.Status = c.CaseNumber != null ? CaseStatus.UnderReview : CaseStatus.Submitted;
            c.VoidReason = null;
            c.VoidReasoning = null;
        }

        await _context.SaveChangesAsync(ct);

        await _publisher.PublishAsync(new CaseStatusChangedNotification(c.Id, oldStatus, c.Status), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task<CaseActionResult> FileMiscAsync(Guid caseId,
                                                      StaffActor actor,
                                                      MiscFilingType filingType,
                                                      string details,
                                                      string? evidenceUrl = null,
                                                      CancellationToken ct = default)
    {
        if (actor.Rank < ReviewerMinRank)
        {
            return CaseActionResult.Fail("You must be QIAD staff (PA+) to file a misc closure.");
        }

        if (string.IsNullOrWhiteSpace(details))
        {
            return CaseActionResult.Fail("Misc filings need the filing details.");
        }

        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        if (c.Status is not (CaseStatus.Submitted or CaseStatus.UnderReview))
        {
            return CaseActionResult.Fail($"Only open cases can be misc-filed (case is {c.Status}).");
        }

        // Protocol bypass (design §5): no ratification, but the case does NOT close yet — the
        // misc post's Close button asks for the separate public reasoning.
        var oldStatus = c.Status;
        c.IsMisc = true;
        c.MiscFilingType = filingType;
        c.DecisionReasoning = Truncate(details, 4000);
        if (!string.IsNullOrWhiteSpace(evidenceUrl))
        {
            c.ExternalEvidenceUrl = Truncate(evidenceUrl, 500);
        }

        c.Status = CaseStatus.AwaitingExecution;
        c.ModifiedById = actor.DiscordUserId;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Case {CaseId} misc-filed ({FilingType}) by {Actor}",
            c.Id, filingType, actor.DiscordUserId);

        await _publisher.PublishAsync(new CaseStatusChangedNotification(c.Id, oldStatus, c.Status), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task<CaseActionResult> ExecuteAsync(Guid caseId, StaffActor actor, CancellationToken ct = default)
    {
        if (actor.Rank < ReviewerMinRank)
        {
            return CaseActionResult.Fail("You must be QIAD staff (PA+) to execute a decision.");
        }

        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        if (c.Status != CaseStatus.AwaitingExecution || c.IsMisc)
        {
            return CaseActionResult.Fail(c is { Status: CaseStatus.AwaitingExecution, IsMisc: true }
                ? "Misc filings are closed from their forum post."
                : $"There is no upheld decision to execute (case is {c.Status}).");
        }

        c.Status = CaseStatus.Closed;
        c.ClosedAt = DateTime.UtcNow;
        c.ClosedById = actor.DiscordUserId;
        c.ClosedByUsername = Truncate(actor.Username, 64);
        c.ModifiedById = actor.DiscordUserId;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Case {CaseId} executed and closed by {Actor}", c.Id, actor.DiscordUserId);

        // Executing a rejection hands out the category's cooldown role automatically.
        if (c.Verdict == CaseVerdict.Rejected)
        {
            await ApplyRejectionRestrictionAsync(c, actor, ct);
        }

        await _publisher.PublishAsync(
            new CaseStatusChangedNotification(c.Id, CaseStatus.AwaitingExecution, c.Status), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task<CaseActionResult> CloseMiscAsync(Guid caseId,
                                                       StaffActor actor,
                                                       string publicReasoning,
                                                       CancellationToken ct = default)
    {
        if (actor.Rank < ReviewerMinRank)
        {
            return CaseActionResult.Fail("You must be QIAD staff (PA+) to close a misc filing.");
        }

        if (string.IsNullOrWhiteSpace(publicReasoning))
        {
            return CaseActionResult.Fail("Closing a misc filing needs the public reasoning.");
        }

        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        if (c.Status != CaseStatus.AwaitingExecution || !c.IsMisc)
        {
            return CaseActionResult.Fail($"There is no pending misc filing to close (case is {c.Status}).");
        }

        c.Verdict ??= CaseVerdict.Other;
        c.Reasoning = Truncate(publicReasoning, 4000);
        c.Status = CaseStatus.Closed;
        c.ClosedAt = DateTime.UtcNow;
        c.ClosedById = actor.DiscordUserId;
        c.ClosedByUsername = Truncate(actor.Username, 64);
        c.ModifiedById = actor.DiscordUserId;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Misc case {CaseId} closed by {Actor}", c.Id, actor.DiscordUserId);

        await _publisher.PublishAsync(
            new CaseStatusChangedNotification(c.Id, CaseStatus.AwaitingExecution, c.Status), ct);

        return CaseActionResult.Ok(c);
    }

    /// <summary>Rejection cooldown (per-category role + days), applied at execution time.</summary>
    private async Task ApplyRejectionRestrictionAsync(Case c, StaffActor actor, CancellationToken ct)
    {
        if (c.Category?.RejectionRestrictionRoleId is not { } roleId)
        {
            return;
        }

        if (c.SubjectDiscordUserId is not { } targetId)
        {
            _logger.LogWarning("Case {CaseId}: rejection role configured but the case has no Discord subject", c.Id);

            return;
        }

        var guildId = await _channelConfig.GetChannelIdAsync(QiadConfigKeys.MainGuildId, ct);
        if (guildId == 0)
        {
            _logger.LogWarning("Case {CaseId}: rejection role configured but MainGuildId is not set", c.Id);

            return;
        }

        var reference = c.NumberSeries == CaseNumberSeries.Incidents
            ? $"Incident #{c.CaseNumber}"
            : $"Case #{c.CaseNumber}";
        var result = await _restrictionService.ApplyAsync(new ApplyRestrictionRequest(
            targetId,
            c.SubjectDiscordUserId == c.AppellantDiscordUserId ? c.AppellantDiscordUsername : null,
            guildId,
            roleId,
            $"Rejection cooldown for {reference}",
            c.Category.RejectionRestrictionDays is { } days ? TimeSpan.FromDays(days) : null,
            CaseId: c.Id,
            TargetRobloxUserId: c.SubjectRobloxUserId), actor, ct);

        if (!result.Success)
        {
            _logger.LogWarning("Case {CaseId}: rejection restriction failed: {Error}", c.Id, result.Error);
        }
    }

    public async Task<CaseActionResult> SetSealedAsync(Guid caseId,
                                                       StaffActor actor,
                                                       bool isSealed,
                                                       CancellationToken ct = default)
    {
        if (actor.Rank < SealMinRank)
        {
            return CaseActionResult.Fail($"Sealing requires {SealMinRank.ShortName()}+.");
        }

        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        var oldStatus = c.Status;

        if (isSealed)
        {
            if (c.Status is not (CaseStatus.Closed or CaseStatus.Voided))
            {
                return CaseActionResult.Fail($"Only terminal cases can be sealed (case is {c.Status}).");
            }

            c.Status = CaseStatus.Sealed;
        }
        else
        {
            if (c.Status != CaseStatus.Sealed)
            {
                return CaseActionResult.Fail("Case is not sealed.");
            }

            // Restore the terminal state it was sealed from: voids carry no ratified verdict.
            c.Status = c.Verdict == null && c.VoidReasoning != null ? CaseStatus.Voided : CaseStatus.Closed;
        }

        c.ModifiedById = actor.DiscordUserId;
        await _context.SaveChangesAsync(ct);

        await _publisher.PublishAsync(new CaseStatusChangedNotification(c.Id, oldStatus, c.Status), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task<CaseActionResult> AddEvidenceAsync(Guid caseId,
                                                         ulong submittedById,
                                                         string submittedByUsername,
                                                         bool isFromAppellant,
                                                         string? text,
                                                         string? attachmentUrl = null,
                                                         string? attachmentFileName = null,
                                                         ulong? sourceChannelId = null,
                                                         ulong? sourceMessageId = null,
                                                         CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(attachmentUrl))
        {
            return CaseActionResult.Fail("Evidence needs text or an attachment.");
        }

        var c = await GetAsync(caseId, ct);
        if (c == null)
        {
            return CaseActionResult.Fail("Case not found.");
        }

        if (c.Status is CaseStatus.Closed or CaseStatus.Voided or CaseStatus.Sealed)
        {
            return CaseActionResult.Fail("This case is closed; evidence can no longer be attached.");
        }

        var item = new CaseEvidenceItem
                   {
                       CaseId = c.Id,
                       SubmittedById = submittedById,
                       SubmittedByUsername = Truncate(submittedByUsername, 64),
                       IsFromAppellant = isFromAppellant,
                       Text = Truncate(text, 4000),
                       AttachmentUrl = Truncate(attachmentUrl, 1000),
                       AttachmentFileName = Truncate(attachmentFileName, 260),
                       SourceChannelId = sourceChannelId,
                       SourceMessageId = sourceMessageId,
                       CreatedById = submittedById
                   };

        _context.CaseEvidenceItems.Add(item);
        await _context.SaveChangesAsync(ct);

        await _publisher.PublishAsync(new CaseEvidenceFiledNotification(c.Id, item.Id), ct);

        return CaseActionResult.Ok(c);
    }

    public async Task RecordQueueMessageAsync(Guid caseId,
                                              ulong channelId,
                                              ulong messageId,
                                              CancellationToken ct = default)
    {
        await _context.Cases
            .Where(c => c.Id == caseId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.QueueChannelId, channelId)
                .SetProperty(c => c.QueueMessageId, messageId), ct);
    }

    public async Task RecordVoidRequestThreadAsync(Guid caseId, ulong threadId, CancellationToken ct = default)
    {
        await _context.Cases
            .Where(c => c.Id == caseId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.VoidRequestThreadId, threadId), ct);
    }

    public async Task RecordMiscFilingThreadAsync(Guid caseId, ulong threadId, CancellationToken ct = default)
    {
        await _context.Cases
            .Where(c => c.Id == caseId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.MiscFilingThreadId, threadId), ct);
    }

    public async Task RecordForumThreadAsync(Guid caseId,
                                             ulong forumChannelId,
                                             ulong forumThreadId,
                                             CancellationToken ct = default)
    {
        await _context.Cases
            .Where(c => c.Id == caseId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ForumChannelId, forumChannelId)
                .SetProperty(c => c.ForumThreadId, forumThreadId), ct);
    }

    public async Task RecordAnnouncementMessageAsync(Guid caseId,
                                                     ulong messageId,
                                                     bool isClosedAnnouncement,
                                                     CancellationToken ct = default)
    {
        await _context.Cases
            .Where(c => c.Id == caseId)
            .ExecuteUpdateAsync(s =>
            {
                if (isClosedAnnouncement)
                {
                    s.SetProperty(c => c.ClosedMessageId, messageId);
                }
                else
                {
                    s.SetProperty(c => c.OpenedMessageId, messageId);
                }
            }, ct);
    }

    public async Task RecordDmChannelAsync(Guid caseId, ulong dmChannelId, CancellationToken ct = default)
    {
        await _context.Cases
            .Where(c => c.Id == caseId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AppellantDmChannelId, dmChannelId), ct);
    }

    public async Task RecordEvidenceMirroredAsync(Guid evidenceItemId,
                                                  ulong mirroredMessageId,
                                                  CancellationToken ct = default)
    {
        await _context.CaseEvidenceItems
            .Where(i => i.Id == evidenceItemId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.MirroredMessageId, mirroredMessageId), ct);
    }

    /// <summary>
    ///     Assigns the next number in the case's series and saves. The (NumberSeries, CaseNumber)
    ///     unique index backstops the rare concurrent mint; retry re-reads MAX+1 until the insert wins.
    /// </summary>
    private async Task SaveMintingNumberAsync(Case c, CancellationToken ct)
    {
        for (var attempt = 1;; attempt++)
        {
            c.CaseNumber = await NextNumberAsync(c.NumberSeries, ct);
            try
            {
                await _context.SaveChangesAsync(ct);

                break;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                _logger.LogWarning("Case number collision on {Series} #{Number}, retrying",
                    c.NumberSeries, c.CaseNumber);
            }
        }
    }

    private async Task<int> NextNumberAsync(CaseNumberSeries series, CancellationToken ct)
    {
        var max = await _context.Cases
            .Where(c => c.NumberSeries == series && c.CaseNumber != null)
            .MaxAsync(c => c.CaseNumber, ct);

        return (max ?? 0) + 1;
    }

    private static string? Truncate(string? value, int max)
    {
        if (value == null)
        {
            return null;
        }

        value = value.Trim();

        return value.Length <= max ? value : value[..max];
    }
}
