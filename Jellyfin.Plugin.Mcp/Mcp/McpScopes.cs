using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// The scopes a token can be granted.
/// </summary>
public static class McpScopes
{
    /// <summary>Read-only access to the library.</summary>
    public const string Library = "library";

    /// <summary>Playback and session control.</summary>
    public const string Playback = "playback";

    /// <summary>Modification of the user's own data and playlists.</summary>
    public const string Manage = "manage";

    /// <summary>Server administration.</summary>
    public const string Admin = "admin";

    /// <summary>
    /// Gets every valid scope.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { Library, Playback, Manage, Admin };
}
