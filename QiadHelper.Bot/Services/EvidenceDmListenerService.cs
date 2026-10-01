using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Services.Cases;

namespace QiadHelper.Bot.Services;

/// <summary>
///     Files anything an appellant sends in the bot DM as evidence on their open case
///     (design §7.3 step 4 — modals cannot carry file uploads, DMs can). Text and each
///     attachment become evidence items, mirrored into the review thread by the consumer.
/// </summary>
public sealed class EvidenceDmListenerService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<EvidenceDmListenerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public EvidenceDmListenerService(DiscordSocketClient client,
                                     IServiceScopeFactory scopeFactory,
                                     ILogger<EvidenceDmListenerService> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _client.MessageReceived += OnMessageReceivedAsync;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _client.MessageReceived -= OnMessageReceivedAsync;

        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(SocketMessage message)
    {
        if (message.Author.IsBot || message.Channel is not IDMChannel)
        {
            return Task.CompletedTask;
        }

        // Fire-and-forget off the gateway thread (Discord.Net events must not block).
        _ = Task.Run(() => HandleDmAsync(message));

        return Task.CompletedTask;
    }

    private async Task HandleDmAsync(SocketMessage message)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<QiadContext>();
            var caseService = scope.ServiceProvider.GetRequiredService<ICaseService>();

            // Most recent open case filed by this user — the one their DM plausibly belongs to.
            var openCase = await context.Cases.AsNoTracking()
                .Where(c => c.AppellantDiscordUserId == message.Author.Id &&
                            (c.Status == CaseStatus.Submitted ||
                             c.Status == CaseStatus.UnderReview ||
                             c.Status == CaseStatus.AwaitingRatification ||
                             c.Status == CaseStatus.AwaitingVoidRatification))
                .OrderByDescending(c => c.SubmittedAt)
                .FirstOrDefaultAsync();

            if (openCase == null)
            {
                return; // Not an appellant with an open case — stay silent.
            }

            var filed = 0;

            foreach (var attachment in message.Attachments)
            {
                var result = await caseService.AddEvidenceAsync(
                    openCase.Id,
                    message.Author.Id,
                    message.Author.Username,
                    isFromAppellant: true,
                    text: filed == 0 ? NullIfEmpty(message.Content) : null,
                    attachmentUrl: attachment.Url,
                    attachmentFileName: attachment.Filename,
                    sourceChannelId: message.Channel.Id,
                    sourceMessageId: message.Id);
                if (result.Success)
                {
                    filed++;
                }
            }

            if (filed == 0 && !string.IsNullOrWhiteSpace(message.Content))
            {
                var result = await caseService.AddEvidenceAsync(
                    openCase.Id,
                    message.Author.Id,
                    message.Author.Username,
                    isFromAppellant: true,
                    text: message.Content,
                    sourceChannelId: message.Channel.Id,
                    sourceMessageId: message.Id);
                if (result.Success)
                {
                    filed++;
                }
            }

            if (filed > 0)
            {
                await message.AddReactionAsync(new Emoji("✅"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to file DM evidence from {UserId}", message.Author.Id);
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
