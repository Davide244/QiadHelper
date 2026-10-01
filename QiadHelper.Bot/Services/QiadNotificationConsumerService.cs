using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;
using QiadHelper.Core.Notifications;

namespace QiadHelper.Bot.Services;

/// <summary>
///     The consuming half of the in-process dispatcher (design §7.5): drains the notification
///     channel and hands each message to <see cref="CaseDiscordService" />. Waits for the gateway
///     before acting, so notifications published during startup are buffered, not lost. When the
///     bot is disabled, it drains and discards so the channel cannot grow unbounded.
/// </summary>
public sealed class QiadNotificationConsumerService : BackgroundService
{
    private readonly InProcessNotificationBus _bus;
    private readonly DiscordSocketClient _client;
    private readonly CaseDiscordService _discordService;
    private readonly ILogger<QiadNotificationConsumerService> _logger;
    private readonly BotOptions _options;
    private readonly DiscordOptions _discordOptions;

    public QiadNotificationConsumerService(InProcessNotificationBus bus,
                                           DiscordSocketClient client,
                                           CaseDiscordService discordService,
                                           IOptions<BotOptions> options,
                                           IOptions<DiscordOptions> discordOptions,
                                           ILogger<QiadNotificationConsumerService> logger)
    {
        _bus = bus;
        _client = client;
        _discordService = discordService;
        _options = options.Value;
        _discordOptions = discordOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botRuns = _options.Enabled && !string.IsNullOrWhiteSpace(_discordOptions.BotToken);

        if (!botRuns)
        {
            // Web-only mode: keep the channel drained so publishers never accumulate memory.
            await foreach (var notification in _bus.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogWarning("Discarding {Notification}: bot is disabled (web-only mode)",
                    notification.GetType().Name);
            }

            return;
        }

        // Wait for the gateway before performing Discord work.
        while (_client.ConnectionState != ConnectionState.Connected && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("Notification consumer started");

        await foreach (var notification in _bus.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _discordService.HandleAsync(notification, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One failed side effect must not stall the queue behind it.
                _logger.LogError(ex, "Failed handling {Notification}", notification.GetType().Name);
            }
        }
    }
}
