using Microsoft.Extensions.DependencyInjection;

namespace QiadHelper.Core.Notifications;

public static class NotificationServiceCollectionExtensions
{
    /// <summary>Registers the in-process notification bus (one singleton, publisher + reader).</summary>
    public static IServiceCollection AddQiadNotifications(this IServiceCollection services)
    {
        services.AddSingleton<InProcessNotificationBus>();
        services.AddSingleton<IQiadNotificationPublisher>(sp => sp.GetRequiredService<InProcessNotificationBus>());
        return services;
    }
}
