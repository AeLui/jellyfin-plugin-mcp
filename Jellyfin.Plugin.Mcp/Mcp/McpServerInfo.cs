namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Identity advertised in the MCP <c>initialize</c> response.
/// </summary>
/// <param name="Name">The server name.</param>
/// <param name="Version">The server version.</param>
public sealed record McpServerInfo(string Name, string Version)
{
    /// <summary>
    /// Creates the server info for this plugin, using the assembly version.
    /// </summary>
    /// <returns>The server info.</returns>
    public static McpServerInfo FromAssembly()
    {
        var version = typeof(McpServerInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new McpServerInfo("jellyfin-mcp", version);
    }
}
