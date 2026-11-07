using System;

namespace Jellyfin.Plugin.Mcp.Api;

/// <summary>
/// A library a token can be limited to, as shown to administrators.
/// </summary>
public sealed class TokenLibraryDto
{
    /// <summary>
    /// Gets the library id.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the library name, or an empty string when the library no longer exists.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the collection type (for example <c>movies</c> or <c>tvshows</c>), or null for mixed content.
    /// </summary>
    public string? CollectionType { get; init; }
}
