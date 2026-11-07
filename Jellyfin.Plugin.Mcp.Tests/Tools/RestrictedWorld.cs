using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tools;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.Mcp.Tests.Tools;

/// <summary>
/// A user with two media libraries (Movies, allowed, and Kids, hidden by the token), a collections library and a
/// playlists folder, and a token restricted to Movies.
/// </summary>
internal sealed class RestrictedWorld
{
    private readonly Dictionary<Guid, BaseItem> _items = [];
    private readonly Dictionary<Guid, Folder[]> _collectionFolders = [];

    public RestrictedWorld()
    {
        Users.Setup(u => u.GetUserById(User.Id)).Returns(User);
        Settings.SetupGet(s => s.MaxPageSize).Returns(50);
        Movies = new CollectionFolder { Id = Guid.NewGuid(), Name = "Movies", CollectionType = CollectionType.movies, PhysicalFolderIds = [MoviesPhysical] };
        Kids = new CollectionFolder { Id = Guid.NewGuid(), Name = "Kids", CollectionType = CollectionType.movies, PhysicalFolderIds = [KidsPhysical] };
        Collections = new CollectionFolder { Id = Guid.NewGuid(), Name = "Collections", CollectionType = CollectionType.boxsets, PhysicalFolderIds = [CollectionsPhysical] };
        Playlists = new PlaylistsFolder { Id = Guid.NewGuid(), Name = "Playlists" };
        Root = new RootFolder { Libraries = [Movies, Kids, Collections, Playlists] };
        Library.Setup(l => l.GetUserRootFolder()).Returns(Root);
        Library.Setup(l => l.GetCollectionFolders(It.IsAny<BaseItem>()))
            .Returns<BaseItem>(i => _collectionFolders.TryGetValue(i.Id, out var folders) ? folders.ToList() : []);
        Library.Setup(l => l.GetItemById<BaseItem>(It.IsAny<Guid>(), It.IsAny<User>()))
            .Returns<Guid, User>((id, _) => _items.GetValueOrDefault(id));
        foreach (var library in Root.Libraries)
        {
            _items[library.Id] = library;
        }

        AllowedMovie = Add(new Movie { Id = Guid.NewGuid(), Name = "Allowed Movie" }, Movies);
        HiddenMovie = Add(new Movie { Id = Guid.NewGuid(), Name = "Hidden Movie" }, Kids);
        Principal = new McpPrincipal(User.Id, new HashSet<string> { McpScopes.Library, McpScopes.Playback, McpScopes.Manage })
        {
            LibraryIds = new HashSet<Guid> { Movies.Id },
        };
    }

    public User User { get; } = TestUsers.Create();

    public Mock<IUserManager> Users { get; } = new();

    public Mock<ILibraryManager> Library { get; } = new(MockBehavior.Strict);

    public Mock<IUserDataManager> UserData { get; } = new();

    public Mock<IUserViewManager> Views { get; } = new();

    public Mock<IPluginSettings> Settings { get; } = new();

    public Guid MoviesPhysical { get; } = Guid.NewGuid();

    public Guid KidsPhysical { get; } = Guid.NewGuid();

    public Guid CollectionsPhysical { get; } = Guid.NewGuid();

    public CollectionFolder Movies { get; }

    public CollectionFolder Kids { get; }

    public CollectionFolder Collections { get; }

    public PlaylistsFolder Playlists { get; }

    public RootFolder Root { get; }

    public Movie AllowedMovie { get; }

    public Movie HiddenMovie { get; }

    public McpPrincipal Principal { get; }

    public List<InternalItemsQuery> Queries { get; } = [];

    public ScopedUser Scoped => new(User, Principal.LibraryIds);

    public ItemLookup Lookup() => new(Library.Object, Views.Object);

    public T Add<T>(T item, params Folder[] libraries)
        where T : BaseItem
    {
        _items[item.Id] = item;
        _collectionFolders[item.Id] = libraries;
        return item;
    }

    /// <summary>
    /// Answers item queries: by-name probes (artist, person, ...) with <paramref name="byName"/>, others with <paramref name="items"/>.
    /// </summary>
    public void QueryReturns(IEnumerable<BaseItem> items, IEnumerable<BaseItem>? byName = null)
    {
        var list = items.ToList();
        var probes = byName?.ToList() ?? [];
        Library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(Queries.Add)
            .Returns<InternalItemsQuery>(q =>
            {
                if (q.ArtistIds.Length > 0 || q.PersonIds.Length > 0 || q.StudioIds.Length > 0 || q.GenreIds.Count > 0)
                {
                    var hits = probes.Where(p => q.ArtistIds.Contains(p.Id) || q.PersonIds.Contains(p.Id) || q.StudioIds.Contains(p.Id) || q.GenreIds.Contains(p.Id)).ToList();
                    return new QueryResult<BaseItem>(0, hits.Count, hits);
                }

                var start = q.StartIndex ?? 0;
                var page = list.Skip(start).Take(q.Limit ?? int.MaxValue).ToList();
                return new QueryResult<BaseItem>(start, list.Count, page);
            });
    }

    public static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    internal sealed class RootFolder : UserRootFolder
    {
        public IReadOnlyList<BaseItem> Libraries { get; set; } = [];

        public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query) => Libraries;
    }

    internal sealed class PlaylistsFolder : BasePluginFolder
    {
        public override CollectionType? CollectionType => Jellyfin.Data.Enums.CollectionType.playlists;
    }

    // Named "Movie" on purpose: BaseItem.GetBaseItemKind parses the runtime type name.
    internal sealed class Movie : MediaBrowser.Controller.Entities.Movies.Movie
    {
        public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => true;
    }

    internal sealed class MusicArtist : MediaBrowser.Controller.Entities.Audio.MusicArtist
    {
        public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => true;
    }

    internal sealed class BoxSet : MediaBrowser.Controller.Entities.Movies.BoxSet
    {
        public IReadOnlyList<BaseItem> Entries { get; init; } = [];

        public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => true;

        public override bool IsVisibleStandalone(User user) => true;

        public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query) => Entries;

        // Folder.GetItems ends here; the real one sorts through the static library manager. Paged like Jellyfin would.
        protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
            => new(query.StartIndex, Entries.Count, Entries.Skip(query.StartIndex ?? 0).Take(query.Limit ?? int.MaxValue).ToList());
    }

    internal sealed class Playlist : MediaBrowser.Controller.Playlists.Playlist
    {
        public IReadOnlyList<BaseItem> Entries { get; init; } = [];

        public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => true;

        public override bool IsVisibleStandalone(User user) => true;

        public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query) => Entries;
    }
}
