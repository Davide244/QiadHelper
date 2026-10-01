using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;
using QiadHelper.Core;
using QiadHelper.Core.Services;

namespace QiadHelper.Web.Security.Authentication;

/// <summary>
///     Adds the QIAD rank claim to cookie principals. Unlike QDF (which looked ranks up in an
///     Operative table), QIAD staff exist only in Discord — the in-process bot resolves rank from
///     guild roles via the QiadRankRoles mapping (design §8.3). Cached briefly because claims
///     transformation runs on every request.
/// </summary>
public sealed class QiadClaimsTransformation : IClaimsTransformation
{
    private static readonly TimeSpan RankCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IMemoryCache _cache;
    private readonly BotOptions _options;
    private readonly IRankResolver _rankResolver;

    public QiadClaimsTransformation(IRankResolver rankResolver, IMemoryCache cache, IOptions<BotOptions> options)
    {
        _rankResolver = rankResolver;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity ||
            identity.HasClaim(c => c.Type == QiadClaimTypes.Rank) ||
            principal.HasClaim("auth.kind", "api-key"))
        {
            return principal;
        }

        var discordIdRaw = identity.FindFirst("discord_id")?.Value
                           ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? identity.FindFirst("sub")?.Value;

        if (!ulong.TryParse(discordIdRaw, out var discordId))
        {
            return principal;
        }

        var rank = await _cache.GetOrCreateAsync($"qiad-rank:{discordId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RankCacheTtl;

            return await _rankResolver.ResolveRankAsync(discordId);
        });

        identity.AddClaim(new Claim(QiadClaimTypes.Rank,
            ((int)rank).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim(QiadClaimTypes.IsDeveloper,
            _options.DeveloperUserIds.Contains(discordId) ? "true" : "false"));

        return principal;
    }
}

/// <summary>Constants for QIAD-specific claim types.</summary>
public static class QiadClaimTypes
{
    public const string Rank = "qiad.rank";
    public const string IsDeveloper = "qiad.isDeveloper";
}
