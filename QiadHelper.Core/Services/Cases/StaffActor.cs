namespace QiadHelper.Core.Services.Cases;

/// <summary>
///     The staff member performing a case action, with their rank already resolved by the caller
///     (bot: guild roles; web: auth claims). The service layer trusts this rank for its gates, so
///     both surfaces enforce identical rules server-side.
/// </summary>
public sealed record StaffActor(ulong DiscordUserId, string Username, QiadRank Rank);

/// <summary>Outcome of a case action; <see cref="Error" /> is user-presentable when it fails.</summary>
public sealed record CaseActionResult(bool Success, string? Error, Data.Cases.Case? Case)
{
    public static CaseActionResult Ok(Data.Cases.Case c) => new(true, null, c);

    public static CaseActionResult Fail(string error) => new(false, error, null);
}
