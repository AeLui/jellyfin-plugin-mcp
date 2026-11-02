using System;

namespace Jellyfin.Plugin.Mcp.Configuration;

/// <summary>
/// <see cref="IPluginSettings"/> backed by the current plugin configuration.
/// </summary>
public sealed class PluginSettings : IPluginSettings
{
    private const int DefaultMaxPageSize = 100;

    /// <inheritdoc />
    public int MaxPageSize => Math.Max(1, Plugin.Instance?.Configuration.MaxPageSize ?? DefaultMaxPageSize);
}
