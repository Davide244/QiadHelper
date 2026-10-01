using Microsoft.Extensions.DependencyInjection;
using QiadHelper.Core.Notifications;
using QiadHelper.Core.Services;
using QiadHelper.Core.Services.Cases;
using QiadHelper.Core.Services.Restrictions;

namespace QiadHelper.Core.Extensions;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    ///     The shared application services — the single write path both the bot's interaction
    ///     modules and the web's controllers go through (design §7.1).
    /// </summary>
    public static IServiceCollection AddQiadCore(this IServiceCollection services)
    {
        services.AddQiadNotifications();

        services.AddSingleton<IChannelConfigService, ChannelConfigService>();
        services.AddScoped<ICaseService, CaseService>();
        services.AddScoped<IRestrictionService, RestrictionService>();

        return services;
    }
}
