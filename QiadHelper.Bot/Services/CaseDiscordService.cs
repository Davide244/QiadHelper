using System.Globalization;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;
using QiadHelper.Core;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Data.Configuration;
using QiadHelper.Core.Data.Restrictions;
using QiadHelper.Core.Notifications;
using QiadHelper.Core.Services;
using QiadHelper.Core.Services.Cases;

namespace QiadHelper.Bot.Services;

/// <summary>
///     Performs every Discord side effect behind the notification seam (design §7.5): the
///     validation queue, review threads, appellant DMs, ratification pings, public announcements,
///     intake panels, and restriction roles. All case/restriction state lives in the DB before
///     this code runs — a Discord hiccup here never corrupts workflow state.
/// </summary>
public sealed class CaseDiscordService
{
    private readonly IChannelConfigService _channelConfig;
    private readonly DiscordSocketClient _client;
    private readonly IDirectMessageService _dm;
    private readonly IBotErrorLogService _errorLog;
    private readonly ILogger<CaseDiscordService> _logger;
    private readonly BotOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public CaseDiscordService(DiscordSocketClient client,
                              IServiceScopeFactory scopeFactory,
                              IDirectMessageService dm,
                              IChannelConfigService channelConfig,
                              IBotErrorLogService errorLog,
                              IOptions<BotOptions> options,
                              ILogger<CaseDiscordService> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _dm = dm;
        _channelConfig = channelConfig;
        _errorLog = errorLog;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(QiadNotification notification, CancellationToken ct)
    {
        switch (notification)
        {
            case CaseSubmittedNotification n:
                await OnCaseSubmittedAsync(n.CaseId, ct);

                break;
            case CaseStatusChangedNotification n:
                await OnStatusChangedAsync(n, ct);

                break;
            case CaseEvidenceFiledNotification n:
                await OnEvidenceFiledAsync(n, ct);

                break;
            case CaseDmOutboundNotification n:
                await OnDmOutboundAsync(n, ct);

                break;
            case RestrictionAppliedNotification n:
                await OnRestrictionAppliedAsync(n.RestrictionId, ct);

                break;
            case RestrictionLiftedNotification n:
                await OnRestrictionLiftedAsync(n, ct);

                break;
            case CaseCategoryPanelsChangedNotification:
                await SyncCategoryPanelsAsync(ct);

                break;
        }
    }

    // ============================================================
    // Case submitted: validation-queue post + confirmation DM
    // ============================================================

    private async Task OnCaseSubmittedAsync(Guid caseId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var caseService = scope.ServiceProvider.GetRequiredService<ICaseService>();
        var c = await caseService.GetAsync(caseId, ct);
        if (c == null)
        {
            return;
        }

        // Design §5: a submission enters the validation queue first; the review thread only
        // exists once a reviewer claims it there. Tracking cases arrive pre-claimed (the filer
        // validated by filing), so they skip the queue and get their thread from the claim
        // transition that follows immediately.
        if (c.Status == CaseStatus.Submitted)
        {
            await PostSubmissionToQueueAsync(caseService, c, ct);
        }

        // Confirmation DM. Tracking cases are handled outside the system; nobody is DM'd about
        // them. (Evidence intake is disabled for now, so no attach button is offered.)
        var user = CanDmLifecycle(c) ? await GetUserAsync(c.AppellantDiscordUserId) : null;
        if (user != null)
        {
            var dmEmbed = new EmbedBuilder()
                .WithTitle(IsStaffFiled(c) ? "📁 A case has been opened for you" : "📥 Submission received")
                .WithColor(Color.Blue)
                .WithDescription(IsStaffFiled(c)
                    ? $"QIAD staff have opened a **{TypeName(c)}** case concerning you. You will be kept informed here."
                    : $"Your **{TypeName(c)}** submission has been received by QIAD. You will be kept informed here.")
                .AddField("Reference", CaseRef(c), true)
                .WithFooter("QIAD Helper")
                .WithCurrentTimestamp()
                .Build();

            if (await _dm.TrySendAsync(user, embed: dmEmbed, ct: ct))
            {
                var dmChannel = await user.CreateDMChannelAsync();
                await caseService.RecordDmChannelAsync(c.Id, dmChannel.Id, ct);
            }
        }
    }

    /// <summary>
    ///     Posts the submission into the validation-queue channel with the three paths out of
    ///     validation (claim / void / misc) as buttons. The message is the case's queue anchor:
    ///     it is edited in place as the case advances.
    /// </summary>
    private async Task PostSubmissionToQueueAsync(ICaseService caseService, Case c, CancellationToken ct)
    {
        var channelId = await _channelConfig.GetChannelIdAsync(QiadConfigKeys.SubmissionQueueChannelId, ct);
        if (channelId == 0 || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            _logger.LogWarning(
                "No submission queue channel configured; case {CaseId} is only reachable from the admin panel",
                c.Id);
            await _errorLog.LogErrorAsync("No submission queue configured",
                $"`{CaseRef(c)}` ({c.Category?.Name}) was submitted, but no submission queue channel is configured " +
                "(admin panel → Settings → SubmissionQueueChannelId). The case is only reachable from the admin panel.");

            return;
        }

        try
        {
            var message = await channel.SendMessageAsync(
                embed: BuildQueueEmbed(c, 0),
                components: await BuildQueueComponentsAsync(c, ct));
            await caseService.RecordQueueMessageAsync(c.Id, channelId, message.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post case {CaseId} to the submission queue", c.Id);
            await _errorLog.LogErrorAsync("Submission queue post failed",
                $"`{CaseRef(c)}` could not be posted to the submission queue; it is only reachable from the admin panel.",
                ex);
        }
    }

    // ============================================================
    // Status transitions
    // ============================================================

    private async Task OnStatusChangedAsync(CaseStatusChangedNotification n, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var caseService = scope.ServiceProvider.GetRequiredService<ICaseService>();
        var context = scope.ServiceProvider.GetRequiredService<QiadContext>();
        var c = await caseService.GetAsync(n.CaseId, ct);
        if (c == null)
        {
            return;
        }

        switch (n.NewStatus)
        {
            case CaseStatus.UnderReview when n.OldStatus == CaseStatus.Submitted:
                await OnClaimedAsync(caseService, context, c, ct);

                break;

            case CaseStatus.UnderReview when n.OldStatus == CaseStatus.AwaitingRatification:
                // The outcome always pings whoever claimed/drafted.
                await PostCaseUpdateAsync(c,
                    text: c.ClaimedById != null ? $"<@{c.ClaimedById}>" : null,
                    embed: new EmbedBuilder()
                        .WithTitle("↩️ Decision overturned")
                        .WithColor(Color.Orange)
                        .WithDescription("The case is back under review.")
                        .Build());

                break;

            case CaseStatus.UnderReview or CaseStatus.Submitted when n.OldStatus == CaseStatus.AwaitingVoidRatification:
                await PostVoidOutcomeAsync(c,
                    new EmbedBuilder()
                        .WithTitle("❌ Void request rejected")
                        .WithColor(Color.Orange)
                        .WithDescription(c.Status == CaseStatus.Submitted
                            ? "The submission is back in the queue."
                            : "The case continues.")
                        .Build());

                break;

            case CaseStatus.AwaitingRatification:
                await OnAwaitingRatificationAsync(context, c, ct);

                break;

            case CaseStatus.AwaitingVoidRatification:
                await OnAwaitingVoidRatificationAsync(caseService, context, c, ct);

                break;

            case CaseStatus.AwaitingExecution:
                await OnAwaitingExecutionAsync(caseService, c, ct);

                break;

            case CaseStatus.Closed or CaseStatus.Voided when n.OldStatus == CaseStatus.Sealed:
                await PostCaseUpdateAsync(c,
                    embed: new EmbedBuilder()
                        .WithTitle("🔓 Case unsealed")
                        .WithColor(Color.DarkGrey)
                        .WithDescription("This case is visible in the public database again.")
                        .Build());

                break;

            case CaseStatus.Closed:
                await OnClosedAsync(caseService, c, ct);

                break;

            case CaseStatus.Voided:
                await OnVoidedAsync(c, ct);

                break;

            case CaseStatus.Sealed:
                await PostCaseUpdateAsync(c,
                    embed: new EmbedBuilder()
                        .WithTitle("🔒 Case sealed")
                        .WithColor(Color.DarkGrey)
                        .WithDescription("This case has been sealed and is hidden from the public database.")
                        .Build());

                break;
        }

        // Whatever happened, the queue anchor re-renders so the channel reflects current state
        // (claimed → thread link, voided/closed → terminal, void-rejected → buttons restored).
        await UpdateQueueMessageAsync(context, c, ct);
    }

    private async Task OnClaimedAsync(ICaseService caseService, QiadContext context, Case c, CancellationToken ct)
    {
        // Validation passed, the number exists: the review thread starts NOW (design §5 — the
        // thread is a review artifact, not a submission artifact). A pre-existing thread only
        // occurs for cases from before the queue-first flow; those just get the number in the title.
        var thread = await GetThreadAsync(c);
        if (thread == null)
        {
            await CreateReviewThreadAsync(caseService, context, c, ct);
        }
        else
        {
            try
            {
                var name = ThreadTitle(c);
                await thread.ModifyAsync(t => t.Name = name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not rename thread for case {CaseId}", c.Id);
            }

            await PostToThreadAsync(c,
                embed: new EmbedBuilder()
                    .WithTitle($"📌 Claimed: {CaseRef(c)}")
                    .WithColor(Color.Green)
                    .WithDescription($"Claimed by <@{c.ClaimedById}>; public as **{CaseRef(c)}** (*Pending*).")
                    .Build());
        }

        // No DM at claim (deliberate); the appellant hears back at the outcome.
        // Public "case opened" announcement (the Form's "Add Case Opened Message ID" — now automatic).
        var announcementId = await AnnounceAsync(AnnouncementEmbed(c, closed: false), ct);
        if (announcementId != null)
        {
            await caseService.RecordAnnouncementMessageAsync(c.Id, announcementId.Value, false, ct);
        }
    }

    /// <summary>Public #case-updates notification; the details live in the public database.</summary>
    private static Embed AnnouncementEmbed(Case c, bool closed)
    {
        return new EmbedBuilder()
            .WithTitle(closed ? "📁 Case closed" : "📂 Case opened")
            .WithColor(closed ? Color.DarkGrey : Color.Blue)
            .WithDescription(closed
                ? "A case has been closed. The outcome is available in the public QIAD database."
                : "A new case has been opened. Updates will be disclosed in the public QIAD database.")
            .AddField(c.NumberSeries == CaseNumberSeries.Incidents ? "Incident Number" : "Case Number",
                $"`#{c.CaseNumber}`", true)
            .WithFooter("QIAD Helper")
            .WithCurrentTimestamp()
            .Build();
    }

    /// <summary>
    ///     Creates the review thread at claim time, starter post carrying the case facts, the
    ///     claim credit, and the review action buttons — then mirrors any evidence the appellant
    ///     filed while the submission sat in the validation queue.
    /// </summary>
    private async Task CreateReviewThreadAsync(ICaseService caseService,
                                               QiadContext context,
                                               Case c,
                                               CancellationToken ct)
    {
        var forumChannelId = c.Category?.ForumChannelId
                             ?? await _channelConfig.GetChannelIdAsync(QiadConfigKeys.DefaultForumChannelId, ct);

        if (forumChannelId == 0 || _client.GetChannel(forumChannelId) is not IForumChannel forum)
        {
            _logger.LogWarning(
                "No forum channel configured for category {Category}; case {CaseId} has no review thread",
                c.Category?.Name, c.Id);
            await _errorLog.LogErrorAsync("No review forum configured",
                $"`{CaseRef(c)}` ({c.Category?.Name}) was claimed but no forum channel is configured for the category or as default.");

            return;
        }

        try
        {
            var starter = BuildCaseEmbed(c, "📌 Under review", Color.Green)
                .WithDescription(Truncate(c.AppealText, 4000));
            if (c.ClaimedById != null)
            {
                starter.AddField("Claimed by", $"<@{c.ClaimedById}> ({c.ClaimedByUsername})", true);
            }

            var thread = await forum.CreatePostAsync(
                ThreadTitle(c),
                ThreadArchiveDuration.OneWeek,
                embed: starter.Build(),
                components: ShowStaffButtons ? BuildThreadActionButtons(c.Id) : null);

            await caseService.RecordForumThreadAsync(c.Id, forumChannelId, thread.Id, ct);
            c.ForumChannelId = forumChannelId;
            c.ForumThreadId = thread.Id;

            // Evidence that arrived while the case waited in the queue moves into the thread.
            var pending = await context.CaseEvidenceItems.AsNoTracking()
                .Where(i => i.CaseId == c.Id && i.MirroredMessageId == null)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync(ct);
            foreach (var item in pending)
            {
                var message = await thread.SendMessageAsync(embed: BuildEvidenceEmbed(item).Build());
                await caseService.RecordEvidenceMirroredAsync(item.Id, message.Id, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create review thread for case {CaseId}", c.Id);
            await _errorLog.LogErrorAsync("Review thread creation failed",
                $"`{CaseRef(c)}` has no review thread; create one manually or re-check forum permissions.",
                ex);
        }
    }

    private async Task OnAwaitingRatificationAsync(QiadContext context, Case c, CancellationToken ct)
    {
        var minRank = c.Category?.VerdictRatifierMinRank ?? QiadRank.Secretary;
        var mention = await BuildRankMentionAsync(context, minRank, ct);

        var text = $"{mention} · ratification ({minRank.ShortName()}+)" +
                   (ShowStaffButtons ? "" : " — /issue-verdict or /overturn");

        await PostToThreadAsync(c,
            text: text,
            embed: new EmbedBuilder()
                .WithTitle($"⚖️ Decision: {c.Verdict}")
                .WithColor(Color.Gold)
                .WithDescription(Truncate(c.DecisionReasoning, 4000))
                .WithFooter($"Decision by {c.ClaimedByUsername}")
                .Build(),
            components: ShowStaffButtons
                ? new ComponentBuilder()
                    .WithButton("Uphold", $"qiad-ratify:{c.Id}:uphold", ButtonStyle.Success, new Emoji("✅"))
                    .WithButton("Overturn", $"qiad-ratify:{c.Id}:overturn", ButtonStyle.Danger, new Emoji("↩️"))
                    .Build()
                : null);
    }

    private async Task OnAwaitingVoidRatificationAsync(ICaseService caseService,
                                                       QiadContext context,
                                                       Case c,
                                                       CancellationToken ct)
    {
        var minRank = c.Category?.VoidRatifierMinRank ?? QiadRank.InvestigativeAffairs;
        var mention = await BuildRankMentionAsync(context, minRank, ct);

        var buttons = ShowStaffButtons
            ? new ComponentBuilder()
                .WithButton("Approve void", $"qiad-void-ratify:{c.Id}:approve", ButtonStyle.Danger, new Emoji("🗑️"))
                .WithButton("Reject request", $"qiad-void-ratify:{c.Id}:reject", ButtonStyle.Secondary)
                .Build()
            : null;

        var text = $"{mention} · void ratification ({minRank.ShortName()}+)" +
                   (ShowStaffButtons ? "" : " — /reject-case or /deny-void");
        var embed = BuildCaseEmbed(c, "🗑️ Void request", Color.DarkRed)
            .WithDescription(Truncate(c.AppealText, 4000))
            .AddField("Void reason", c.VoidReason?.DisplayName() ?? "Other", true)
            .AddField("Reasoning", Truncate(c.VoidReasoning, 1000) ?? "(none)")
            .AddField("Requested by", c.VoidRequestedById != null ? $"<@{c.VoidRequestedById}>" : "?", true)
            .Build();

        // One post per request in the void-requests forum (the old #voiding-requests shape);
        // ratification happens there. Inline (queue/thread) only as fallback.
        var forumId = await _channelConfig.GetChannelIdAsync(QiadConfigKeys.VoidRequestsForumChannelId, ct);
        if (forumId != 0 && _client.GetChannel(forumId) is IForumChannel forum)
        {
            try
            {
                var post = await forum.CreatePostAsync(
                    VoidPostTitle(c),
                    ThreadArchiveDuration.OneWeek,
                    text: text,
                    embed: embed,
                    components: buttons,
                    allowedMentions: new AllowedMentions(AllowedMentionTypes.Roles | AllowedMentionTypes.Users));
                await caseService.RecordVoidRequestThreadAsync(c.Id, post.Id, ct);
                c.VoidRequestThreadId = post.Id;

                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create the void-request post for case {CaseId}", c.Id);
            }
        }
        else
        {
            await _errorLog.LogErrorAsync("No void-requests forum configured",
                $"`{CaseRef(c)}`: void request posted inline instead (Settings → VoidRequestsForumChannelId).");
        }

        await PostCaseUpdateAsync(c, text, embed, buttons);
    }

    private async Task OnClosedAsync(ICaseService caseService, Case c, CancellationToken ct)
    {
        var (emoji, color) = c.Verdict switch
        {
            CaseVerdict.Accepted => ("🟢", Color.Green),
            CaseVerdict.Rejected => ("🔴", Color.Red),
            _                    => ("⚪", Color.LightGrey)
        };

        var closedEmbed = new EmbedBuilder()
            .WithTitle($"{emoji} Case closed: {c.Verdict}")
            .WithColor(color)
            .WithDescription(Truncate(c.Reasoning, 4000))
            .WithFooter(c.IsMisc
                ? $"Misc filing ({c.MiscFilingType}) by {c.ClosedByUsername}"
                : $"Ratified by {c.RatifiedByUsername}");
        if (c.IsMisc && !string.IsNullOrWhiteSpace(c.ExternalEvidenceUrl))
        {
            closedEmbed.AddField("Evidence link", Truncate(c.ExternalEvidenceUrl, 500));
        }

        // The closer IS the executor (claimer/filer pressed the button), so no ping here;
        // the "decision upheld" ping already happened at ratification.
        var closed = closedEmbed.Build();
        await PostCaseUpdateAsync(c, embed: closed);

        if (c.IsMisc)
        {
            await PostMiscOutcomeAsync(c, closed);
        }

        var user = CanDmLifecycle(c) ? await GetUserAsync(c.AppellantDiscordUserId) : null;
        if (user != null)
        {
            await _dm.TrySendAsync(user, embed: new EmbedBuilder()
                .WithTitle($"{emoji} Your case has been closed: {c.Verdict}")
                .WithColor(color)
                .WithDescription(Truncate(c.Reasoning, 4000))
                .AddField("Reference", CaseRef(c), true)
                .WithFooter("QIAD Helper")
                .WithCurrentTimestamp()
                .Build(), ct: ct);
        }

        // Cases misc-filed straight out of the queue never got a number and are not in the
        // public database, so there is nothing to announce publicly.
        if (c.CaseNumber != null)
        {
            var announcementId = await AnnounceAsync(AnnouncementEmbed(c, closed: true), ct);
            if (announcementId != null)
            {
                await caseService.RecordAnnouncementMessageAsync(c.Id, announcementId.Value, true, ct);
            }
        }

        await ArchiveThreadAsync(c);
    }

    private async Task OnVoidedAsync(Case c, CancellationToken ct)
    {
        var reasonTag = c.VoidReason?.DisplayName();
        var outcome = new EmbedBuilder()
            .WithTitle("✅ Void request approved: case voided")
            .WithColor(Color.DarkGrey)
            .WithDescription((reasonTag != null ? $"**{reasonTag}**\n" : "") + Truncate(c.VoidReasoning, 3500))
            .WithFooter($"Approved by {c.ClosedByUsername}")
            .Build();

        await PostVoidOutcomeAsync(c, outcome);

        // The review thread (post-claim voids) records the outcome before it closes.
        if (c.ForumThreadId != null && c.VoidRequestThreadId != null)
        {
            await PostToThreadAsync(c, embed: outcome);
        }

        var user = CanDmLifecycle(c) ? await GetUserAsync(c.AppellantDiscordUserId) : null;
        if (user != null)
        {
            await _dm.TrySendAsync(user, embed: new EmbedBuilder()
                .WithTitle("🗑️ Your submission was voided")
                .WithColor(Color.DarkGrey)
                .WithDescription((reasonTag != null ? $"**{reasonTag}**\n" : "") +
                                 Truncate(c.VoidReasoning, 3500))
                .AddField("Reference", CaseRef(c), true)
                .WithFooter("QIAD Helper")
                .WithCurrentTimestamp()
                .Build(), ct: ct);
        }

        await ArchiveThreadAsync(c);
    }

    /// <summary>
    ///     Posts a void-ratification outcome into the void-request post (pinging the requester,
    ///     per process) and concludes that post. Falls back inline when no post exists.
    /// </summary>
    private async Task PostVoidOutcomeAsync(Case c, Embed outcome)
    {
        var ping = c.VoidRequestedById != null ? $"<@{c.VoidRequestedById}>" : null;

        if (c.VoidRequestThreadId is { } postId && await GetThreadByIdAsync(postId) is { } post)
        {
            try
            {
                await post.SendMessageAsync(ping, embed: outcome,
                    allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
                await post.ModifyAsync(t =>
                {
                    t.Archived = true;
                    t.Locked = true;
                });

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not conclude the void-request post for case {CaseId}", c.Id);
            }
        }

        await PostCaseUpdateAsync(c, ping, outcome);
    }

    /// <summary>
    ///     Decision made, case not yet closed: the misc post gets its Close button; an upheld
    ///     verdict pings the claimer with the Execute button.
    /// </summary>
    private async Task OnAwaitingExecutionAsync(ICaseService caseService, Case c, CancellationToken ct)
    {
        if (c.IsMisc)
        {
            await CreateMiscFilingPostAsync(caseService, c, ct);

            return;
        }

        var ping = c.ClaimedById != null ? $"<@{c.ClaimedById}>" : null;
        var text = ShowStaffButtons
            ? ping
            : ping is null ? "Run /execute in this thread to close." : $"{ping} · run /execute to close";

        await PostCaseUpdateAsync(c,
            text: text,
            embed: new EmbedBuilder()
                .WithTitle($"✅ Decision upheld: {c.Verdict}")
                .WithColor(Color.Green)
                .WithDescription(Truncate(c.Reasoning, 4000))
                .WithFooter($"Upheld by {c.RatifiedByUsername}")
                .Build(),
            components: ShowStaffButtons
                ? new ComponentBuilder()
                    .WithButton("Execute decision", $"qiad-execute:{c.Id}", ButtonStyle.Success, new Emoji("✅"))
                    .Build()
                : null);
    }

    /// <summary>
    ///     Post in the misc-filings forum, created at filing time (the old misc-appeals forum
    ///     shape). Carries the Close button that asks for the separate public reasoning.
    /// </summary>
    private async Task CreateMiscFilingPostAsync(ICaseService caseService, Case c, CancellationToken ct)
    {
        var embedBuilder = BuildCaseEmbed(c, "⚡ Misc filing", Color.LightGrey)
            .WithDescription(Truncate(c.DecisionReasoning, 4000))
            .AddField("Filing type", c.MiscFilingType?.ToString() ?? "?", true)
            .AddField("Filed by", c.ModifiedById != null ? $"<@{c.ModifiedById}>" : "?", true);
        if (!ShowStaffButtons)
        {
            embedBuilder.AddField("To close", "Run /close-misc in this post", true);
        }

        var embed = embedBuilder.Build();
        var buttons = ShowStaffButtons
            ? new ComponentBuilder()
                .WithButton("Close (public reasoning)", $"qiad-misc-close:{c.Id}", ButtonStyle.Success, new Emoji("📁"))
                .Build()
            : null;

        var forumId = await _channelConfig.GetChannelIdAsync(QiadConfigKeys.MiscFilingsForumChannelId, ct);
        if (forumId != 0 && _client.GetChannel(forumId) is IForumChannel forum)
        {
            try
            {
                var post = await forum.CreatePostAsync(
                    MiscPostTitle(c), ThreadArchiveDuration.OneWeek, embed: embed, components: buttons);
                await caseService.RecordMiscFilingThreadAsync(c.Id, post.Id, ct);
                c.MiscFilingThreadId = post.Id;

                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create the misc-filing post for case {CaseId}", c.Id);
            }
        }
        else
        {
            await _errorLog.LogErrorAsync("No misc-filings forum configured",
                $"`{CaseRef(c)}`: misc filing posted inline instead (Settings → MiscFilingsForumChannelId).");
        }

        // Fallback keeps the filing closable where the case lives.
        await PostCaseUpdateAsync(c, embed: embed, components: buttons);
    }

    /// <summary>Concludes the misc post once the filing is closed with its public reasoning.</summary>
    private async Task PostMiscOutcomeAsync(Case c, Embed outcome)
    {
        if (c.MiscFilingThreadId is not { } postId || await GetThreadByIdAsync(postId) is not { } post)
        {
            return;
        }

        try
        {
            await post.SendMessageAsync(embed: outcome);
            await post.ModifyAsync(t =>
            {
                t.Archived = true;
                t.Locked = true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not conclude the misc-filing post for case {CaseId}", c.Id);
        }
    }

    // ============================================================
    // Evidence mirroring
    // ============================================================

    private async Task OnEvidenceFiledAsync(CaseEvidenceFiledNotification n, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QiadContext>();
        var caseService = scope.ServiceProvider.GetRequiredService<ICaseService>();

        var item = await context.CaseEvidenceItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == n.EvidenceItemId, ct);
        var c = await caseService.GetAsync(n.CaseId, ct);
        if (item == null || c == null)
        {
            return;
        }

        if (c.ForumThreadId != null)
        {
            var message = await PostToThreadAsync(c, embed: BuildEvidenceEmbed(item).Build());
            if (message != null)
            {
                await caseService.RecordEvidenceMirroredAsync(item.Id, message.Id, ct);
            }
        }
        else
        {
            // Still in the validation queue: surface it there for the validators, but do NOT
            // mark it mirrored — the review thread gets the authoritative copy at claim.
            await PostToQueueChannelAsync(c, embed: BuildEvidenceEmbed(item).Build());
            await UpdateQueueMessageAsync(context, c, ct);
        }
    }

    private async Task OnDmOutboundAsync(CaseDmOutboundNotification n, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var caseService = scope.ServiceProvider.GetRequiredService<ICaseService>();
        var c = await caseService.GetAsync(n.CaseId, ct);
        if (c == null || c.AppellantDiscordUserId == 0)
        {
            return;
        }

        // Deliberate staff message: allowed even on tracking cases, as long as a user is on file.
        var user = await GetUserAsync(c.AppellantDiscordUserId);
        if (user != null)
        {
            await _dm.TrySendAsync(user, embed: new EmbedBuilder()
                .WithTitle("📨 Message from QIAD")
                .WithColor(Color.Blue)
                .WithDescription(Truncate(n.Content, 4000))
                .AddField("Reference", CaseRef(c), true)
                .WithFooter("QIAD Helper")
                .WithCurrentTimestamp()
                .Build(), ct: ct);
        }
    }

    // ============================================================
    // Restrictions (design §7.6 — replaces ?role5ever + /remind)
    // ============================================================

    private async Task OnRestrictionAppliedAsync(Guid restrictionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QiadContext>();
        var r = await context.Restrictions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == restrictionId, ct);
        if (r == null)
        {
            return;
        }

        var guild = _client.GetGuild(r.GuildId);
        var role = guild?.GetRole(r.RoleId);
        if (guild == null || role == null)
        {
            await _errorLog.LogErrorAsync("Restriction role apply failed",
                $"Guild {r.GuildId} or role {r.RoleId} not found for restriction `{r.Id}` (target <@{r.TargetDiscordUserId}>).");

            return;
        }

        try
        {
            var member = guild.GetUser(r.TargetDiscordUserId)
                         ?? (IGuildUser?)await _client.Rest.GetGuildUserAsync(r.GuildId, r.TargetDiscordUserId);
            if (member == null)
            {
                await _errorLog.LogErrorAsync("Restriction role apply failed",
                    $"User <@{r.TargetDiscordUserId}> is not a member of {guild.Name}; restriction `{r.Id}` was recorded but no role was applied.");

                return;
            }

            await member.AddRoleAsync(role.Id);
            _logger.LogInformation("Applied restriction role {Role} to {User} (restriction {Id})",
                role.Name, r.TargetDiscordUserId, r.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed applying restriction role for {RestrictionId}", r.Id);
            await _errorLog.LogErrorAsync("Restriction role apply failed", ex.Message, ex);

            return;
        }

        var user = await GetUserAsync(r.TargetDiscordUserId);
        if (user != null)
        {
            var until = r.ExpiresAt == null
                ? "This restriction is **permanent** unless lifted by staff."
                : $"It expires automatically <t:{new DateTimeOffset(r.ExpiresAt.Value).ToUnixTimeSeconds()}:R>.";
            await _dm.TrySendAsync(user, embed: new EmbedBuilder()
                .WithTitle($"⛔ Restriction applied: {role.Name}")
                .WithColor(Color.Red)
                .WithDescription($"**Reason:** {Truncate(r.Reason, 1000)}\n\n{until}")
                .WithFooter("QIAD Helper")
                .WithCurrentTimestamp()
                .Build(), ct: ct);
        }

        if (r.CaseId != null)
        {
            await using var caseScope = _scopeFactory.CreateAsyncScope();
            var caseService = caseScope.ServiceProvider.GetRequiredService<ICaseService>();
            var c = await caseService.GetAsync(r.CaseId.Value, ct);
            if (c != null)
            {
                await PostToThreadAsync(c, embed: new EmbedBuilder()
                    .WithTitle("⛔ Restriction applied")
                    .WithColor(Color.Red)
                    .WithDescription($"**{role.Name}** applied to <@{r.TargetDiscordUserId}> by <@{r.AppliedById}>.\n" +
                                     $"Expires: {(r.ExpiresAt == null ? "never (permanent)" : $"<t:{new DateTimeOffset(r.ExpiresAt.Value).ToUnixTimeSeconds()}:f>")}")
                    .Build());
            }
        }
    }

    private async Task OnRestrictionLiftedAsync(RestrictionLiftedNotification n, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QiadContext>();
        var r = await context.Restrictions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == n.RestrictionId, ct);
        if (r == null)
        {
            return;
        }

        var guild = _client.GetGuild(r.GuildId);
        try
        {
            var member = guild?.GetUser(r.TargetDiscordUserId)
                         ?? (IGuildUser?)await _client.Rest.GetGuildUserAsync(r.GuildId, r.TargetDiscordUserId);
            if (member != null && member.RoleIds.Contains(r.RoleId))
            {
                await member.RemoveRoleAsync(r.RoleId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed removing restriction role for {RestrictionId}", r.Id);
            await _errorLog.LogErrorAsync("Restriction role removal failed", ex.Message, ex);

            return;
        }

        var user = await GetUserAsync(r.TargetDiscordUserId);
        if (user != null)
        {
            await _dm.TrySendAsync(user, embed: new EmbedBuilder()
                .WithTitle(n.WasExpiry ? "✅ Your restriction has expired" : "✅ Your restriction was lifted")
                .WithColor(Color.Green)
                .WithDescription(n.WasExpiry
                    ? "The restriction applied to you has run out and the role was removed automatically."
                    : "Staff lifted your restriction early and the role was removed.")
                .WithFooter("QIAD Helper")
                .WithCurrentTimestamp()
                .Build(), ct: ct);
        }

        _logger.LogInformation("Restriction {Id} lifted (expiry={WasExpiry})", r.Id, n.WasExpiry);
    }

    // ============================================================
    // Intake panels (design §7.3 step 1)
    // ============================================================

    public async Task SyncCategoryPanelsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QiadContext>();

        var activeById = await context.CaseCategories
            .Where(c => c.IsActive)
            .ToDictionaryAsync(c => c.Id, ct);

        var categories = activeById.Values
            .Where(c => c.ShowOnPublicForm && c.SubmissionChannelId != null)
            .OrderBy(c => c.SortOrder)
            .ToList();

        foreach (var category in categories)
        {
            try
            {
                if (_client.GetChannel(category.SubmissionChannelId!.Value) is not IMessageChannel channel)
                {
                    _logger.LogWarning("Panel channel {ChannelId} for category {Name} not found",
                        category.SubmissionChannelId, category.Name);

                    continue;
                }

                var embed = new EmbedBuilder()
                    .WithTitle($"{category.Name}")
                    .WithColor(Color.Blue)
                    .WithDescription(Truncate(
                        string.Join("\n\n",
                            new[] { category.Description, category.PanelText }
                                .Where(s => !string.IsNullOrWhiteSpace(s))!), 4000))
                    .WithFooter("QIAD Helper")
                    .WithCurrentTimestamp()
                    .Build();

                // Panel shape from the live system: "Start an Appeal" plus, where enabled,
                // "Start an Unfair Appeal" filing the SAME category with the unfair flag.
                var buttons = new ComponentBuilder()
                    .WithButton(category.ButtonLabel,
                        $"qiad-open:{category.Id}:{Modules.CasePanelModule.KindAppeal}", ButtonStyle.Primary);
                if (category.OfferUnfairAppeals)
                {
                    buttons.WithButton(category.UnfairButtonLabel ?? "Start an Unfair Appeal",
                        $"qiad-open:{category.Id}:{Modules.CasePanelModule.KindUnfair}", ButtonStyle.Primary);
                }

                var components = buttons.Build();

                IUserMessage? existing = null;
                if (category.PanelMessageId != null)
                {
                    existing = await channel.GetMessageAsync(category.PanelMessageId.Value) as IUserMessage;
                }

                if (existing != null && existing.Author.Id == _client.CurrentUser.Id)
                {
                    await existing.ModifyAsync(m =>
                    {
                        m.Embed = embed;
                        m.Components = components;
                    });
                }
                else
                {
                    var message = await channel.SendMessageAsync(embed: embed, components: components);
                    category.PanelMessageId = message.Id;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync intake panel for category {Name}", category.Name);
            }
        }

        await context.SaveChangesAsync(ct);
        _logger.LogInformation("Synced {Count} intake panel(s)", categories.Count);
    }

    // ============================================================
    // Shared helpers
    // ============================================================

    /// <summary>Unfair appeals read "Unfair Game Ban Appeal", matching the public display.</summary>
    private static string TypeName(Case c) =>
        c.IsUnfair ? $"Unfair {c.Category?.Name}" : c.Category?.Name ?? "?";

    private static string ThreadTitle(Case c) =>
        Truncate($"[{CaseRef(c)}] {TypeName(c)} · {c.AppellantDiscordUsername ?? "external"}", 100)!;

    /// <summary>Old bulletin format: "Discord Username / Roblox Username - Appeal Type".</summary>
    private static string VoidPostTitle(Case c) =>
        Truncate($"{c.AppellantDiscordUsername ?? "unknown"} / {c.SubjectRobloxUsername ?? "unknown"} - {TypeName(c)}",
            100)!;

    /// <summary>Old misc-appeals forum format: "Misc Appeal - DiscordUsername/RobloxUsername".</summary>
    private static string MiscPostTitle(Case c) =>
        Truncate($"Misc Appeal - {c.AppellantDiscordUsername ?? "unknown"}/{c.SubjectRobloxUsername ?? "unknown"}",
            100)!;

    /// <summary>"Case #12" / "Incident #3", or a short reference for unnumbered submissions.</summary>
    private static string CaseRef(Case c)
    {
        if (c.CaseNumber != null)
        {
            return c.NumberSeries == CaseNumberSeries.Incidents
                ? $"Incident #{c.CaseNumber}"
                : $"Case #{c.CaseNumber}";
        }

        return $"REF-{c.Id.ToString("N")[..8].ToUpperInvariant()}";
    }

    private static EmbedBuilder BuildCaseEmbed(Case c, string title, Color color)
    {
        // Field shapes follow the old forum formats: usernames WITH raw IDs, appeal type,
        // evidence link, request date/location — staff copy these into external tools.
        var builder = new EmbedBuilder()
            .WithTitle($"{title}: {CaseRef(c)}")
            .WithColor(color)
            .AddField("Category", TypeName(c), true)
            .AddField("Appellant", c.AppellantDiscordUserId == 0
                ? $"{c.AppellantDiscordUsername ?? "external"} (no Discord on file)"
                : $"<@{c.AppellantDiscordUserId}> ({c.AppellantDiscordUsername} · `{c.AppellantDiscordUserId}`)", true)
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (c.IsTracking)
        {
            builder.AddField("Tracking case", "No DMs are sent.", true);
        }

        if (c.SubjectRobloxUserId is { } robloxId)
        {
            // Resolved through Guardsman at intake; link straight to the profile.
            var display = c.SubjectRobloxUsername ?? robloxId.ToString(CultureInfo.InvariantCulture);
            builder.AddField("Subject (Roblox)",
                $"[{display}](https://www.roblox.com/users/{robloxId}/profile) (`{robloxId}`) · verified", true);
        }
        else if (!string.IsNullOrWhiteSpace(c.SubjectRobloxUsername))
        {
            builder.AddField("Subject (Roblox)", $"{c.SubjectRobloxUsername} · unverified", true);
        }

        if (c.SubjectDiscordUserId != null && c.SubjectDiscordUserId != c.AppellantDiscordUserId)
        {
            builder.AddField("Subject (Discord)",
                $"<@{c.SubjectDiscordUserId}> (`{c.SubjectDiscordUserId}`)", true);
        }

        if (!string.IsNullOrWhiteSpace(c.ReportedBanReason))
        {
            builder.AddField("Reported ban reason", Truncate(c.ReportedBanReason, 1000));
        }

        if (!string.IsNullOrWhiteSpace(c.ExternalEvidenceUrl))
        {
            builder.AddField("Evidence link", Truncate(c.ExternalEvidenceUrl, 500));
        }

        if (!string.IsNullOrWhiteSpace(c.RequestContext))
        {
            builder.AddField("Requested", Truncate(c.RequestContext, 200), true);
        }

        if (IsStaffFiled(c))
        {
            builder.AddField("Filed by staff", $"<@{c.CreatedById}> on the appellant's behalf", true);
        }

        return builder;
    }

    private static EmbedBuilder BuildEvidenceEmbed(CaseEvidenceItem item)
    {
        var builder = new EmbedBuilder()
            .WithTitle($"📎 Evidence filed by {(item.IsFromAppellant ? "the appellant" : "staff")}")
            .WithColor(Color.Teal)
            .WithFooter($"{item.SubmittedByUsername} · item {item.Id.ToString()[..8]}")
            .WithTimestamp(item.CreatedAt == default ? DateTimeOffset.UtcNow : new DateTimeOffset(item.CreatedAt));

        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            builder.WithDescription(Truncate(item.Text, 4000));
        }

        if (!string.IsNullOrWhiteSpace(item.AttachmentUrl))
        {
            builder.AddField("Attachment",
                $"[{item.AttachmentFileName ?? "file"}]({item.AttachmentUrl})");
            if (LooksLikeImage(item.AttachmentFileName))
            {
                builder.WithImageUrl(item.AttachmentUrl);
            }
        }

        return builder;
    }

    /// <summary>True when QIAD staff opened the case on the appellant's behalf (/open-case).</summary>
    private static bool IsStaffFiled(Case c) =>
        c.CreatedById != null && c.CreatedById != c.AppellantDiscordUserId;

    /// <summary>Automatic lifecycle DMs go out only for real appellant-facing cases.</summary>
    private static bool CanDmLifecycle(Case c) => !c.IsTracking && c.AppellantDiscordUserId != 0;

    /// <summary>
    ///     Whether staff-action buttons are drawn. False in Commands mode (staff use slash
    ///     commands instead); the surfaces and pings are still posted, just without buttons.
    /// </summary>
    private bool ShowStaffButtons => _options.StaffWorkflowMode != StaffWorkflowMode.Commands;

    private static MessageComponent BuildThreadActionButtons(Guid caseId)
    {
        // Review-stage actions only: claiming happens in the validation queue, before the thread
        // exists. Buttons validate state + rank server-side, so a stale button is a no-op with a
        // friendly error, never a wrong transition.
        return new ComponentBuilder()
            .WithButton("Submit verdict", $"qiad-verdict:{caseId}", ButtonStyle.Primary, new Emoji("⚖️"))
            .WithButton("Void request", $"qiad-void:{caseId}", ButtonStyle.Secondary, new Emoji("🗑️"))
            .WithButton("Misc file", $"qiad-misc:{caseId}", ButtonStyle.Secondary, new Emoji("⚡"))
            .WithButton("Restrict", $"qiad-restrict:{caseId}", ButtonStyle.Danger, new Emoji("⛔"))
            .Build();
    }

    /// <summary>
    ///     Renders the validation-queue anchor for the case's current state; the anchor is edited
    ///     in place on every transition so the queue channel never shows stale actions.
    /// </summary>
    private static Embed BuildQueueEmbed(Case c, int evidenceCount)
    {
        var (title, color) = c.Status switch
        {
            CaseStatus.Submitted                                => ("📥 Awaiting validation", Color.Blue),
            CaseStatus.AwaitingVoidRatification                 => ("🗑️ Void ratification pending", Color.DarkRed),
            CaseStatus.AwaitingExecution when c.IsMisc          => ("⚡ Misc filing pending", Color.Gold),
            CaseStatus.AwaitingExecution                        => ("✅ Upheld, awaiting execution", Color.Gold),
            CaseStatus.UnderReview or CaseStatus.AwaitingRatification => ("📌 Validated and claimed", Color.Green),
            CaseStatus.Voided                                   => ("🗑️ Voided", Color.DarkGrey),
            CaseStatus.Sealed                                   => ("🔒 Sealed", Color.DarkGrey),
            _                                                   => ("📁 Closed", Color.LightGrey)
        };

        var builder = BuildCaseEmbed(c, title, color)
            .WithDescription(Truncate(c.AppealText, 4000));

        switch (c.Status)
        {
            case CaseStatus.Submitted:
                if (evidenceCount > 0)
                {
                    builder.AddField("Evidence", $"{evidenceCount} item(s) in the replies", true);
                }

                break;

            case CaseStatus.AwaitingVoidRatification or CaseStatus.Voided:
                if (c.VoidReason != null)
                {
                    builder.AddField("Void reason", c.VoidReason.Value.DisplayName(), true);
                }

                builder.AddField("Void reasoning", Truncate(c.VoidReasoning, 1000) ?? "(none)");

                break;

            case CaseStatus.AwaitingExecution when c.IsMisc:
                builder.AddField("Filing type", c.MiscFilingType?.ToString() ?? "?", true);

                break;

            case CaseStatus.UnderReview or CaseStatus.AwaitingRatification or CaseStatus.AwaitingExecution:
                builder.AddField("Claimed by", $"<@{c.ClaimedById}> ({c.ClaimedByUsername})", true);

                break;

            case CaseStatus.Closed:
                builder.AddField(c.IsMisc ? $"Misc filing: {c.MiscFilingType}" : $"Verdict: {c.Verdict}",
                    Truncate(c.Reasoning, 1000) ?? "(none)");

                break;
        }

        return builder.Build();
    }

    /// <summary>Queue-anchor buttons: the three validation paths while Submitted, a thread link after.</summary>
    private async Task<MessageComponent> BuildQueueComponentsAsync(Case c, CancellationToken ct)
    {
        if (c.Status == CaseStatus.Submitted)
        {
            // In Commands mode staff triage with /claim-case ref:… (the REF is on the embed).
            return ShowStaffButtons
                ? new ComponentBuilder()
                    .WithButton("Claim", $"qiad-claim:{c.Id}", ButtonStyle.Success, new Emoji("📌"))
                    .WithButton("Void request", $"qiad-void:{c.Id}", ButtonStyle.Secondary, new Emoji("🗑️"))
                    .WithButton("Misc file", $"qiad-misc:{c.Id}", ButtonStyle.Secondary, new Emoji("⚡"))
                    .Build()
                : new ComponentBuilder().Build();
        }

        if (c.ForumThreadId != null)
        {
            var guildId = await GetMainGuildIdAsync(ct);
            if (guildId != 0)
            {
                return new ComponentBuilder()
                    .WithButton("Open review thread", style: ButtonStyle.Link,
                        url: $"https://discord.com/channels/{guildId}/{c.ForumThreadId}")
                    .Build();
            }
        }

        // Empty set clears the buttons on edit (terminal states, void-ratification pending).
        return new ComponentBuilder().Build();
    }

    /// <summary>Re-renders the case's queue anchor in place. No-op when the case never had one.</summary>
    private async Task UpdateQueueMessageAsync(QiadContext context, Case c, CancellationToken ct)
    {
        if (c.QueueChannelId is not { } channelId || c.QueueMessageId is not { } messageId)
        {
            return;
        }

        try
        {
            if (_client.GetChannel(channelId) is not IMessageChannel channel ||
                await channel.GetMessageAsync(messageId) is not IUserMessage message ||
                message.Author.Id != _client.CurrentUser.Id)
            {
                return;
            }

            var evidenceCount = c.Status == CaseStatus.Submitted
                ? await context.CaseEvidenceItems.CountAsync(i => i.CaseId == c.Id, ct)
                : 0;
            var embed = BuildQueueEmbed(c, evidenceCount);
            var components = await BuildQueueComponentsAsync(c, ct);

            await message.ModifyAsync(m =>
            {
                m.Embed = embed;
                m.Components = components;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update the queue message for case {CaseId}", c.Id);
        }
    }

    private async Task<ulong> GetMainGuildIdAsync(CancellationToken ct)
    {
        var guildId = await _channelConfig.GetChannelIdAsync(QiadConfigKeys.MainGuildId, ct);

        return guildId != 0 ? guildId : _options.MainGuildId;
    }

    private async Task<string> BuildRankMentionAsync(QiadContext context, QiadRank minRank, CancellationToken ct)
    {
        var guildId = await GetMainGuildIdAsync(ct);

        var roleIds = await context.QiadRankRoles.AsNoTracking()
            .Where(m => m.GuildId == guildId && m.Rank >= minRank)
            .Select(m => m.DiscordRoleId)
            .Distinct()
            .ToListAsync(ct);

        return roleIds.Count == 0
            ? $"*(no {minRank.ShortName()}+ role mapped; configure rank roles in the admin panel)*"
            : string.Join(" ", roleIds.Select(id => $"<@&{id}>"));
    }

    /// <summary>
    ///     Posts a case update where the case currently lives: the review thread once one exists,
    ///     otherwise the validation queue (as a reply under the case's anchor message).
    /// </summary>
    private Task<IUserMessage?> PostCaseUpdateAsync(Case c,
                                                    string? text = null,
                                                    Embed? embed = null,
                                                    MessageComponent? components = null)
        => c.ForumThreadId != null
            ? PostToThreadAsync(c, text, embed, components)
            : PostToQueueChannelAsync(c, text, embed, components);

    private async Task<IUserMessage?> PostToQueueChannelAsync(Case c,
                                                              string? text = null,
                                                              Embed? embed = null,
                                                              MessageComponent? components = null)
    {
        if (c.QueueChannelId is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            _logger.LogWarning("Case {CaseId} has no reachable validation-queue channel", c.Id);

            return null;
        }

        try
        {
            return await channel.SendMessageAsync(text, embed: embed, components: components,
                messageReference: c.QueueMessageId is { } messageId
                    ? new MessageReference(messageId, channelId, failIfNotExists: false)
                    : null,
                allowedMentions: new AllowedMentions(AllowedMentionTypes.Roles | AllowedMentionTypes.Users));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post to the queue channel for case {CaseId}", c.Id);

            return null;
        }
    }

    private async Task<IUserMessage?> PostToThreadAsync(Case c,
                                                        string? text = null,
                                                        Embed? embed = null,
                                                        MessageComponent? components = null)
    {
        var thread = await GetThreadAsync(c);
        if (thread == null)
        {
            _logger.LogWarning("Case {CaseId} has no reachable review thread", c.Id);

            return null;
        }

        try
        {
            return await thread.SendMessageAsync(text, embed: embed, components: components,
                allowedMentions: new AllowedMentions(AllowedMentionTypes.Roles | AllowedMentionTypes.Users));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post to thread {ThreadId} for case {CaseId}", c.ForumThreadId, c.Id);

            return null;
        }
    }

    private Task<IThreadChannel?> GetThreadAsync(Case c) =>
        c.ForumThreadId is { } threadId ? GetThreadByIdAsync(threadId) : Task.FromResult<IThreadChannel?>(null);

    private async Task<IThreadChannel?> GetThreadByIdAsync(ulong threadId)
    {
        if (_client.GetChannel(threadId) is IThreadChannel socketThread)
        {
            // Archived threads must be re-opened before we can post.
            if (socketThread.IsArchived)
            {
                try { await socketThread.ModifyAsync(t => t.Archived = false); }
                catch { }
            }

            return socketThread;
        }

        return await _client.Rest.GetChannelAsync(threadId) as IThreadChannel;
    }

    private async Task ArchiveThreadAsync(Case c)
    {
        var thread = await GetThreadAsync(c);
        if (thread == null)
        {
            return;
        }

        try
        {
            await thread.ModifyAsync(t =>
            {
                t.Archived = true;
                t.Locked = true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not archive thread for case {CaseId}", c.Id);
        }
    }

    private async Task<ulong?> AnnounceAsync(Embed embed, CancellationToken ct)
    {
        try
        {
            var channelId = await _channelConfig.GetChannelIdAsync(QiadConfigKeys.CaseUpdatesChannelId, ct);
            if (channelId == 0 || _client.GetChannel(channelId) is not IMessageChannel channel)
            {
                return null;
            }

            var message = await channel.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.None);

            return message.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post case-updates announcement");

            return null;
        }
    }

    private async Task<IUser?> GetUserAsync(ulong userId) =>
        _client.GetUser(userId) ?? (IUser?)await _client.Rest.GetUserAsync(userId);

    private static bool LooksLikeImage(string? fileName) =>
        fileName != null &&
        (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
         fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
         fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
         fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
         fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
