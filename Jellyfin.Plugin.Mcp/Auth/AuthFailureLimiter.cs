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
    private readonly int _maxTrackedAddresses;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _failures = new(StringComparer.Ordinal);
    private readonly object _sweepLock = new();
    private DateTime _lastSweep;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthFailureLimiter"/> class.
    /// </summary>
    /// <param name="clock">The clock.</param>
    /// <param name="maxFailures">Failures allowed within the window before blocking.</param>
    /// <param name="window">The sliding window; one minute when null.</param>
    /// <param name="maxTrackedAddresses">
    /// Upper bound on tracked addresses. When it is reached, failures from addresses not yet tracked
    /// are dropped and untracked addresses are never reported as blocked (fail open), so an attacker
    /// spraying from many addresses cannot lock out legitimate users. Tracked addresses behave normally.
    /// Space is only freed by the periodic sweep (at most once per window) or by an address's own pruning.
    /// </param>
    public AuthFailureLimiter(IClock clock, int maxFailures = 10, TimeSpan? window = null, int maxTrackedAddresses = 10_000)
    {
        _clock = clock;
        _maxFailures = maxFailures;
        _maxTrackedAddresses = maxTrackedAddresses;
        _window = window ?? TimeSpan.FromMinutes(1);
        _lastSweep = clock.UtcNow;
    }

    /// <summary>
    /// Gets the number of addresses currently tracked.
    /// </summary>
    public int TrackedAddressCount => _failures.Count;

    /// <summary>
    /// Gets a value indicating whether the address has used up its failures.
    /// </summary>
    /// <param name="ip">The remote address.</param>
    /// <returns>True when blocked.</returns>
    public bool IsBlocked(string ip)
    {
        SweepIfDue();
        if (_failures.TryGetValue(ip, out var queue))
        {
            return PruneAndCount(ip, queue) >= _maxFailures;
        }

        return false;
    }

    /// <summary>
    /// Records a failed attempt.
    /// </summary>
    /// <param name="ip">The remote address.</param>
    public void RecordFailure(string ip)
    {
        SweepIfDue();
        if (!_failures.ContainsKey(ip) && IsFull())
        {
            return;
        }

        var queue = _failures.GetOrAdd(ip, static _ => new Queue<DateTime>());
        lock (queue)
        {
            queue.Enqueue(_clock.UtcNow);
        }
    }

    private bool IsFull() => _failures.Count >= _maxTrackedAddresses;

    private int PruneAndCount(string ip, Queue<DateTime> queue)
    {
        var cutoff = _clock.UtcNow - _window;
        int count;
        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() <= cutoff)
            {
                queue.Dequeue();
            }

            count = queue.Count;
        }

        if (count == 0)
        {
            _failures.TryRemove(new KeyValuePair<string, Queue<DateTime>>(ip, queue));
        }

        return count;
    }

    private void SweepIfDue()
    {
        var now = _clock.UtcNow;
        lock (_sweepLock)
        {
            if (now - _lastSweep < _window)
            {
                return;
            }

            _lastSweep = now;
        }

        foreach (var (ip, queue) in _failures)
        {
            PruneAndCount(ip, queue);
        }
    }
}
