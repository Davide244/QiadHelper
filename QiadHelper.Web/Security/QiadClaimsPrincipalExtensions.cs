using System.Security.Claims;
using QiadHelper.Core;
using QiadHelper.Core.Services.Cases;
using QiadHelper.Web.Security.Authentication;

namespace QiadHelper.Web.Security;

public static class QiadClaimsPrincipalExtensions
{
    /// <summary>
    ///     Returns the authenticated user's Discord user ID, regardless of which claim type the
    ///     OAuth pipeline emitted it under. Returns null for API-key auth or unauthenticated users.
    /// </summary>
    public static ulong? GetDiscordUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirst("discord_id")?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? user.FindFirst("sub")?.Value;

        return ulong.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    ///     Best-effort Discord username (Discord identify scope sets ClaimTypes.Name).
    /// </summary>
    public static string? GetDiscordUsername(this ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Name)?.Value;

    /// <summary>
    ///     The authenticated user's QIAD rank. Returns null for API-key or anonymous callers.
    /// </summary>
    public static QiadRank? GetRank(this ClaimsPrincipal user)
    {
        var raw = user.FindFirst(QiadClaimTypes.Rank)?.Value;

        return int.TryParse(raw, out var v) && Enum.IsDefined(typeof(QiadRank), v)
            ? (QiadRank)v
            : null;
    }

    /// <summary>
    ///     True if the user has at least <paramref name="minimum" /> rank. Use for soft
    ///     "may use feature X" checks inside controllers/views; hard gates are [QiadAuthorize].
    /// </summary>
    public static bool HasRankAtLeast(this ClaimsPrincipal user, QiadRank minimum)
    {
        var rank = user.GetRank();

        return rank.HasValue && rank.Value >= minimum;
    }

    /// <summary>
    ///     True when the caller authenticated with an API key rather than a Discord cookie.
    /// </summary>
    public static bool IsApiKey(this ClaimsPrincipal user)
        => string.Equals(user.FindFirst("auth.kind")?.Value, "api-key", StringComparison.Ordinal);

    /// <summary>True for the configured developer/bootstrap users.</summary>
    public static bool IsDeveloper(this ClaimsPrincipal user)
        => string.Equals(user.FindFirst(QiadClaimTypes.IsDeveloper)?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     The web-side <see cref="StaffActor" /> for calling the shared case/restriction services
    ///     — the same rank the bot would resolve, so both surfaces hit identical service gates.
    ///     Developers act as Director (mirrors the rank-handler bypass).
    /// </summary>
    public static StaffActor ToStaffActor(this ClaimsPrincipal user)
    {
        var rank = user.IsDeveloper() ? QiadRank.Director : user.GetRank() ?? QiadRank.Unranked;

        return new StaffActor(
            user.GetDiscordUserId() ?? 0,
            user.GetDiscordUsername() ?? "unknown",
            rank);
    }
}
