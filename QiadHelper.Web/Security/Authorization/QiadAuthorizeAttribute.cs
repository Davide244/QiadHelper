using Microsoft.AspNetCore.Authorization;
using QiadHelper.Core;

namespace QiadHelper.Web.Security.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class QiadAuthorizeAttribute : AuthorizeAttribute
{
    public QiadAuthorizeAttribute(QiadRank minimumRank)
    {
        Policy = $"QiadMinRank:{(int)minimumRank}";
    }
}
