using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QiadHelper.Web.Controllers;

public class ErrorController : Controller
{
    [HttpGet("/error/{code:int}")]
    [AllowAnonymous]
    public IActionResult Index(int code, string? m = null)
    {
        ViewBag.Code = code;
        ViewBag.Message = (code, m) switch
        {
            (401, "auth-failed")         => "Discord sign-in failed or was cancelled.",
            (401, "oauth-unconfigured")  => "Discord sign-in is not configured on this instance (Discord:ClientId / ClientSecret).",
            (401, _)                     => "You need to sign in with Discord to view this page.",
            (403, _)             => "Your QIAD rank does not permit this page.",
            (404, _)             => "That page does not exist.",
            (429, _)             => "Too many requests. Try again in a minute.",
            _                    => "Something went wrong on our side."
        };

        ViewBag.ShowLogin = code == 401 && m != "oauth-unconfigured";
        Response.StatusCode = code is >= 400 and < 600 ? code : 500;

        return View("Error");
    }
}
