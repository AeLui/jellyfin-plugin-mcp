namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// The MCP-specific HTTP request headers the dispatcher needs, as raw values.
/// </summary>
/// <param name="ProtocolVersion">The <c>MCP-Protocol-Version</c> header, if any.</param>
/// <param name="Method">The <c>Mcp-Method</c> header, if any.</param>
/// <param name="Name">The <c>Mcp-Name</c> header, if any.</param>
public sealed record McpRequestHeaders(string? ProtocolVersion, string? Method, string? Name)
{
    /// <summary>
    /// Gets an instance with no headers.
    /// </summary>
    public static McpRequestHeaders None { get; } = new(null, null, null);
}
