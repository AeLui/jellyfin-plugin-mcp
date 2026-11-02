using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// The result of an authentication attempt.
/// </summary>
/// <param name="Status">The outcome.</param>
/// <param name="Principal">The principal, on success.</param>
/// <param name="Token">The matched token, on success.</param>
public sealed record AuthResult(AuthStatus Status, McpPrincipal? Principal, McpToken? Token);
