using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Configuration;

namespace QiadHelper.Core.Services;

/// <summary>
///     Scope-factory based so bot singletons (which outlive any request scope) can use it too.
///     Reads are rare (startup, routing decisions) — no caching needed.
/// </summary>
public sealed class ChannelConfigService : IChannelConfigService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ChannelConfigService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<ulong> GetChannelIdAsync(string key, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QiadContext>();

        var entry = await context.DiscordChannelConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, ct);

        return entry?.ChannelId ?? 0;
    }

    public async Task SetChannelIdAsync(string key,
                                        ulong channelId,
                                        string? updatedBy = null,
                                        string? description = null,
                                        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QiadContext>();

        var entry = await context.DiscordChannelConfigs.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (entry == null)
        {
            entry = new DiscordChannelConfig
                    {
                        Key = key
                    };
            context.DiscordChannelConfigs.Add(entry);
        }

        entry.ChannelId = channelId;
        entry.UpdatedAt = DateTime.UtcNow;
        entry.UpdatedBy = updatedBy;
        if (description != null)
        {
            entry.Description = description;
        }

        await context.SaveChangesAsync(ct);
    }
}
