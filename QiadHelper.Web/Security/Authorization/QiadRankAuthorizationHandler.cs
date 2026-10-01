using Microsoft.AspNetCore.Authorization;
using QiadHelper.Core.Data.Security;
using QiadHelper.Web.Security.Authentication;

namespace QiadHelper.Web.Security.Authorization;

public sealed class QiadRankAuthorizationHandler : AuthorizationHandler<QiadRankRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
                                                   QiadRankRequirement requirement)
    {
        var isApiKey =
            context.User?.Identities?.Any(i => i.AuthenticationType == ApiKeyAuthenticationHandler.Scheme) == true
            || context.User?.HasClaim("auth.kind", "api-key") == true;

        if (isApiKey)
        {
            var scopeClaim = context.User?.FindFirst("api-key.scope")?.Value;
            if (int.TryParse(scopeClaim, out var scopeValue) &&
                ((ApiKeyScope)scopeValue).HasFlag(ApiKeyScope.Admin))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }

        var isDeveloper = context.User?.FindFirst(QiadClaimTypes.IsDeveloper)?.Value;
        if (string.Equals(isDeveloper, "true", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);

            return Task.CompletedTask;
        }

        var rankValue = context.User?.FindFirst(QiadClaimTypes.Rank)?.Value;
        if (int.TryParse(rankValue, out var rank) && rank >= requirement.MinRankValue)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
