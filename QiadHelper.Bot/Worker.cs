using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;

namespace QiadHelper.Bot;

/// <summary>
///     Starts the Discord bot inside the web host (design §7.1 — there is no bot executable).
///     A fatal crash here stops the host by .NET default, the container exits, and Docker's
///     restart policy brings the whole app back — the intended failure model.
/// </summary>
public class Worker : BackgroundService
{
    public static readonly string BotName = "QIAD Helper";
    public static readonly string BotVersion = "1.0.0";
    public static readonly string FullBotName = $"{BotName} v{BotVersion}";

    private readonly QiadBot _bot;
    private readonly BotOptions _botOptions;
    private readonly DiscordOptions _discordOptions;
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger,
                  QiadBot bot,
                  IOptions<BotOptions> botOptions,
                  IOptions<DiscordOptions> discordOptions)
    {
        _logger = logger;
        _bot = bot;
        _botOptions = botOptions.Value;
        _discordOptions = discordOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Web-only mode: dev without Discord, or a staging deploy (design §7.1).
        if (!_botOptions.Enabled)
        {
            _logger.LogWarning("Bot is disabled (Bot:Enabled=false); running web-only.");

            return;
        }

        if (string.IsNullOrWhiteSpace(_discordOptions.BotToken))
        {
            _logger.LogWarning("No Discord bot token configured (Discord:BotToken); running web-only.");

            return;
        }

        try
        {
            await _bot.StartAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(100, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // normal on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker crashed");

            throw;
        }
    }
}
