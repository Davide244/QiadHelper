using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;

namespace QiadHelper.Bot.Services;

public sealed record RobloxIdentity(long RobloxUserId, string? Username);

/// <summary>
///     Resolves a Discord user to their verified Roblox identity. Returns null when the user is
///     not registered, on any API trouble, or when the integration is unconfigured; intake never
///     blocks on this.
/// </summary>
public interface IRobloxIdentityResolver
{
    Task<RobloxIdentity?> ResolveAsync(ulong discordUserId, CancellationToken ct = default);
}

/// <summary>
///     Guardsman-backed implementation (the verification service the QDF bot already uses),
///     with a follow-up call to the public Roblox users API for the canonical username.
/// </summary>
public sealed class GuardsmanIdentityResolver : IRobloxIdentityResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GuardsmanIdentityResolver> _logger;
    private readonly GuardsmanOptions _options;

    public GuardsmanIdentityResolver(IHttpClientFactory httpClientFactory,
                                     IOptions<GuardsmanOptions> options,
                                     ILogger<GuardsmanIdentityResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RobloxIdentity?> ResolveAsync(ulong discordUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.UserId))
        {
            return null; // integration not configured
        }

        try
        {
            var client = _httpClientFactory.CreateClient("Guardsman");

            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_options.BaseUrl.TrimEnd('/')}/v2/cloud/users/{discordUserId}/fetch?field_type=discord");
            request.Headers.Add("X-Guardsman-API-Key", _options.ApiKey);
            request.Headers.Add("X-Guardsman-User-ID", _options.UserId);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Guardsman lookup for {DiscordId} returned {Status}",
                    discordUserId, (int)response.StatusCode);

                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.GetProperty("status").GetString() != "success" ||
                !json.RootElement.TryGetProperty("user", out var user) ||
                !user.TryGetProperty("roblox_id", out var robloxIdElement))
            {
                return null;
            }

            // roblox_id arrives as a string in the Guardsman payload.
            var raw = robloxIdElement.ValueKind == JsonValueKind.String
                ? robloxIdElement.GetString()
                : robloxIdElement.GetRawText();
            if (!long.TryParse(raw, out var robloxId) || robloxId <= 0)
            {
                return null;
            }

            return new RobloxIdentity(robloxId, await FetchUsernameAsync(robloxId, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Guardsman lookup failed for {DiscordId}", discordUserId);

            return null;
        }
    }

    /// <summary>Canonical username from the public Roblox users API; null on any trouble.</summary>
    private async Task<string?> FetchUsernameAsync(long robloxId, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Roblox");
            using var response = await client.GetAsync($"https://users.roblox.com/v1/users/{robloxId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            return json.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Roblox username lookup failed for {RobloxId}", robloxId);

            return null;
        }
    }
}
