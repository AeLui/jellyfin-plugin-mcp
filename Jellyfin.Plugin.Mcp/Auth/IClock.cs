using System;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// Abstraction over the system clock to make time-dependent logic testable.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current UTC time.
    /// </summary>
    DateTime UtcNow { get; }
}
