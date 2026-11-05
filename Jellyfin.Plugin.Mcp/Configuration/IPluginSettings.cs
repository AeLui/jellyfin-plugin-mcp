using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Configuration;

/// <summary>
/// Read access to the live plugin settings that tools depend on.
/// </summary>
public interface IPluginSettings
{
    /// <summary>
    /// Gets the maximum page size accepted by list tools, never below 1.
    /// </summary>
    int MaxPageSize { get; }

    /// <summary>
    /// Gets the browser origins allowed to call the MCP endpoint, besides the server's own host.
    /// </summary>
    IReadOnlyCollection<string> AllowedOrigins { get; }
}
