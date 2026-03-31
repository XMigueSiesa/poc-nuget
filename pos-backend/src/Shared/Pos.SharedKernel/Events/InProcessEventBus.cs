using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Pos.SharedKernel.Events;

public sealed class InProcessEventBus : IEventBus, IDisposable
{
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return Task.CompletedTask;

        var tasks = handlers
            .Cast<Func<TEvent, CancellationToken, Task>>()
            .Select(h => h(@event, ct));

        return Task.WhenAll(tasks);
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class
    {
        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => new List<object>());
        handlers.Add(handler);
        return new Subscription(() => handlers.Remove(handler));
    }

    public void Dispose()
    {
        _handlers.Clear();
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
