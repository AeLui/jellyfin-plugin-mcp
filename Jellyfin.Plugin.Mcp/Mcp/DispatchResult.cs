namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Result of dispatching one HTTP body.
/// </summary>
/// <param name="StatusCode">The HTTP status code to return.</param>
/// <param name="Body">The JSON response body, or <c>null</c> for an empty response.</param>
public sealed record DispatchResult(int StatusCode, string? Body);
