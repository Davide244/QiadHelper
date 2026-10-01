using Microsoft.AspNetCore.Authorization;
using QiadHelper.Web.Security.Authentication;

namespace QiadHelper.Web.Security.Authorization;

/// <summary>
///     Requirement that allows either API key or cookie-based authentication
/// </summary>
public class ApiKeyOrAuthenticatedRequirement : IAuthorizationRequirement
{
}

/// <summary>
///     Handler for the ApiKeyOrUser policy
/// </summary>
public class ApiKeyOrAuthenticatedHandler : AuthorizationHandler<ApiKeyOrAuthenticatedRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
                                                   ApiKeyOrAuthenticatedRequirement requirement)
    {
        var hasApiKey =
            context.User?.Identities?.Any(i => i.AuthenticationType == ApiKeyAuthenticationHandler.Scheme) == true;
        if (hasApiKey || (context.User?.Identity?.IsAuthenticated ?? false))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
