namespace QiadHelper.Bot.Configuration;

/// <summary>Bound from the "Discord" configuration section.</summary>
public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public string BotToken { get; set; } = string.Empty;

    /// <summary>OAuth application client id (used by the web host's Discord login).</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
