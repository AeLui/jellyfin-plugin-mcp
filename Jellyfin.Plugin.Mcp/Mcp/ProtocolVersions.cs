using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// MCP protocol revisions supported by this server.
/// </summary>
/// <remarks>
/// Handshake revisions negotiate the version once through <c>initialize</c>. Stateless revisions (2026-07-28 and later)
/// have no handshake: every request names its version in the <c>MCP-Protocol-Version</c> header and in <c>_meta</c>.
/// </remarks>
public static class ProtocolVersions
{
    /// <summary>
    /// Gets the supported handshake versions, newest first.
    /// </summary>
    public static IReadOnlyList<string> Supported { get; } = new[] { "2025-11-25", "2025-06-18", "2025-03-26" };

    /// <summary>
    /// Gets the supported stateless versions, newest first.
    /// </summary>
    public static IReadOnlyList<string> Stateless { get; } = new[] { "2026-07-28" };

    /// <summary>
    /// Gets every supported version, newest first.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Stateless.Concat(Supported).ToArray();

    /// <summary>
    /// Gets the newest supported handshake version.
    /// </summary>
    public static string Latest => Supported[0];

    /// <summary>
    /// Picks the handshake version to use for a client that requested <paramref name="requested"/>.
    /// </summary>
    /// <param name="requested">The version requested by the client, if any.</param>
    /// <returns>The requested version when it is a supported handshake version, otherwise <see cref="Latest"/>.</returns>
    public static string Negotiate(string? requested)
        => requested is not null && IsHandshake(requested) ? requested : Latest;

    /// <summary>
    /// Gets a value indicating whether <paramref name="version"/> is a supported handshake version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns><c>true</c> for a supported handshake version.</returns>
    public static bool IsHandshake(string version) => Supported.Contains(version, StringComparer.Ordinal);

    /// <summary>
    /// Gets a value indicating whether <paramref name="version"/> is a supported stateless version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns><c>true</c> for a supported stateless version.</returns>
    public static bool IsStateless(string version) => Stateless.Contains(version, StringComparer.Ordinal);
}
