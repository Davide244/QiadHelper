namespace QiadHelper.Core.Services;

/// <summary>
///     Resolves a Discord user's QIAD rank. Implemented bot-side (guild roles ∩ the
///     <c>QiadRankRoles</c> mapping table) — usable from web code too because everything is one
///     process (design §7.1). Returns <see cref="QiadRank.Unranked" /> for non-staff or when the
///     gateway is unavailable.
/// </summary>
public interface IRankResolver
{
    Task<QiadRank> ResolveRankAsync(ulong discordUserId, CancellationToken ct = default);
}
