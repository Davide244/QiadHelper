using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using QiadHelper.Core.Data.Security;
using QiadHelper.Web.Configuration;
using QiadHelper.Web.Services.Security;

namespace QiadHelper.Web.Security.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
///     X-Api-Key authentication for the small external API surface (design §8.3). Copied from
///     QDF; note the bot is NOT a consumer here — it runs in-process and needs no credential.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public new const string Scheme = "ApiKey";
    private readonly IApiKeyService _apiKeyService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService) : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyOptions.HeaderName, out var provided))
        {
            return AuthenticateResult.NoResult();
        }

        var key = provided.ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return AuthenticateResult.Fail("API key header is empty");
        }

        var ipAddress = Context.Connection.RemoteIpAddress?.ToString();
        var result = await _apiKeyService.ValidateKeyAsync(key, ipAddress);

        if (!result.IsValid)
        {
            Logger.LogWarning("API key authentication failed from {IP}: {Reason}",
                ipAddress, result.FailureReason);

            return AuthenticateResult.Fail(result.FailureReason ?? "Invalid API key");
        }

        var claims = new List<Claim>
                     {
                         new(ClaimTypes.Name, result.Key?.Name ?? "ApiKeyUser"),
                         new("auth.kind", "api-key"),
                         new("api-key.id", result.Key?.Id.ToString() ?? "unknown"),
                         new("api-key.scope",
                             ((int)(result.Key?.Scope ?? ApiKeyScope.None)).ToString(
                                 System.Globalization.CultureInfo.InvariantCulture))
                     };

        if (result.Key != null)
        {
            claims.Add(new Claim("api-key.name", result.Key.Name));
        }

        var identity = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);

        return AuthenticateResult.Success(ticket);
    }
}
