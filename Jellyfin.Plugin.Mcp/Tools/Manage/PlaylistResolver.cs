using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.Mcp.Tools.Manage;

/// <summary>
/// Resolves playlists for a user and applies the same permission rules as Jellyfin's playlist API.
/// </summary>
public sealed class PlaylistResolver
{
    /// <summary>
    /// The message returned for unknown playlists and for playlists the user may not use.
    /// </summary>
    public const string NotFound = "Playlist not found";

    private readonly ItemLookup _lookup;
    private readonly IPlaylistManager _playlistManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistResolver"/> class.
    /// </summary>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="playlistManager">The playlist manager.</param>
    public PlaylistResolver(ItemLookup lookup, IPlaylistManager playlistManager)
    {
        _lookup = lookup;
        _playlistManager = playlistManager;
    }

    /// <summary>
    /// Gets the id that identifies a playlist entry for <c>RemoveItemFromPlaylistAsync</c>.
    /// </summary>
    /// <param name="child">The linked child.</param>
    /// <returns>The entry id, or <c>null</c> when the entry has no item id.</returns>
    public static string? EntryId(LinkedChild child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return child.ItemId?.ToString("N", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets a playlist the user is allowed to read.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="id">The playlist id supplied by the caller.</param>
    /// <returns>The playlist.</returns>
    /// <exception cref="ToolException">The playlist is unknown or not readable by the user.</exception>
    public Playlist GetReadable(User user, Guid id)
    {
        var playlist = Find(user, id);
        var allowed = playlist.OpenAccess
            || playlist.OwnerUserId.Equals(user.Id)
            || playlist.Shares.Any(s => s.UserId.Equals(user.Id));
        return allowed ? playlist : throw new ToolException(NotFound);
    }

    /// <summary>
    /// Gets a playlist the user may change: one they own or one shared with them with edit permission.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="id">The playlist id supplied by the caller.</param>
    /// <returns>The playlist.</returns>
    /// <exception cref="ToolException">The playlist is unknown or not editable by the user.</exception>
    public Playlist GetEditable(User user, Guid id)
    {
        var playlist = Find(user, id);
        var allowed = playlist.OwnerUserId.Equals(user.Id)
            || playlist.Shares.Any(s => s.CanEdit && s.UserId.Equals(user.Id));
        return allowed ? playlist : throw new ToolException(NotFound);
    }

    /// <summary>
    /// Gets a playlist the user owns.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="id">The playlist id supplied by the caller.</param>
    /// <returns>The playlist.</returns>
    /// <exception cref="ToolException">The playlist is unknown or not owned by the user.</exception>
    public Playlist GetOwned(User user, Guid id)
    {
        var playlist = Find(user, id);
        return playlist.OwnerUserId.Equals(user.Id) ? playlist : throw new ToolException(NotFound);
    }

    private Playlist Find(User user, Guid id)
    {
        ArgumentNullException.ThrowIfNull(user);
        try
        {
            _lookup.GetVisible(user, id);
        }
        catch (ToolException)
        {
            throw new ToolException(NotFound);
        }

        return _playlistManager.GetPlaylistForUser(id, user.Id) ?? throw new ToolException(NotFound);
    }
}
