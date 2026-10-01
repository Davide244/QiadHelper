using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;
using QiadHelper.Bot.Infrastructure;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;

namespace QiadHelper.Bot;

/// <summary>
///     Owns login, the Ready handshake, command registration and the status loop
///     (copied from QDF's <c>QDFBot</c>; status now reflects open cases instead of operatives).
/// </summary>
public class QiadBot : IAsyncDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionHandler _interactionHandler;
    private readonly ILogger<QiadBot> _logger;
    private readonly DiscordOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    private bool _isReady;
    private Timer? _statusUpdateTimer;

    public QiadBot(DiscordSocketClient client,
                   IOptions<DiscordOptions> options,
                   ILogger<QiadBot> logger,
                   InteractionHandler interactionHandler,
                   IServiceScopeFactory scopeFactory)
    {
        _client = client;
        _logger = logger;
        _options = options.Value;
        _interactionHandler = interactionHandler;
        _scopeFactory = scopeFactory;

        _client.Log += LogAsync;
        _client.Ready += ClientReadyAsync;
    }

    /// <summary>Gateway state, surfaced by the web host's /healthz endpoint.</summary>
    public bool IsConnected => _client.ConnectionState == ConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        if (_statusUpdateTimer != null)
        {
            await _statusUpdateTimer.DisposeAsync();
        }

        await _client.LogoutAsync();
        await _client.StopAsync();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _interactionHandler.InitializeAsync();

        await _client.LoginAsync(TokenType.Bot, _options.BotToken);
        await _client.StartAsync();

        while (!_isReady && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(50, cancellationToken);
        }
    }

    private async Task ClientReadyAsync()
    {
        await _interactionHandler.RegisterCommandsAsync();
        await UpdateStatusAsync();

        // Refresh the status every 5 minutes to reflect open-case count changes
        _statusUpdateTimer = new Timer(
            async _ => await UpdateStatusAsync(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));

        _isReady = true;
    }

    private async Task UpdateStatusAsync()
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<QiadContext>();

            var openCount = await context.Cases.CountAsync(c =>
                c.Status == CaseStatus.Submitted ||
                c.Status == CaseStatus.UnderReview ||
                c.Status == CaseStatus.AwaitingRatification ||
                c.Status == CaseStatus.AwaitingVoidRatification);

            await _client.SetActivityAsync(new Game($"{openCount} open case(s)", ActivityType.Watching));
            _logger.LogDebug("Updated bot status: {Count} open case(s)", openCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating bot status");
            // Set a fallback status
            await _client.SetActivityAsync(new Game("QIAD appeals", ActivityType.Watching));
        }
    }

    private Task LogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error    => LogLevel.Error,
            LogSeverity.Warning  => LogLevel.Warning,
            LogSeverity.Info     => LogLevel.Information,
            LogSeverity.Verbose  => LogLevel.Debug,
            LogSeverity.Debug    => LogLevel.Trace,
            _                    => LogLevel.Debug
        };

        _logger.Log(level, message.Exception, "DISCORD: {Message}", message.Message);

        return Task.CompletedTask;
    }
}
