using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// The authenticated caller of an MCP request.
/// </summary>
/// <param name="UserId">The Jellyfin user the token acts as.</param>
/// <param name="Scopes">The scopes granted to the token.</param>
public sealed record McpPrincipal(Guid UserId, IReadOnlySet<string> Scopes)
{
    private static readonly IReadOnlySet<Guid> NoLibraries = new HashSet<Guid>();

    /// <summary>
    /// Gets the ids of the libraries the token is limited to; empty means every library the user can access.
    /// </summary>
    public IReadOnlySet<Guid> LibraryIds { get; init; } = NoLibraries;

    /// <summary>
    /// Gets a value indicating whether the token is limited to some libraries.
    /// </summary>
    public bool IsLibraryRestricted => LibraryIds.Count > 0;
}
