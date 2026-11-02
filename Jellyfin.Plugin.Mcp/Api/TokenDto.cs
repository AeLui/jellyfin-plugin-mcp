using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Api;

/// <summary>
/// A token as shown to administrators. Never contains the secret or its hash.
/// </summary>
public sealed class TokenDto
{
    /// <summary>
    /// Gets the token id.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the id of the user the token acts as.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the name of the user the token acts as, or an empty string when the user no longer exists.
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the granted scopes.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the first characters of the secret, for display.
    /// </summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>
    /// Gets the creation time (UTC).
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets the time of last use (UTC).
    /// </summary>
    public DateTime? LastUsedAt { get; init; }

    /// <summary>
    /// Gets the expiry time (UTC), or null for no expiry.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether the token is enabled.
    /// </summary>
    public bool Enabled { get; init; }
}
