namespace QiadHelper.Web.Middleware;

public static class SecurityHeadersExtensions
{
    /// <summary>
    ///     The standard hardening headers for both the anonymous public pages and the admin UI.
    ///     CSP allows self plus Discord CDN images (avatars/attachments in admin views).
    /// </summary>
    public static IApplicationBuilder UseQiadSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "img-src 'self' data: https://cdn.discordapp.com https://media.discordapp.net; " +
                "style-src 'self' 'unsafe-inline'; " +
                "script-src 'self'; " +
                "frame-ancestors 'none'";

            await next();
        });
    }
}
