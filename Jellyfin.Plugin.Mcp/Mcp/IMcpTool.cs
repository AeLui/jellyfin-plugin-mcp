using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// An MCP tool.
/// </summary>
public interface IMcpTool
{
    /// <summary>
    /// Gets the unique tool name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description shown to the model.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the scope a token needs to see and call this tool.
    /// </summary>
    string RequiredScope { get; }

    /// <summary>
    /// Gets the JSON schema of the tool arguments.
    /// </summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// Gets the behaviour hints.
    /// </summary>
    ToolAnnotations Annotations { get; }

    /// <summary>
    /// Runs the tool.
    /// </summary>
    /// <param name="args">The call arguments.</param>
    /// <param name="principal">The authenticated caller.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The tool result.</returns>
    Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct);
}
