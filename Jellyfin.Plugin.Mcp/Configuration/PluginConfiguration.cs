using System.Collections.ObjectModel;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Mcp.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the MCP endpoint is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum page size accepted by list tools.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets a value indicating whether tool calls are logged.
    /// </summary>
    public bool LogToolCalls { get; set; }

    /// <summary>
    /// Gets or sets the browser origins allowed to call the MCP endpoint, besides the server's own host.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only (required for XML serialization)
    public Collection<string> AllowedOrigins { get; set; } = new();
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the configured access tokens.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only (required for XML serialization)
    public Collection<McpToken> Tokens { get; set; } = new();
#pragma warning restore CA2227
}
