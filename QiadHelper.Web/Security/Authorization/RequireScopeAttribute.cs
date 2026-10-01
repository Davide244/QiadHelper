using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using QiadHelper.Core.Data.Security;
using QiadHelper.Web.Security.Authentication;

namespace QiadHelper.Web.Security.Authorization;

/// <summary>
///     Restricts an endpoint to API keys carrying the given scope. Cookie-authenticated humans
///     pass through — their access is governed by the rank policies instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireScopeAttribute : Attribute, IAuthorizationFilter
{
    private readonly ApiKeyScope _required;

    public RequireScopeAttribute(ApiKeyScope required)
    {
        _required = required;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        var isApiKey =
            user.Identities.Any(i => i.AuthenticationType == ApiKeyAuthenticationHandler.Scheme) ||
            user.HasClaim("auth.kind", "api-key");
        if (!isApiKey)
        {
            return;
        }

        var scopeClaim = user.FindFirst("api-key.scope")?.Value;
        if (!int.TryParse(scopeClaim, out var scopeValue) ||
            !((ApiKeyScope)scopeValue).HasFlag(_required))
        {
            context.Result = new ObjectResult(new
                                              {
                                                  error = $"API key lacks the required scope: {_required}"
                                              })
                             {
                                 StatusCode = StatusCodes.Status403Forbidden
                             };
        }
    }
}
