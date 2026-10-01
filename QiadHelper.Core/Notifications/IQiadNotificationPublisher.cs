namespace QiadHelper.Core.Notifications;

/// <summary>
///     Publish side of the in-process notification dispatcher. Same interface shape as QDF's
///     Redis publisher so a future split back to two processes only swaps the implementation.
///     Publishing never blocks on Discord work and never throws for consumer-side failures.
/// </summary>
public interface IQiadNotificationPublisher
{
    ValueTask PublishAsync(QiadNotification notification, CancellationToken cancellationToken = default);
}
