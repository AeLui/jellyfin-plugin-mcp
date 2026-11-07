using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// The Jellyfin user a token acts as, narrowed to the libraries the token is limited to.
/// </summary>
/// <remarks>
/// Item lookups and queries take this instead of a bare <see cref="User"/> so that no tool can forget the token's
/// library restriction. One instance lives for one tool call; it caches the resolved libraries.
/// </remarks>
public sealed class ScopedUser
{
    private static readonly IReadOnlySet<Guid> NoLibraries = new HashSet<Guid>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopedUser"/> class.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="libraryIds">The library ids the token is limited to; empty for no restriction.</param>
    public ScopedUser(User user, IReadOnlySet<Guid> libraryIds)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(libraryIds);
        User = user;
        LibraryIds = libraryIds;
    }

    /// <summary>
    /// Gets the user.
    /// </summary>
    public User User { get; }

    /// <summary>
    /// Gets the library ids the token is limited to; empty means every library the user can access.
    /// </summary>
    public IReadOnlySet<Guid> LibraryIds { get; }

    /// <summary>
    /// Gets a value indicating whether the token is limited to some libraries.
    /// </summary>
    public bool IsRestricted => LibraryIds.Count > 0;

    /// <summary>
    /// Gets or sets the libraries resolved for this call by <see cref="LibraryRestriction"/>.
    /// </summary>
    internal LibraryRestriction.Resolved? Resolved { get; set; }

    /// <summary>
    /// Creates a scope with no library restriction.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>The scope.</returns>
    public static ScopedUser Unrestricted(User user) => new(user, NoLibraries);
}
