using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// In-memory sliding-window limiter for failed authentication attempts per IP.
/// </summary>
public sealed class AuthFailureLimiter
{
    private readonly IClock _clock;
    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _failures = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthFailureLimiter"/> class.
    /// </summary>
    /// <param name="clock">The clock.</param>
    /// <param name="maxFailures">Failures allowed within the window before blocking.</param>
    /// <param name="window">The sliding window; one minute when null.</param>
    public AuthFailureLimiter(IClock clock, int maxFailures = 10, TimeSpan? window = null)
    {
        _clock = clock;
        _maxFailures = maxFailures;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Gets a value indicating whether the address has used up its failures.
    /// </summary>
    /// <param name="ip">The remote address.</param>
    /// <returns>True when blocked.</returns>
    public bool IsBlocked(string ip)
    {
        Prune();
        return _failures.TryGetValue(ip, out var queue) && Count(queue) >= _maxFailures;
    }

    /// <summary>
    /// Records a failed attempt.
    /// </summary>
    /// <param name="ip">The remote address.</param>
    public void RecordFailure(string ip)
    {
        Prune();
        var queue = _failures.GetOrAdd(ip, static _ => new Queue<DateTime>());
        lock (queue)
        {
            queue.Enqueue(_clock.UtcNow);
        }
    }

    private static int Count(Queue<DateTime> queue)
    {
        lock (queue)
        {
            return queue.Count;
        }
    }

    private void Prune()
    {
        var cutoff = _clock.UtcNow - _window;
        foreach (var (ip, queue) in _failures)
        {
            bool empty;
            lock (queue)
            {
                while (queue.Count > 0 && queue.Peek() <= cutoff)
                {
                    queue.Dequeue();
                }

                empty = queue.Count == 0;
            }

            if (empty)
            {
                _failures.TryRemove(new KeyValuePair<string, Queue<DateTime>>(ip, queue));
            }
        }
    }
}
