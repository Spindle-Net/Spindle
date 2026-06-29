using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Spindle.Transport;

namespace Spindle.Transport.InMemory;

public class InMemorySpindleTransport : ISpindleTransport
{
    private readonly object _gate = new();
    private readonly List<SpindleMessage> _messages = [];
    private readonly List<Subscriber> _subscribers = [];

    public IReadOnlyList<SpindleMessage> PublishedMessages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    public ValueTask PublishAsync(
        SpindleMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _messages.Add(message);

            foreach (var subscriber in _subscribers)
            {
                if (Matches(subscriber.Subscription, message))
                {
                    subscriber.Channel.Writer.TryWrite(message);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<SpindleMessage> SubscribeAsync(
        SpindleSubscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<SpindleMessage>();
        var subscriber = new Subscriber(subscription, channel);

        lock (_gate)
        {
            _subscribers.Add(subscriber);

            foreach (var message in _messages)
            {
                if (Matches(subscription, message))
                {
                    channel.Writer.TryWrite(message);
                }
            }
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var message))
                {
                    yield return message;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(subscriber);
            }

            channel.Writer.TryComplete();
        }
    }

    private static bool Matches(SpindleSubscription subscription, SpindleMessage message)
    {
        if (message.TargetApplication is { } target &&
            target != subscription.Application)
        {
            return false;
        }

        if (subscription.Queue is { } queue &&
            message.Queue != queue)
        {
            return false;
        }

        return subscription.MessageKinds.Count == 0 ||
               subscription.MessageKinds.Contains(message.Kind);
    }

    private sealed record Subscriber(
        SpindleSubscription Subscription,
        Channel<SpindleMessage> Channel);
}
