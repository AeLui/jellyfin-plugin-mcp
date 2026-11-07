using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Mcp.Configuration;

/// <summary>
/// An MCP access token. Only the SHA-256 hash of the secret is stored.
/// </summary>
public class McpToken
{
    /// <summary>
    /// Gets or sets the token id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the id of the Jellyfin user the token acts as.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the granted scopes.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only (required for XML serialization)
    public Collection<string> Scopes { get; set; } = new();
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the ids of the libraries the token is limited to; empty means every library the user can access.
    /// </summary>
    /// <remarks>
    /// Tokens saved before this field existed deserialize with an empty list, so they stay unrestricted.
    /// </remarks>
#pragma warning disable CA2227 // Collection properties should be read only (required for XML serialization)
    public Collection<Guid> LibraryIds { get; set; } = new();
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the lowercase SHA-256 hex hash of the token.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the first characters of the secret, for display.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation time (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time of last use (UTC).
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Gets or sets the expiry time (UTC), or null for no expiry.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the token is enabled.
    /// </summary>
    public bool Enabled { get; set; }
}
