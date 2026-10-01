using System.ComponentModel.DataAnnotations;

namespace QiadHelper.Core.Data.Cases;

/// <summary>
///     Configurable case type (design §2.2): Game/Discord/Blacklist/Group/Raider/Unfair/QSP-Warn
///     appeals, Investigations, … The set has grown over the years, so it is data, not an enum.
///     Each category drives its own intake panel (channel + button) and carries the minimum
///     ratifier rank per action — the "rank-parameterised, not a single role" gate of §2.1.
/// </summary>
public class CaseCategory : QiadDataAbstract
{
    [Required] [MaxLength(64)] public string Name { get; set; } = string.Empty;

    [MaxLength(500)] public string? Description { get; set; }

    /// <summary>Display order in panels and public filters.</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Whether the category is offered to appellants (false = staff-filed only, e.g. audits).</summary>
    public bool ShowOnPublicForm { get; set; } = true;

    /// <summary>Which public numbering sequence cases of this category draw from.</summary>
    public CaseNumberSeries NumberSeries { get; set; } = CaseNumberSeries.Cases;

    // ---- Intake panel (bot-maintained persistent button message) ----

    /// <summary>Channel the bot posts this category's intake panel into.</summary>
    public ulong? SubmissionChannelId { get; set; }

    /// <summary>The persistent panel message the bot maintains (recorded automatically).</summary>
    public ulong? PanelMessageId { get; set; }

    [MaxLength(80)] public string ButtonLabel { get; set; } = "Start an Appeal";

    /// <summary>Extra instructions shown in the panel embed (criteria, cooldowns, …).</summary>
    [MaxLength(2000)]
    public string? PanelText { get; set; }

    /// <summary>
    ///     Adds the second panel button that files an UNFAIR appeal of this same category
    ///     (the live system's "Start an Unfair Appeal" next to "Start an Appeal").
    /// </summary>
    public bool OfferUnfairAppeals { get; set; }

    [MaxLength(80)] public string? UnfairButtonLabel { get; set; }

    // ---- Review routing ----

    /// <summary>Forum channel review threads are created in (falls back to the global default).</summary>
    public ulong? ForumChannelId { get; set; }

    // ---- Ratification gates (design §2.1: configurable per-action minimum rank) ----

    /// <summary>Minimum rank that may ratify/overturn a drafted verdict. Default: Secretary+.</summary>
    public QiadRank VerdictRatifierMinRank { get; set; } = QiadRank.Secretary;

    /// <summary>Minimum rank that may ratify a void request. Default: IA+.</summary>
    public QiadRank VoidRatifierMinRank { get; set; } = QiadRank.InvestigativeAffairs;

    // ---- Execution (rejection cooldown role) ----

    /// <summary>
    ///     Restriction role applied automatically when a case of this category closes with a
    ///     REJECTED verdict (must be on the restriction-role whitelist). Null = none.
    /// </summary>
    public ulong? RejectionRestrictionRoleId { get; set; }

    /// <summary>How long the rejection restriction lasts, in days. Null = permanent.</summary>
    public double? RejectionRestrictionDays { get; set; }
}
