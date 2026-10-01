using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Restrictions;
using QiadHelper.Core.Notifications;

namespace QiadHelper.Bot.Services;

/// <summary>
///     Auto-removes restriction roles at their expiry (design §7.6 — this service is what makes
///     the old "/remind on a second bot" step disappear). Same shape as QDF's LoAExpirationService:
///     poll every 15 minutes for due rows and act. The role removal + DM happen via the
///     notification consumer so lifts share one code path with manual removals.
/// </summary>
public sealed class RestrictionExpiryService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    private readonly DiscordSocketClient _discord;
    private readonly ILogger<RestrictionExpiryService> _logger;
    private readonly IQiadNotificationPublisher _publisher;
    private readonly IServiceScopeFactory _scopeFactory;

    public RestrictionExpiryService(DiscordSocketClient discord,
                                    IServiceScopeFactory scopeFactory,
                                    IQiadNotificationPublisher publisher,
                                    ILogger<RestrictionExpiryService> logger)
    {
        _discord = discord;
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for Discord to be ready
        while (_discord.ConnectionState != ConnectionState.Connected && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("Starting restriction expiry service");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueExpiriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for expired restrictions");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task ProcessDueExpiriesAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QiadContext>();

        var now = DateTime.UtcNow;
        var due = await context.Restrictions
            .Where(r => r.Status == RestrictionStatus.Active && r.ExpiresAt != null && r.ExpiresAt <= now)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} expired restriction(s) to process", due.Count);

        foreach (var restriction in due)
        {
            restriction.Status = RestrictionStatus.Expired;
            restriction.RemovedAt = now;
        }

        await context.SaveChangesAsync(ct);

        foreach (var restriction in due)
        {
            // Consumer removes the Discord role and DMs the user.
            await _publisher.PublishAsync(new RestrictionLiftedNotification(restriction.Id, WasExpiry: true), ct);
        }
    }
}
