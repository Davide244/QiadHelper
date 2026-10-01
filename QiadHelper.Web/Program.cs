using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Bot;
using QiadHelper.Bot.Extensions;
using QiadHelper.Core.Data;
using QiadHelper.Core.Extensions;
using QiadHelper.Web.Configuration;
using QiadHelper.Web.Data;
using QiadHelper.Web.Extensions;
using QiadHelper.Web.Middleware;
using QiadHelper.Web.Security.Authorization;

namespace QiadHelper.Web;

public class Program
{
    public static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

        var builder = WebApplication.CreateBuilder(args);

        if (!string.IsNullOrWhiteSpace(builder.Configuration["Sentry:Dsn"]))
        {
            builder.WebHost.UseSentry(options =>
            {
                builder.Configuration.GetSection("Sentry").Bind(options);
                options.Environment = builder.Environment.EnvironmentName;
                options.Release = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            });
        }

        // One host (design §7.1): the shared application services, the database, the web's own
        // auth stack, and the Discord bot's hosted services all live in this container.
        builder.Services
            .AddQiadDatabase(builder.Configuration, builder.Environment)
            .AddQiadCore()
            .AddQiadBot(builder.Configuration)
            .AddQiadAuthentication(builder.Configuration)
            .AddQiadWebServices();

        // Cookie keys live in Postgres so logins survive container redeploys (design §13.5).
        builder.Services.AddDataProtection()
            .PersistKeysToDbContext<QiadContext>()
            .SetApplicationName("QiadHelper");

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Only forward the scheme so Kestrel sees the original HTTPS behind the reverse proxy.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ApiKeyOrUser", policy =>
            {
                policy.AddAuthenticationSchemes(
                    QiadHelper.Web.Security.Authentication.ApiKeyAuthenticationHandler.Scheme,
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ApiKeyOrAuthenticatedRequirement());
            });

            options.AddPolicy("RequiresDeveloper", policy =>
            {
                policy.AddAuthenticationSchemes(
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(QiadHelper.Web.Security.Authentication.QiadClaimTypes.IsDeveloper, "true");
            });
        });
        builder.Services.AddSingleton<IAuthorizationHandler, ApiKeyOrAuthenticatedHandler>();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, QiadRankPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, QiadRankAuthorizationHandler>();

        builder.Services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var path = httpContext.Request.Path.Value ?? "";

                if (path.StartsWith("/error", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/img", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/healthz", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("excluded");
                }

                if (httpContext.Request.Headers.TryGetValue(ApiKeyOptions.HeaderName, out var keyHeader) &&
                    !string.IsNullOrWhiteSpace(keyHeader))
                {
                    // Partition by a hash of the key, not the key itself, so the raw secret
                    // never lands in rate-limiter state / diagnostics.
                    var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(keyHeader.ToString()));
                    var keyPartition = "apikey:" + Convert.ToHexString(keyHash)[..16];

                    return RateLimitPartition.GetFixedWindowLimiter(
                        keyPartition,
                        _ => new FixedWindowRateLimiterOptions
                             {
                                 AutoReplenishment = true,
                                 PermitLimit = 600,
                                 QueueLimit = 0,
                                 Window = TimeSpan.FromMinutes(1)
                             });
                }

                // Logged-in staff get a generous per-IP allowance; the strict limit is for
                // anonymous/public traffic. (Cookie presence, not validated identity — worst
                // case a forged cookie raises one IP's ceiling.)
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var hasAuthCookie = httpContext.Request.Cookies.ContainsKey(".AspNetCore.Cookies");

                return RateLimitPartition.GetFixedWindowLimiter(
                    (hasAuthCookie ? "user-ip:" : "ip:") + ip,
                    _ => new FixedWindowRateLimiterOptions
                         {
                             AutoReplenishment = true,
                             PermitLimit = hasAuthCookie ? 600 : 120,
                             QueueLimit = 0,
                             Window = TimeSpan.FromMinutes(1)
                         });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsync(
                        JsonSerializer.Serialize(new
                                                 {
                                                     error = "Too many requests. Please try again later."
                                                 }),
                        cancellationToken);
                }
                else
                {
                    context.HttpContext.Response.Redirect("/error/429");
                }
            };
        });

        builder.Services.AddControllersWithViews()
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });
        builder.Services.AddProblemDetails();

        var app = builder.Build();

        // Migrations + seed run before Kestrel serves traffic and before the bot logs in
        // (design §13.5): a fresh boot fully self-provisions. The database is NOT optional —
        // if PostgreSQL is unreachable we fail fast with an actionable message rather than
        // serving an app whose every page throws. (In compose, `depends_on: service_healthy`
        // plus the restart policy make this a non-event.)
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<QiadContext>();
            try
            {
                context.Database.Migrate();
                DefaultDataSeeder.SeedAsync(context).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex.GetBaseException() is SocketException or Npgsql.NpgsqlException)
            {
                var csb = new Npgsql.NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
                app.Logger.LogCritical(
                    "Cannot reach PostgreSQL at {Host}:{Port} (database '{Database}'). " +
                    "For local development start the dev database once with " +
                    "`docker compose -f docker-compose.dev.yml up -d` " +
                    "(credentials match appsettings.Development.json), or point ConnectionStrings:Qiad " +
                    "at your own server; user-secrets override the dev default. Underlying error: {Error}",
                    csb.Host, csb.Port, csb.Database, ex.GetBaseException().Message);

                throw;
            }
        }

        app.UseForwardedHeaders();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/error/500");
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseQiadSecurityHeaders();

        app.UseStaticFiles();
        app.UseRouting();

        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseStatusCodePagesWithReExecute("/error/{0}");

        app.MapControllerRoute(
            "areas",
            "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

        app.MapControllerRoute(
            "default",
            "{controller=Home}/{action=Index}/{id?}");

        // One endpoint tells you the whole system's health (design §13.1): DB connectivity and
        // Discord gateway state. Feeds the container healthcheck.
        app.MapGet("/healthz", async (QiadContext db, QiadBot bot, IConfiguration config) =>
        {
            var dbOk = false;
            try
            {
                dbOk = await db.Database.CanConnectAsync();
            }
            catch
            {
                // dbOk stays false
            }

            var botEnabled = config.GetValue("Bot:Enabled", true) &&
                             !string.IsNullOrWhiteSpace(config["Discord:BotToken"]);
            var botOk = !botEnabled || bot.IsConnected;

            var healthy = dbOk && botOk;
            var payload = new
                          {
                              status = healthy ? "healthy" : "unhealthy",
                              database = dbOk ? "up" : "down",
                              discord = botEnabled ? (bot.IsConnected ? "connected" : "disconnected") : "disabled"
                          };

            return healthy ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
        }).AllowAnonymous();

        app.Run();
    }
}
