using System;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// <see cref="IClock"/> backed by <see cref="DateTime.UtcNow"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
