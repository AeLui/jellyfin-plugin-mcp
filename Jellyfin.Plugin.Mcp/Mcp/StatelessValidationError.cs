namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// A stateless request validation failure; always answered with HTTP 400.
/// </summary>
/// <param name="Code">The JSON-RPC error code.</param>
/// <param name="Message">The error message (never contains argument values).</param>
public sealed record StatelessValidationError(int Code, string Message);
