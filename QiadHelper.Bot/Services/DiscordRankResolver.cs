using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;
using QiadHelper.Core;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Configuration;
using QiadHelper.Core.Services;

namespace QiadHelper.Bot.Services;

/// <summary>
///     Resolves a user's QIAD rank from their guild roles via the admin-configured
///     <c>QiadRankRoles</c> mapping (design §2.1/§11.1). Serves both bot interactions and the web
///     claims transformation — one process, one rank truth.
/// </summary>
public sealed class DiscordRankResolver : IRankResolver
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordRankResolver> _logger;
    private readonly BotOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public DiscordRankResolver(DiscordSocketClient client,
                               IServiceScopeFactory scopeFactory,
                               IOptions<BotOptions> options,
                               ILogger<DiscordRankResolver> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<QiadRank> ResolveRankAsync(ulong discordUserId, CancellationToken ct = default)
    {
        if (_options.DeveloperUserIds.Contains(discordUserId))
        {
            return QiadRank.Director;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<QiadContext>();

            var guildId = await context.DiscordChannelConfigs.AsNoTracking()
                .Where(c => c.Key == QiadConfigKeys.MainGuildId)
                .Select(c => c.ChannelId)
                .FirstOrDefaultAsync(ct);
            if (guildId == 0)
            {
                guildId = _options.MainGuildId;
            }

            if (guildId == 0)
            {
                return QiadRank.Unranked;
            }

            var mappings = await context.QiadRankRoles.AsNoTracking()
                .Where(m => m.GuildId == guildId)
                .ToListAsync(ct);
            if (mappings.Count == 0)
            {
                return QiadRank.Unranked;
            }

            var roleIds = await GetMemberRoleIdsAsync(guildId, discordUserId);
            if (roleIds == null)
            {
                return QiadRank.Unranked;
            }

            var rank = mappings
                .Where(m => roleIds.Contains(m.DiscordRoleId))
                .Select(m => m.Rank)
                .DefaultIfEmpty(QiadRank.Unranked)
                .Max();

            return rank;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rank resolution failed for {UserId}; treating as Unranked", discordUserId);

            return QiadRank.Unranked;
        }
    }

    private async Task<IReadOnlyCollection<ulong>?> GetMemberRoleIdsAsync(ulong guildId, ulong userId)
    {
        var guild = _client.GetGuild(guildId);
        var cached = guild?.GetUser(userId);
        if (cached != null)
        {
            return cached.Roles.Select(r => r.Id).ToArray();
        }

        // Member not in cache (or gateway not ready) — fall back to REST.
        var restUser = await _client.Rest.GetGuildUserAsync(guildId, userId);

        return restUser?.RoleIds.ToArray();
    }
}
