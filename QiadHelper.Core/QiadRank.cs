namespace QiadHelper.Core;

/// <summary>
///     The QIAD rank ladder (reference guide §2.1). Values are spaced so future tiers can be
///     inserted without renumbering. Ratification gates compare with &gt;= — e.g. a verdict
///     ratification gate of <see cref="Secretary" /> admits Secretary and Director.
/// </summary>
public enum QiadRank
{
    /// <summary>Not QIAD staff.</summary>
    Unranked = 0,

    /// <summary>PA — Probationary Affairs (entry/training). Validates, claims, drafts, executes.</summary>
    ProbationaryAffairs = 10,

    /// <summary>RA — Regulatory Affairs (junior reviewer).</summary>
    RegulatoryAffairs = 20,

    /// <summary>AA — Administrative Affairs (senior reviewer).</summary>
    AdministrativeAffairs = 30,

    /// <summary>IA — Investigative Affairs (high-ranking; ratifies void requests).</summary>
    InvestigativeAffairs = 40,

    /// <summary>Secretary+ — leadership; ratifies case verdicts.</summary>
    Secretary = 50,

    /// <summary>Director — top of the ladder.</summary>
    Director = 60
}

public static class QiadRankExtensions
{
    /// <summary>The abbreviation staff actually use ("PA", "RA", …).</summary>
    public static string ShortName(this QiadRank rank) =>
        rank switch
        {
            QiadRank.ProbationaryAffairs => "PA",
            QiadRank.RegulatoryAffairs => "RA",
            QiadRank.AdministrativeAffairs => "AA",
            QiadRank.InvestigativeAffairs => "IA",
            QiadRank.Secretary => "Secretary",
            QiadRank.Director => "Director",
            _ => "—"
        };

    public static string DisplayName(this QiadRank rank) =>
        rank switch
        {
            QiadRank.ProbationaryAffairs => "Probationary Affairs",
            QiadRank.RegulatoryAffairs => "Regulatory Affairs",
            QiadRank.AdministrativeAffairs => "Administrative Affairs",
            QiadRank.InvestigativeAffairs => "Investigative Affairs",
            QiadRank.Secretary => "Secretary",
            QiadRank.Director => "Director",
            _ => "Unranked"
        };
}
