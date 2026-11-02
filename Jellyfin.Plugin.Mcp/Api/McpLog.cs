using System;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mcp.Api;

/// <summary>
/// Log messages of the MCP endpoint. They never include tool arguments or tokens.
/// </summary>
internal static partial class McpLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "MCP tool call {ToolName} by user {UserId} took {DurationMs} ms (success: {Success})")]
    public static partial void ToolCall(ILogger logger, string toolName, Guid userId, long durationMs, bool success);

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP tool {ToolName} failed unexpectedly")]
    public static partial void ToolFailure(ILogger logger, Exception exception, string toolName);
}
