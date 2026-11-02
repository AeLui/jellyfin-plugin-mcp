using System;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Metadata about one executed tool call. Deliberately carries no arguments.
/// </summary>
/// <param name="ToolName">The tool name.</param>
/// <param name="UserId">The Jellyfin user the call ran as.</param>
/// <param name="Duration">How long the call took.</param>
/// <param name="IsError">Whether the call produced an error result.</param>
public sealed record ToolCallLog(string ToolName, Guid UserId, TimeSpan Duration, bool IsError);
