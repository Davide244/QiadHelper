using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace QiadHelper.Web.Security.Authorization;

public sealed partial class QiadRankPolicyProvider : DefaultAuthorizationPolicyProvider
{
    private static readonly Regex Pattern = PolicyPattern();

    public QiadRankPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var match = Pattern.Match(policyName);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var min))
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new QiadRankRequirement(min))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return base.GetPolicyAsync(policyName);
    }

    [GeneratedRegex(@"^QiadMinRank:(-?\d+)$", RegexOptions.Compiled)]
    private static partial Regex PolicyPattern();
}
