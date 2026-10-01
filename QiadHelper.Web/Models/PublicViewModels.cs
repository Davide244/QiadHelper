using QiadHelper.Core.Data.Cases;

namespace QiadHelper.Web.Models;

/// <summary>
///     The ONLY shape public case data leaves the server in (design §8.2): the Sheet's public
///     columns, nothing else. Internal notes, appellant identities, evidence and draft verdicts
///     never reach a view or the API through this type.
/// </summary>
public sealed record PublicCaseRow(
    int CaseNumber,
    string CaseType,
    bool IsUnfair,
    DateTime Opened,
    DateTime? Closed,
    CaseStatus Status,
    CaseVerdict? Verdict,
    string? Description,
    string? DirectorsNote,
    string? ArchivedBy)
{
    /// <summary>The public type label: unfair appeals read "Unfair Game Ban Appeal".</summary>
    public string DisplayType => IsUnfair ? $"Unfair {CaseType}" : CaseType;

    public TimeSpan? ResolutionTime => Closed - Opened;

    public bool IsPending => Status is CaseStatus.UnderReview
        or CaseStatus.AwaitingRatification
        or CaseStatus.AwaitingVoidRatification
        or CaseStatus.AwaitingExecution;
}

public sealed record PublicIncidentRow(
    int IncidentNumber,
    DateTime ReportDate,
    CaseStatus Status,
    string? Description);

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed class PublicCasesViewModel
{
    public required PagedResult<PublicCaseRow> Cases { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
    public string? CategoryFilter { get; init; }
    public string? VerdictFilter { get; init; }
    public int? NumberSearch { get; init; }
}

public sealed class HomeViewModel
{
    public required int TotalCases { get; init; }
    public required int TotalIncidents { get; init; }
    public required int PendingCases { get; init; }
    public required double AcceptanceRate { get; init; }
    public required IReadOnlyList<PublicCaseRow> RecentCases { get; init; }
}

public sealed class PublicStatsViewModel
{
    public required int TotalCases { get; init; }
    public required int TotalIncidents { get; init; }
    public required int PendingCases { get; init; }
    public required double OverallAcceptanceRate { get; init; }
    public required TimeSpan? AverageResolutionTime { get; init; }
    public required IReadOnlyList<YearStats> ByYear { get; init; }
    public required IReadOnlyList<CategoryYearStats> CategoriesByYear { get; init; }
}

public sealed record YearStats(
    int Year,
    int Total,
    int Accepted,
    int Rejected,
    int Other,
    int Voided,
    int Unfair,
    double AcceptanceRate);

public sealed record CategoryYearStats(string Category, int Year, int Count);
