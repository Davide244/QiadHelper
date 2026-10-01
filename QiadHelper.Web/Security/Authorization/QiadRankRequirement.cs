using Microsoft.AspNetCore.Authorization;

namespace QiadHelper.Web.Security.Authorization;

public sealed record QiadRankRequirement(
    int MinRankValue
) : IAuthorizationRequirement;
