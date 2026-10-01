using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace QiadHelper.Core.Data.Cases;

/// <summary>
///     A QIAD case — an appeal (any category) or an investigation/incident. Single table,
///     discriminated by <see cref="CategoryId" /> (design §6). Created at submission with no
///     number; the case number is minted at claim (validation pass), which is also the moment
///     the case becomes visible in the public database.
/// </summary>
public class Case : QiadDataAbstract
{
    // ---- Classification ----

    public Guid CategoryId { get; set; }

    /// <summary>
    ///     Public number, null until the case is claimed. Unique within its
    ///     <see cref="NumberSeries" /> once assigned.
    /// </summary>
    public int? CaseNumber { get; set; }

    /// <summary>
    ///     Which numbering sequence the case draws from. Appeals share the "Cases" sequence;
    ///     investigations use the separate "Incidents" sequence (mirrors the Sheet's two tabs).
    /// </summary>
    public CaseNumberSeries NumberSeries { get; set; } = CaseNumberSeries.Cases;

    public CaseStatus Status { get; set; } = CaseStatus.Submitted;

    /// <summary>
    ///     Contests the punishment as unjust rather than asking for clemency (design §2.2:
    ///     one attempt only, requires an evidence-forum log). A flag, not a category: the case
    ///     keeps the punishment type it is contesting.
    /// </summary>
    public bool IsUnfair { get; set; }

    // ---- Subject (the banned/reported group member — NOT an operative entity) ----

    /// <summary>Roblox user ID of the case subject, when known/resolvable.</summary>
    public long? SubjectRobloxUserId { get; set; }

    /// <summary>Roblox username as typed/resolved at submission time (snapshot).</summary>
    [MaxLength(64)]
    public string? SubjectRobloxUsername { get; set; }

    /// <summary>Discord user ID of the case subject.</summary>
    public ulong? SubjectDiscordUserId { get; set; }

    // ---- Appellant (who filed and receives DMs; usually == subject) ----

    /// <summary>0 = no Discord user on file (tracking cases for externally handled matters).</summary>
    public ulong AppellantDiscordUserId { get; set; }

    [MaxLength(64)] public string? AppellantDiscordUsername { get; set; }

    /// <summary>Cached DM channel for status updates, once the first DM succeeds.</summary>
    public ulong? AppellantDmChannelId { get; set; }

    // ---- Submission content ----

    /// <summary>The punishment reason as reported by the appellant (the embed's "Reported Ban Reason").</summary>
    [MaxLength(1000)]
    public string? ReportedBanReason { get; set; }

    /// <summary>The appeal narrative / incident description from the submission modal.</summary>
    [Required]
    [MaxLength(4000)]
    public string AppealText { get; set; } = string.Empty;

    // ---- Review workflow ----

    /// <summary>Reviewer (PA/RA/AA) who claimed the case at validation pass.</summary>
    public ulong? ClaimedById { get; set; }

    [MaxLength(64)] public string? ClaimedByUsername { get; set; }

    public DateTime? ClaimedAt { get; set; }

    /// <summary>Drafted verdict; published once ratified.</summary>
    public CaseVerdict? Verdict { get; set; }

    /// <summary>
    ///     The reviewer's internal rationale filed with the drafted verdict, addressed at the
    ///     ratifier. Staff-only; NEVER shown publicly.
    /// </summary>
    [MaxLength(4000)]
    public string? DecisionReasoning { get; set; }

    /// <summary>
    ///     Public verdict text (the Sheet's "Description" column), authored by the RATIFIER when
    ///     upholding — not by the reviewer. Visible once closed. Misc filings write it directly.
    /// </summary>
    [MaxLength(4000)]
    public string? Reasoning { get; set; }

    /// <summary>Secretary+ who ratified the verdict (null for void/misc closures).</summary>
    public ulong? RatifiedById { get; set; }

    [MaxLength(64)] public string? RatifiedByUsername { get; set; }

    public DateTime? RatifiedAt { get; set; }

    // ---- Void path (invalid submission; IA+ ratifies) ----

    /// <summary>Categorized void reason — the old #voiding-requests forum tags.</summary>
    public VoidReason? VoidReason { get; set; }

    /// <summary>
    ///     Why the submission failed validation, when the case went down the void path.
    ///     Appellant-facing (DM'd on void; shown publicly for post-claim voids).
    /// </summary>
    [MaxLength(2000)]
    public string? VoidReasoning { get; set; }

    /// <summary>Who filed the (latest) void request; pinged with the ratification outcome.</summary>
    public ulong? VoidRequestedById { get; set; }

    /// <summary>The void-request post in the void forum, where ratification happens.</summary>
    public ulong? VoidRequestThreadId { get; set; }

    // ---- Misc / protocol-bypass path (PA files directly, no ratification) ----

    public bool IsMisc { get; set; }

    public MiscFilingType? MiscFilingType { get; set; }

    /// <summary>The documentation post in the misc-filings forum.</summary>
    public ulong? MiscFilingThreadId { get; set; }

    // ---- Old forum-format fields (misc appeals / staff filings; design §7.4 formats) ----

    /// <summary>The format's "Evidence Link": URL to logs/screenshots backing the filing.</summary>
    [MaxLength(500)]
    public string? ExternalEvidenceUrl { get; set; }

    /// <summary>
    ///     The format's "Date Of Request" + "Request Location" for matters that arrived outside
    ///     the system, e.g. "30.05.2026 via DMs to an IA". Free text on purpose.
    /// </summary>
    [MaxLength(200)]
    public string? RequestContext { get; set; }

    // ---- Optional public note (pending design §11.2 — may merge into Reasoning) ----

    [MaxLength(2000)]
    public string? DirectorsNote { get; set; }

    // ---- Discord integration (recorded automatically — no hand-copied message IDs) ----

    /// <summary>Channel the validation-queue message was posted in (design §5: validation stage).</summary>
    public ulong? QueueChannelId { get; set; }

    /// <summary>
    ///     The validation-queue message carrying the claim/void/misc buttons; edited in place as
    ///     the case advances so the queue always reflects the current state.
    /// </summary>
    public ulong? QueueMessageId { get; set; }

    /// <summary>Forum channel the staff review thread lives in (created at claim, not at submit).</summary>
    public ulong? ForumChannelId { get; set; }

    /// <summary>Staff review thread for this case.</summary>
    public ulong? ForumThreadId { get; set; }

    /// <summary>The public "#case-updates" post announcing the case was opened.</summary>
    public ulong? OpenedMessageId { get; set; }

    /// <summary>The public "#case-updates" post announcing the case was closed.</summary>
    public ulong? ClosedMessageId { get; set; }

    // ---- Lifecycle timestamps ----

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAt { get; set; }

    /// <summary>Who closed/archived the case (the Sheet's "Archived By" public column).</summary>
    public ulong? ClosedById { get; set; }

    [MaxLength(64)] public string? ClosedByUsername { get; set; }

    /// <summary>True for rows imported from the legacy Google Sheet (identity is best-effort there).</summary>
    public bool IsImported { get; set; }

    /// <summary>
    ///     A "dummy" case: the matter is handled outside the system (e.g. in DMs with an IA) and
    ///     this record exists so the public database is complete. Numbered at creation, no
    ///     appellant DMs, but the full close/void/seal lifecycle applies.
    /// </summary>
    public bool IsTracking { get; set; }

    // ---- Navigation ----

    [JsonIgnore] public virtual CaseCategory? Category { get; set; }

    [JsonIgnore]
    public virtual ICollection<CaseEvidenceItem> EvidenceItems { get; set; } = new List<CaseEvidenceItem>();
}

public enum CaseStatus
{
    /// <summary>Filed, awaiting validation. No number; invisible to the public.</summary>
    Submitted = 0,

    /// <summary>Claimed (validation passed, number minted); reviewer investigating. Public: "Pending".</summary>
    UnderReview = 10,

    /// <summary>Verdict drafted; Secretary+ (per-category configurable) pinged to uphold or overturn.</summary>
    AwaitingRatification = 20,

    /// <summary>Void request filed; IA+ (per-category configurable) pinged to ratify.</summary>
    AwaitingVoidRatification = 25,

    /// <summary>
    ///     Decision made (verdict upheld, or misc filed), not yet executed: the claimer/filer
    ///     presses "Execute decision" / "Close" to finish. Public: still "Pending".
    /// </summary>
    AwaitingExecution = 27,

    /// <summary>Terminal: verdict ratified and executed. Public: verdict + reasoning.</summary>
    Closed = 30,

    /// <summary>Terminal: submission was invalid (IA+-ratified void, or legacy import).</summary>
    Voided = 40,

    /// <summary>Hidden from the public view post-hoc. Underlying verdict fields are retained.</summary>
    Sealed = 50
}

public enum CaseVerdict
{
    Accepted = 0,
    Rejected = 1,
    Other = 2
}

/// <summary>
///     Categorized void reasons, mirroring the tags of the old #voiding-requests forum
///     ("Use the appropriate tag for the reason the appeal should be voided").
/// </summary>
public enum VoidReason
{
    /// <summary>The appeal cooldown was violated.</summary>
    Cooldown = 0,

    /// <summary>The maximum number of appeal attempts is exhausted.</summary>
    MaxAttempts = 1,

    /// <summary>The submission is not an appeal at all.</summary>
    NotAnAppeal = 2,

    /// <summary>Outside QIAD's jurisdiction.</summary>
    NonQiadMatter = 3,

    /// <summary>No active punishment on record.</summary>
    NoActiveBan = 4,

    /// <summary>Filed under the wrong category (e.g. a temporary punishment outside unfair).</summary>
    InvalidCategory = 5,

    /// <summary>Duplicate of another submission.</summary>
    Duplicate = 6,

    /// <summary>Anything else; the reasoning text explains.</summary>
    Other = 99
}

public static class VoidReasonExtensions
{
    /// <summary>Human name matching the old forum tag wording.</summary>
    public static string DisplayName(this VoidReason reason) => reason switch
    {
        VoidReason.Cooldown        => "Cooldown",
        VoidReason.MaxAttempts     => "Max attempts",
        VoidReason.NotAnAppeal     => "Not an appeal",
        VoidReason.NonQiadMatter   => "Non-QIAD matter",
        VoidReason.NoActiveBan     => "No active ban",
        VoidReason.InvalidCategory => "Invalid category",
        VoidReason.Duplicate       => "Duplicate",
        _                          => "Other"
    };
}

/// <summary>Protocol-bypass filing types (design §5, misc path).</summary>
public enum MiscFilingType
{
    /// <summary>Ban issued against a compromised account.</summary>
    CompromisedAccount = 0,

    /// <summary>Autoban of a &lt;24h-old account.</summary>
    NewAccountAutoban = 1,

    /// <summary>The punishment already expired or was already appealed.</summary>
    PunishmentAlreadyExpiredOrAppealed = 2,

    /// <summary>Anything else leadership files outside the standard flow.</summary>
    Other = 99
}

/// <summary>Which public numbering sequence a case belongs to.</summary>
public enum CaseNumberSeries
{
    /// <summary>Appeals — the Sheet's "Cases" tabs.</summary>
    Cases = 0,

    /// <summary>Investigations — the Sheet's separate "Incidents" tab.</summary>
    Incidents = 1
}
