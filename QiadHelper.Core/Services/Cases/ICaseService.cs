using QiadHelper.Core.Data.Cases;

namespace QiadHelper.Core.Services.Cases;

/// <summary>
///     What an appellant files through the intake modal. Staff-created tracking ("dummy") cases
///     set <paramref name="IsTracking" /> + the filer fields: the matter is handled outside the
///     system, so the case is numbered immediately (public "Pending"), claimed by the filer, and
///     no appellant DMs are ever sent. <paramref name="AppellantDiscordUserId" /> may be 0 for
///     tracking cases with no Discord user on file.
/// </summary>
public sealed record SubmitCaseRequest(
    Guid CategoryId,
    ulong AppellantDiscordUserId,
    string? AppellantUsername,
    string AppealText,
    string? SubjectRobloxUsername = null,
    ulong? SubjectDiscordUserId = null,
    string? ReportedBanReason = null,
    long? SubjectRobloxUserId = null,
    ulong? FiledById = null,
    string? FiledByUsername = null,
    bool IsTracking = false,
    bool IsUnfair = false,
    string? ExternalEvidenceUrl = null,
    string? RequestContext = null);

/// <summary>
///     The case state machine (design §5) — one implementation shared by bot interactions and web
///     controllers. Every transition enforces its minimum rank server-side and publishes a typed
///     notification for the Discord side effects.
/// </summary>
public interface ICaseService
{
    Task<Case?> GetAsync(Guid caseId, CancellationToken ct = default);

    /// <summary>
    ///     Resolves a case from any of its staff Discord surfaces — the review thread, the
    ///     void-request post, or the misc-filing post (design §5). Used by the slash-command
    ///     workflow to target "the case I'm running this command in".
    /// </summary>
    Task<Case?> GetByThreadAsync(ulong threadId, CancellationToken ct = default);

    /// <summary>
    ///     Resolves an unnumbered (Submitted/queued) case by the short reference the bot prints
    ///     ("REF-XXXXXXXX", the first 8 hex of the case id). Numbered cases are addressed by their
    ///     number, not a reference, so only unnumbered cases are searched. Null when none match.
    /// </summary>
    Task<Case?> GetByReferenceAsync(string reference, CancellationToken ct = default);

    /// <summary>Creates the case in Submitted (no number, publicly invisible).</summary>
    Task<Case> SubmitAsync(SubmitCaseRequest request, CancellationToken ct = default);

    /// <summary>Validation passed: claims for the actor and mints the case number. PA+.</summary>
    Task<CaseActionResult> ClaimAsync(Guid caseId, StaffActor actor, CancellationToken ct = default);

    /// <summary>
    ///     Drafts the decision (verdict + INTERNAL reasoning for the ratifier, never public) and
    ///     moves to AwaitingRatification. PA+.
    /// </summary>
    Task<CaseActionResult> DraftVerdictAsync(Guid caseId,
                                             StaffActor actor,
                                             CaseVerdict verdict,
                                             string decisionReasoning,
                                             CancellationToken ct = default);

    /// <summary>
    ///     Uphold (→ AwaitingExecution; <paramref name="publicVerdictText" /> becomes the public
    ///     description, required) or overturn (back to review; text ignored). Gate: the
    ///     category's <c>VerdictRatifierMinRank</c> (default Secretary+). The claimer then
    ///     executes via <see cref="ExecuteAsync" /> to actually close.
    /// </summary>
    Task<CaseActionResult> RatifyAsync(Guid caseId,
                                       StaffActor actor,
                                       bool uphold,
                                       string? publicVerdictText,
                                       CancellationToken ct = default);

    /// <summary>
    ///     Executes an upheld decision: closes the case and, for REJECTED verdicts, applies the
    ///     category's rejection restriction role automatically. PA+ (the claimer is pinged).
    /// </summary>
    Task<CaseActionResult> ExecuteAsync(Guid caseId, StaffActor actor, CancellationToken ct = default);

    /// <summary>
    ///     Files a void request (invalid submission) with the categorized reason (the old
    ///     forum tags) and an appellant-facing reasoning. PA+.
    /// </summary>
    Task<CaseActionResult> RequestVoidAsync(Guid caseId,
                                            StaffActor actor,
                                            VoidReason reason,
                                            string voidReasoning,
                                            CancellationToken ct = default);

    /// <summary>
    ///     Approves or rejects a pending void request. On approval,
    ///     <paramref name="dmReason" /> (required) is the ratifier-authored reason DM'd to the
    ///     appellant (and public for numbered cases). Gate: the category's
    ///     <c>VoidRatifierMinRank</c> (default IA+).
    /// </summary>
    Task<CaseActionResult> RatifyVoidAsync(Guid caseId,
                                           StaffActor actor,
                                           bool approve,
                                           string? dmReason,
                                           CancellationToken ct = default);

    /// <summary>
    ///     Misc / protocol-bypass filing — NO ratification, but the case stays open
    ///     (AwaitingExecution) until <see cref="CloseMiscAsync" /> sets the public reasoning.
    ///     <paramref name="details" /> is internal; <paramref name="evidenceUrl" /> is the old
    ///     format's "Evidence Link". PA+.
    /// </summary>
    Task<CaseActionResult> FileMiscAsync(Guid caseId,
                                         StaffActor actor,
                                         MiscFilingType filingType,
                                         string details,
                                         string? evidenceUrl = null,
                                         CancellationToken ct = default);

    /// <summary>Closes a pending misc filing with its separate public reasoning. PA+.</summary>
    Task<CaseActionResult> CloseMiscAsync(Guid caseId,
                                          StaffActor actor,
                                          string publicReasoning,
                                          CancellationToken ct = default);

    /// <summary>Hides a terminal case from (or restores it to) the public view. Secretary+.</summary>
    Task<CaseActionResult> SetSealedAsync(Guid caseId,
                                          StaffActor actor,
                                          bool isSealed,
                                          CancellationToken ct = default);

    /// <summary>Attaches evidence (appellant DM button or staff in-thread) and publishes the mirror notification.</summary>
    Task<CaseActionResult> AddEvidenceAsync(Guid caseId,
                                            ulong submittedById,
                                            string submittedByUsername,
                                            bool isFromAppellant,
                                            string? text,
                                            string? attachmentUrl = null,
                                            string? attachmentFileName = null,
                                            ulong? sourceChannelId = null,
                                            ulong? sourceMessageId = null,
                                            CancellationToken ct = default);

    // ---- Plumbing setters the bot uses to record Discord IDs automatically (§1.1) ----

    Task RecordQueueMessageAsync(Guid caseId, ulong channelId, ulong messageId, CancellationToken ct = default);

    Task RecordVoidRequestThreadAsync(Guid caseId, ulong threadId, CancellationToken ct = default);

    Task RecordMiscFilingThreadAsync(Guid caseId, ulong threadId, CancellationToken ct = default);

    Task RecordForumThreadAsync(Guid caseId, ulong forumChannelId, ulong forumThreadId, CancellationToken ct = default);

    Task RecordAnnouncementMessageAsync(Guid caseId, ulong messageId, bool isClosedAnnouncement, CancellationToken ct = default);

    Task RecordDmChannelAsync(Guid caseId, ulong dmChannelId, CancellationToken ct = default);

    Task RecordEvidenceMirroredAsync(Guid evidenceItemId, ulong mirroredMessageId, CancellationToken ct = default);
}
