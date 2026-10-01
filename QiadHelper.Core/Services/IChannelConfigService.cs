namespace QiadHelper.Core.Services;

/// <summary>
///     Runtime Discord channel/guild configuration, persisted in <c>DiscordChannelConfigs</c>.
///     Used by both the bot (routing, error channel, guild id) and the admin UI (editing).
///     Well-known keys live in <see cref="Data.Configuration.QiadConfigKeys" />.
/// </summary>
public interface IChannelConfigService
{
    /// <summary>Returns the configured ID for <paramref name="key" />, or 0 when unset.</summary>
    Task<ulong> GetChannelIdAsync(string key, CancellationToken ct = default);

    Task SetChannelIdAsync(string key,
                           ulong channelId,
                           string? updatedBy = null,
                           string? description = null,
                           CancellationToken ct = default);
}
