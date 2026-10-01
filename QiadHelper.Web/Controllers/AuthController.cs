using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiadHelper.Web.Extensions;

namespace QiadHelper.Web.Controllers;

public class AuthController : Controller
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("/auth/login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (!WebServiceCollectionExtensions.IsOAuthConfigured(_config))
        {
            return Redirect("/error/401?m=oauth-unconfigured");
        }

        var redirect = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";

        return Challenge(new AuthenticationProperties
                         {
                             RedirectUri = redirect
                         },
            DiscordAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpPost("/auth/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Redirect("/");
    }
}
