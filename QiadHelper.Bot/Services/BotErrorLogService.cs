using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;
using QiadHelper.Core.Data.Configuration;
using QiadHelper.Core.Services;

namespace QiadHelper.Bot.Services;

/// <summary>Context attached to a Discord error report, extracted from the failing interaction.</summary>
public sealed record BotErrorContext(ulong? UserId, string? Username, ulong? ChannelId, string? CommandName)
{
    public static BotErrorContext FromInteraction(IDiscordInteraction interaction, string? commandName = null)
    {
        return new BotErrorContext(
            interaction.User?.Id,
            interaction.User?.Username,
            interaction.ChannelId,
            commandName);
    }
}

public interface IBotErrorLogService
{
    /// <summary>Reports an error to the configured error-log channel. Never throws.</summary>
    Task LogErrorAsync(string title, string message, Exception? exception = null, BotErrorContext? context = null);
}

public sealed class BotErrorLogService : IBotErrorLogService
{
    private readonly IChannelConfigService _channelConfig;
    private readonly DiscordSocketClient _client;
    private readonly ILogger<BotErrorLogService> _logger;
    private readonly BotOptions _options;

    public BotErrorLogService(DiscordSocketClient client,
                              IChannelConfigService channelConfig,
                              IOptions<BotOptions> options,
                              ILogger<BotErrorLogService> logger)
    {
        _client = client;
        _channelConfig = channelConfig;
        _options = options.Value;
        _logger = logger;
    }

    public async Task LogErrorAsync(string title,
                                    string message,
                                    Exception? exception = null,
                                    BotErrorContext? context = null)
    {
        try
        {
            var channelId = await _channelConfig.GetChannelIdAsync(QiadConfigKeys.ErrorLogChannelId);
            if (channelId == 0)
            {
                channelId = _options.ErrorLogChannelId;
            }

            if (channelId == 0 || _client.GetChannel(channelId) is not IMessageChannel channel)
            {
                return;
            }

            var builder = new EmbedBuilder()
                .WithTitle($"⚠️ {title}")
                .WithColor(Color.Red)
                .WithDescription(message.Length > 2000 ? message[..2000] : message)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (exception != null)
            {
                var detail = exception.ToString();
                builder.AddField("Exception", $"```{(detail.Length > 1000 ? detail[..1000] : detail)}```");
            }

            if (context != null)
            {
                var parts = new List<string>();
                if (context.UserId != null)
                {
                    parts.Add($"User: {context.Username} (<@{context.UserId}>)");
                }

                if (context.ChannelId != null)
                {
                    parts.Add($"Channel: <#{context.ChannelId}>");
                }

                if (context.CommandName != null)
                {
                    parts.Add($"Command: `{context.CommandName}`");
                }

                if (parts.Count > 0)
                {
                    builder.AddField("Context", string.Join("\n", parts));
                }
            }

            await channel.SendMessageAsync(embed: builder.Build());
        }
        catch (Exception ex)
        {
            // Error reporting must never take the bot down with it.
            _logger.LogWarning(ex, "Failed to post error report to the error-log channel");
        }
    }
}
