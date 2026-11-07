using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Api;

/// <summary>
/// The request body for creating a token.
/// </summary>
public sealed class CreateTokenRequest
{
    /// <summary>
    /// Gets the display name (1 to 100 characters).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the id of the Jellyfin user the token acts as.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the requested scopes.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the ids of the libraries the token is limited to, or null or empty for every library the user can access.
    /// A restricted token cannot have the admin scope.
    /// </summary>
    public IReadOnlyList<Guid>? LibraryIds { get; init; }

    /// <summary>
    /// Gets the lifetime in days (30, 90 or 365), or null for a token that never expires.
    /// </summary>
    public int? ExpiresInDays { get; init; }
}
