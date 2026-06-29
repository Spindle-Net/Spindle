namespace Spindle.Transport;

public interface ISpindleTransport
{
    ValueTask PublishAsync(
        SpindleMessage message,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<SpindleMessage> SubscribeAsync(
        SpindleSubscription subscription,
        CancellationToken cancellationToken = default);
}
