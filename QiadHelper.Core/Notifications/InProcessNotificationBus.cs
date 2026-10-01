using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace QiadHelper.Core.Notifications;

/// <summary>
///     Channel-backed in-memory dispatcher (design §7.5, replaces QDF's Redis pub/sub).
///     Web/bot code enqueues typed notifications; the bot-side hosted consumer drains
///     <see cref="Reader" />. Unbounded is fine here: notifications are tiny and produced at
///     human speed, and buffering covers "bot not ready yet" moments during startup.
/// </summary>
public sealed class InProcessNotificationBus : IQiadNotificationPublisher
{
    private readonly Channel<QiadNotification> _channel =
        Channel.CreateUnbounded<QiadNotification>(new UnboundedChannelOptions
                                                  {
                                                      SingleReader = true
                                                  });

    private readonly ILogger<InProcessNotificationBus> _logger;

    public InProcessNotificationBus(ILogger<InProcessNotificationBus> logger)
    {
        _logger = logger;
    }

    public ChannelReader<QiadNotification> Reader => _channel.Reader;

    public ValueTask PublishAsync(QiadNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Publishing {Notification}", notification.GetType().Name);
        return _channel.Writer.WriteAsync(notification, cancellationToken);
    }
}
