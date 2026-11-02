using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Registry of the MCP tools exposed by the server.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IMcpTool> _tools = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
    /// </summary>
    /// <param name="tools">The available tools.</param>
    public ToolRegistry(IEnumerable<IMcpTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var tool in tools)
        {
            if (!_tools.TryAdd(tool.Name, tool))
            {
                throw new ArgumentException("Duplicate tool name: " + tool.Name, nameof(tools));
            }
        }
    }

    /// <summary>
    /// Lists the tools the principal may use, sorted by name.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <returns>The visible tools.</returns>
    public IReadOnlyList<IMcpTool> ListFor(McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return _tools.Values
            .Where(t => principal.Scopes.Contains(t.RequiredScope))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Finds a tool by name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="principal">The caller.</param>
    /// <returns>The tool, or <c>null</c> when it is unknown or outside the caller's scopes.</returns>
    public IMcpTool? Find(string name, McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(principal);
        return _tools.TryGetValue(name, out var tool) && principal.Scopes.Contains(tool.RequiredScope) ? tool : null;
    }
}
