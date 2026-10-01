using System.Security.Claims;
using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Core.Data;
using QiadHelper.Web.Security.Authentication;
using QiadHelper.Web.Services.Security;

namespace QiadHelper.Web.Extensions;

public static class WebServiceCollectionExtensions
{
    public static IServiceCollection AddQiadDatabase(this IServiceCollection services,
                                                     IConfiguration config,
                                                     IHostEnvironment env)
    {
        // Development reads the checked-in appsettings.Development.json (matching
        // docker-compose.dev.yml); containers get ConnectionStrings__Qiad from the environment.
        // The fallback only exists so `dotnet ef migrations add` (design-time, no connection
        // needed) works even under a bare Production environment; startup fails fast on it.
        var connectionString = config.GetConnectionString("Qiad");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=qiad;Username=qiad;Password=qiad";
        }

        services.AddDbContext<QiadContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.EnableDetailedErrors();
            if (env.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
            }
        });

        return services;
    }

    /// <summary>True when Discord OAuth credentials are configured (login is possible).</summary>
    public static bool IsOAuthConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Discord:ClientId"]) &&
        !string.IsNullOrWhiteSpace(config["Discord:ClientSecret"]);

    public static IServiceCollection AddQiadAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var authBuilder = services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(o =>
            {
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Cookie.MaxAge = TimeSpan.FromDays(30);
                o.Cookie.HttpOnly = true;
                o.Cookie.IsEssential = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;

                o.Events = new CookieAuthenticationEvents
                           {
                               OnRedirectToLogin = ctx =>
                               {
                                   if (ctx.Request.Path.StartsWithSegments("/api"))
                                   {
                                       ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;

                                       return Task.CompletedTask;
                                   }

                                   if (ctx.Request.Path.StartsWithSegments("/error"))
                                   {
                                       return Task.CompletedTask; // avoid loop
                                   }

                                   ctx.Response.Redirect("/error/401");

                                   return Task.CompletedTask;
                               },
                               OnRedirectToAccessDenied = ctx =>
                               {
                                   if (ctx.Request.Path.StartsWithSegments("/api"))
                                   {
                                       ctx.Response.StatusCode = StatusCodes.Status403Forbidden;

                                       return Task.CompletedTask;
                                   }

                                   if (ctx.Request.Path.StartsWithSegments("/error"))
                                   {
                                       return Task.CompletedTask;
                                   }

                                   ctx.Response.Redirect("/error/403");

                                   return Task.CompletedTask;
                               }
                           };
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.Scheme,
                _ => { });

        // Register the Discord scheme only when credentials exist: remote auth handlers are
        // initialized on EVERY request (callback-path interception), and an empty ClientId
        // fails options validation — which would 500 the whole site in web-only/dev mode.
        if (!IsOAuthConfigured(config))
        {
            return services;
        }

        authBuilder.AddDiscord(options =>
            {
                options.ClientId = config["Discord:ClientId"] ?? string.Empty;
                options.ClientSecret = config["Discord:ClientSecret"] ?? string.Empty;
                options.Scope.Add("identify");
                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
                options.SaveTokens = false;
                options.Events = new OAuthEvents
                                 {
                                     OnCreatingTicket = async ctx =>
                                     {
                                         // Any Discord user may hold a session — staff pages are
                                         // gated by rank claims resolved from guild roles (§8.3).
                                         var id = ctx.Identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                                         var name = ctx.Identity?.FindFirst(ClaimTypes.Name)?.Value;

                                         using var scope = ctx.HttpContext.RequestServices.CreateScope();
                                         var tracker = scope.ServiceProvider
                                             .GetRequiredService<ILoginTrackingService>();
                                         await tracker.LogSuccessfulLoginAsync(id, name,
                                             ctx.HttpContext.Connection.RemoteIpAddress?.ToString(),
                                             ctx.HttpContext.Request.Headers.UserAgent.ToString());
                                     },
                                     OnRemoteFailure = async ctx =>
                                     {
                                         using var scope = ctx.HttpContext.RequestServices.CreateScope();
                                         var tracker = scope.ServiceProvider
                                             .GetRequiredService<ILoginTrackingService>();
                                         await tracker.LogFailedLoginAsync(null, null,
                                             ctx.Failure?.Message ?? "Authentication failed",
                                             ctx.HttpContext.Connection.RemoteIpAddress?.ToString(),
                                             ctx.HttpContext.Request.Headers.UserAgent.ToString());

                                         ctx.Response.Redirect("/error/401?m=auth-failed");
                                         ctx.HandleResponse();
                                     }
                                 };
            });

        return services;
    }

    public static IServiceCollection AddQiadWebServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<ILoginTrackingService, LoginTrackingService>();
        services.AddScoped<IClaimsTransformation, QiadClaimsTransformation>();
        services.AddScoped<Services.Backup.IDatabaseBackupService, Services.Backup.DatabaseBackupService>();

        return services;
    }
}
