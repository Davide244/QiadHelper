using System.ComponentModel.DataAnnotations;

namespace QiadHelper.Core.Data.Configuration;

/// <summary>
///     Stores Discord channel/guild configuration that can be modified at runtime.
///     Well-known keys live in <see cref="QiadConfigKeys" />.
/// </summary>
public class DiscordChannelConfig
{
    [Key] [MaxLength(50)] public string Key { get; set; } = string.Empty;

    public ulong ChannelId { get; set; }

    [MaxLength(200)] public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)] public string? UpdatedBy { get; set; }
}

/// <summary>Well-known <see cref="DiscordChannelConfig" /> keys (values are channel/guild IDs).</summary>
public static class QiadConfigKeys
{
    /// <summary>The QIAD guild the bot registers commands in.</summary>
    public const string MainGuildId = "MainGuildId";

    /// <summary>
    ///     Validation queue: every new submission lands here as a message with claim/void/misc
    ///     buttons. The review thread is only created once a reviewer claims from this queue.
    /// </summary>
    public const string SubmissionQueueChannelId = "SubmissionQueueChannelId";

    /// <summary>Fallback forum channel for review threads when a category has none configured.</summary>
    public const string DefaultForumChannelId = "DefaultForumChannelId";

    /// <summary>Forum for void-request posts (one per request; IA+ ratifies there).</summary>
    public const string VoidRequestsForumChannelId = "VoidRequestsForumChannelId";

    /// <summary>Forum for misc-filing documentation posts.</summary>
    public const string MiscFilingsForumChannelId = "MiscFilingsForumChannelId";

    /// <summary>The public #case-updates channel for opened/closed announcements.</summary>
    public const string CaseUpdatesChannelId = "CaseUpdatesChannelId";

    /// <summary>Channel where unexpected bot errors are reported.</summary>
    public const string ErrorLogChannelId = "ErrorLogChannelId";
}
