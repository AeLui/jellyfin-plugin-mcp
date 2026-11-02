using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// MCP protocol revisions supported by this server (initialize-handshake revisions, newest first).
/// </summary>
public static class ProtocolVersions
{
    /// <summary>
    /// Gets the supported protocol versions, newest first.
    /// </summary>
    public static IReadOnlyList<string> Supported { get; } = new[] { "2025-11-25", "2025-06-18", "2025-03-26" };

    /// <summary>
    /// Gets the newest supported protocol version.
    /// </summary>
    public static string Latest => Supported[0];

    /// <summary>
    /// Picks the protocol version to use for a client that requested <paramref name="requested"/>.
    /// </summary>
    /// <param name="requested">The version requested by the client, if any.</param>
    /// <returns>The requested version when supported, otherwise <see cref="Latest"/>.</returns>
    public static string Negotiate(string? requested)
        => requested is not null && Supported.Contains(requested, StringComparer.Ordinal) ? requested : Latest;
}
