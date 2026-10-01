using System.Net;
using Discord;
using Discord.Net;
using Microsoft.Extensions.Logging;

namespace QiadHelper.Bot.Services;

public sealed class DirectMessageService : IDirectMessageService
{
    private readonly ILogger<DirectMessageService> _logger;

    public DirectMessageService(ILogger<DirectMessageService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> TrySendAsync(IUser user,
                                         string? content = null,
                                         Embed? embed = null,
                                         CancellationToken ct = default)
    {
        if (user is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(content) && embed is null)
        {
            return false;
        }

        try
        {
            var dm = await user.CreateDMChannelAsync();
            await dm.SendMessageAsync(content, embed: embed);

            return true;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or
                                           HttpStatusCode.NotFound)
        {
            // User has DMs closed or bot blocked
            _logger.LogInformation("DM to {UserId} failed with {Code}: {Message}", user.Id, (int)ex.HttpCode,
                ex.Message);

            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("DM to {UserId} canceled/timed out.", user.Id);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error while DM-ing {UserId}", user.Id);

            return false;
        }
    }

    public async Task<bool> TrySendComponentsAsync(IUser user,
                                                   MessageComponent components,
                                                   string? content = null,
                                                   Embed? embed = null,
                                                   CancellationToken ct = default)
    {
        if (user is null || components is null)
        {
            return false;
        }

        try
        {
            var dm = await user.CreateDMChannelAsync();
            await dm.SendMessageAsync(content, embed: embed, components: components);

            return true;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or
                                           HttpStatusCode.NotFound)
        {
            _logger.LogInformation("DM (components) to {UserId} failed with {Code}: {Message}", user.Id,
                (int)ex.HttpCode, ex.Message);

            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("DM (components) to {UserId} canceled/timed out.", user.Id);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error while DM-ing (components) {UserId}", user.Id);

            return false;
        }
    }
}
