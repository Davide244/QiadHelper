namespace QiadHelper.Bot.Configuration;

/// <summary>
///     Configuration for the Guardsman verification API (same integration the QDF bot uses).
///     Leave <see cref="ApiKey" /> empty to disable automatic Discord→Roblox resolution.
/// </summary>
public sealed class GuardsmanOptions
{
    public const string SectionName = "Guardsman";

    /// <summary>The Guardsman API key. Configure via user-secrets or Guardsman__ApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Guardsman consumer user ID sent with each request.</summary>
    public string UserId { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.gdsmn.dev";
}
