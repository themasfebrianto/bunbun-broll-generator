using System.Collections.Concurrent;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Singleton event bus for broadcasting generation progress across Blazor circuits.
/// UI components subscribe by sessionId and receive progress updates from background jobs.
/// </summary>
public class GenerationEventBus
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<Action<GenerationProgressEvent>>> _subscribers = new();

    /// <summary>
    /// Subscribe to progress events for a specific session.
    /// Returns an IDisposable that removes the subscription when disposed.
    /// </summary>
    public IDisposable Subscribe(string sessionId, Action<GenerationProgressEvent> callback)
    {
        var bag = _subscribers.GetOrAdd(sessionId, _ => new ConcurrentBag<Action<GenerationProgressEvent>>());
        bag.Add(callback);
        return new Subscription(this, sessionId, callback);
    }

    /// <summary>
    /// Publish a progress event to all subscribers of the given session.
    /// </summary>
    public void Publish(string sessionId, GenerationProgressEvent evt)
    {
        if (_subscribers.TryGetValue(sessionId, out var bag))
        {
            foreach (var callback in bag)
            {
                try { callback(evt); }
                catch { /* subscriber errors should not crash the publisher */ }
            }
        }
    }

    private void Unsubscribe(string sessionId, Action<GenerationProgressEvent> callback)
    {
        if (_subscribers.TryGetValue(sessionId, out var bag))
        {
            // ConcurrentBag doesn't support removal, so rebuild without the target
            var remaining = new ConcurrentBag<Action<GenerationProgressEvent>>(
                bag.Where(cb => cb != callback));
            _subscribers.TryUpdate(sessionId, remaining, bag);

            // Clean up empty entries
            if (remaining.IsEmpty)
                _subscribers.TryRemove(sessionId, out _);
        }
    }

    private class Subscription : IDisposable
    {
        private readonly GenerationEventBus _bus;
        private readonly string _sessionId;
        private readonly Action<GenerationProgressEvent> _callback;
        private bool _disposed;

        public Subscription(GenerationEventBus bus, string sessionId, Action<GenerationProgressEvent> callback)
        {
            _bus = bus;
            _sessionId = sessionId;
            _callback = callback;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _bus.Unsubscribe(_sessionId, _callback);
            }
        }
    }
}
