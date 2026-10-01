using System.ComponentModel.DataAnnotations;
using QiadHelper.Core;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Data.Configuration;
using QiadHelper.Core.Data.Restrictions;

namespace QiadHelper.Web.Models;

public sealed class AdminDashboardViewModel
{
    public required int Submitted { get; init; }
    public required int UnderReview { get; init; }
    public required int AwaitingRatification { get; init; }
    public required int AwaitingVoidRatification { get; init; }
    public required int AwaitingExecution { get; init; }
    public required int ClosedTotal { get; init; }
    public required int ActiveRestrictions { get; init; }
    public required bool BotConnected { get; init; }
}

public sealed class AdminCaseListViewModel
{
    public required PagedResult<Case> Cases { get; init; }
    public CaseStatus? StatusFilter { get; init; }
}

public sealed class AdminCaseDetailViewModel
{
    public required Case Case { get; init; }
    public required IReadOnlyList<CaseEvidenceItem> Evidence { get; init; }
    public required IReadOnlyList<Restriction> Restrictions { get; init; }
}

public sealed class CategoryEditModel
{
    public Guid? Id { get; set; }

    [Required] [MaxLength(64)] public string Name { get; set; } = string.Empty;

    [MaxLength(500)] public string? Description { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnPublicForm { get; set; } = true;
    public CaseNumberSeries NumberSeries { get; set; } = CaseNumberSeries.Cases;

    public ulong? SubmissionChannelId { get; set; }
    public ulong? ForumChannelId { get; set; }

    [MaxLength(80)] public string ButtonLabel { get; set; } = "Start an Appeal";

    [MaxLength(2000)] public string? PanelText { get; set; }

    public bool OfferUnfairAppeals { get; set; }

    [MaxLength(80)] public string? UnfairButtonLabel { get; set; }

    public QiadRank VerdictRatifierMinRank { get; set; } = QiadRank.Secretary;
    public QiadRank VoidRatifierMinRank { get; set; } = QiadRank.InvestigativeAffairs;

    public ulong? RejectionRestrictionRoleId { get; set; }

    [Range(0.01, 3650)] public double? RejectionRestrictionDays { get; set; }
}

public sealed class ImportConfirmViewModel
{
    public required string Token { get; init; }
    public required DateTime ExportedAtUtc { get; init; }
    public string? ExportedBy { get; init; }
    public required IReadOnlyDictionary<string, int> FileCounts { get; init; }
    public required IReadOnlyDictionary<string, int> CurrentCounts { get; init; }
}

public sealed class AdminSettingsViewModel
{
    public required ulong MainGuildId { get; init; }
    public required IReadOnlyList<QiadRankRole> RankRoles { get; init; }
    public required IReadOnlyList<RestrictionRoleConfig> RestrictionRoles { get; init; }
    public required IReadOnlyDictionary<string, ulong> Channels { get; init; }

    /// <summary>The well-known config keys the admin can set, with a human hint.</summary>
    public static readonly IReadOnlyList<(string Key, string Hint)> KnownChannelKeys =
    [
        (QiadConfigKeys.MainGuildId, "The QIAD guild ID (server, not a channel)"),
        (QiadConfigKeys.SubmissionQueueChannelId, "Validation queue: new submissions land here for claim/void/misc"),
        (QiadConfigKeys.DefaultForumChannelId, "Fallback review forum for categories without their own"),
        (QiadConfigKeys.VoidRequestsForumChannelId, "Forum where void requests are posted and ratified"),
        (QiadConfigKeys.MiscFilingsForumChannelId, "Forum where misc filings are documented"),
        (QiadConfigKeys.CaseUpdatesChannelId, "Public #case-updates announcement channel"),
        (QiadConfigKeys.ErrorLogChannelId, "Channel for unexpected bot error reports")
    ];
}
