using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// The authenticated caller of an MCP request.
/// </summary>
/// <param name="UserId">The Jellyfin user the token acts as.</param>
/// <param name="Scopes">The scopes granted to the token.</param>
public sealed record McpPrincipal(Guid UserId, IReadOnlySet<string> Scopes);
