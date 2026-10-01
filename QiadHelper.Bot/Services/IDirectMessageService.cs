using Discord;

namespace QiadHelper.Bot.Services;

public interface IDirectMessageService
{
    /// <summary>
    ///     Sends a DM with traditional embed.
    ///     Returns true if DM was sent successfully; false if user has DMs closed or other non-fatal errors.
    /// </summary>
    Task<bool> TrySendAsync(IUser user,
                            string? content = null,
                            Embed? embed = null,
                            CancellationToken ct = default);

    /// <summary>
    ///     Sends a DM carrying message components (buttons).
    ///     Returns true if DM was sent successfully; false if user has DMs closed or other non-fatal errors.
    /// </summary>
    Task<bool> TrySendComponentsAsync(IUser user,
                                      MessageComponent components,
                                      string? content = null,
                                      Embed? embed = null,
                                      CancellationToken ct = default);
}
