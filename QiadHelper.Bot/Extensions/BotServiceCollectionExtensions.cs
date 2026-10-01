using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QiadHelper.Bot.Configuration;
using QiadHelper.Bot.Infrastructure;
using QiadHelper.Bot.Services;
using QiadHelper.Core.Services;

namespace QiadHelper.Bot.Extensions;

public static class BotServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the whole Discord bot inside the calling host (design §7.1). This is the DI
    ///     recipe from QDF's bot Program.cs, minus what a single process makes obsolete: no Redis,
    ///     no typed HTTP clients — bot code injects the same application services the web uses.
    /// </summary>
    public static IServiceCollection AddQiadBot(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DiscordOptions>(configuration.GetSection(DiscordOptions.SectionName));
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
        services.Configure<GuardsmanOptions>(configuration.GetSection(GuardsmanOptions.SectionName));

        // Discord→Roblox identity resolution via Guardsman (no-op when unconfigured).
        services.AddHttpClient("Guardsman", c => c.Timeout = TimeSpan.FromSeconds(6));
        services.AddHttpClient("Roblox", c => c.Timeout = TimeSpan.FromSeconds(6));
        services.AddSingleton<IRobloxIdentityResolver, GuardsmanIdentityResolver>();

        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
                                                      {
                                                          GatewayIntents =
                                                              GatewayIntents.AllUnprivileged |
                                                              GatewayIntents.GuildMembers,
                                                          LogGatewayIntentWarnings = false
                                                      }));
        // AutoServiceScopes: modules inject scoped services (QiadContext, ICaseService) safely.
        services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig
            {
                AutoServiceScopes = true
            }));
        services.AddSingleton<InteractionHandler>();

        services.AddSingleton<IDirectMessageService, DirectMessageService>();
        services.AddSingleton<IBotErrorLogService, BotErrorLogService>();
        services.AddSingleton<IRankResolver, DiscordRankResolver>();
        services.AddSingleton<CaseDiscordService>();

        services.AddSingleton<QiadBot>();
        services.AddHostedService<Worker>();
        services.AddHostedService<QiadNotificationConsumerService>();
        // Evidence intake is disabled for now: no DM listener, no attach button in the
        // confirmation DM (EvidenceModule answers stale buttons with a disabled notice).
        // services.AddHostedService<EvidenceDmListenerService>();
        services.AddHostedService<RestrictionExpiryService>();

        return services;
    }
}
