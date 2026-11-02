using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library;

/// <summary>
/// Playlists and collections are not filtered per user by the item SQL; the tools must hide the ones the user cannot see.
/// </summary>
public class HiddenContainersTests
{
    private readonly User _user = TestUsers.Create();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new(MockBehavior.Strict);
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<IUserViewManager> _views = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly McpPrincipal _principal;
    private readonly List<InternalItemsQuery> _queries = [];

    public HiddenContainersTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(50);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Library });
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private ItemLookup Lookup() => new(_library.Object, _views.Object);

    private Fakes.Playlist Playlist(string name, Guid owner, bool open = false, params Guid[] sharedWith)
    {
        // Playlist.IsVisible depends on server paths; model the outcome of its owner/share/open-access rules.
        return new Fakes.Playlist
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerUserId = owner,
            OpenAccess = open,
            Shares = sharedWith.Select(u => new PlaylistUserPermissions(u, false)).ToList(),
            Visible = open || owner == _user.Id || sharedWith.Contains(_user.Id),
        };
    }

    private static Fakes.BoxSet BoxSet(string name, bool visible)
        => new() { Id = Guid.NewGuid(), Name = name, Visible = visible };

    private void Returns(int total, params BaseItem[] items)
        => _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_queries.Add)
            .Returns(() => new QueryResult<BaseItem>(0, total, items));

    private static List<string?> Names(ToolResult result)
        => JsonDocument.Parse(result.Text).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();

    private static int Total(ToolResult result) => JsonDocument.Parse(result.Text).RootElement.GetProperty("totalCount").GetInt32();

    private void MockPlaylists(out Fakes.Playlist mine, out Fakes.Playlist shared, out Fakes.Playlist other)
    {
        mine = Playlist("mine", _user.Id);
        shared = Playlist("shared", Guid.NewGuid(), false, _user.Id);
        other = Playlist("secret", Guid.NewGuid());
    }

    [Fact]
    public async Task Browse_PlaylistsFolder_ListsOnlyVisiblePlaylists_WithExactCountAndPaging()
    {
        MockPlaylists(out var mine, out var shared, out var other);
        var folder = new Mock<BasePluginFolder> { CallBase = true };
        folder.Object.Id = Guid.NewGuid();
        _library.Setup(l => l.GetItemById<BaseItem>(folder.Object.Id, _user)).Returns(folder.Object);
        Returns(3, other, mine, shared);
        var tool = new BrowseTool(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());

        var all = await tool.ExecuteAsync(Args($$"""{"parentId":"{{folder.Object.Id}}"}"""), _principal, CancellationToken.None);
        var page = await tool.ExecuteAsync(Args($$"""{"parentId":"{{folder.Object.Id}}","limit":1,"startIndex":1}"""), _principal, CancellationToken.None);

        Assert.Equal(new[] { "mine", "shared" }, Names(all));
        Assert.Equal(2, Total(all));
        Assert.Equal(new[] { "shared" }, Names(page));
        Assert.Equal(2, Total(page));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_PlaylistType_HidesOtherUsersPlaylists(bool withLibrary)
    {
        MockPlaylists(out var mine, out var shared, out var other);
        var library = Guid.NewGuid();
        _views.Setup(v => v.GetUserViews(It.IsAny<UserViewQuery>())).Returns(new Folder[] { new CollectionFolder { Id = library } });
        Returns(3, other, mine, shared);
        var tool = new SearchTool(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());
        var extra = withLibrary ? $$""","libraryId":"{{library}}" """ : string.Empty;

        var result = await tool.ExecuteAsync(Args($$"""{"query":"x","types":["playlist"]{{extra}}}"""), _principal, CancellationToken.None);

        Assert.Equal(new[] { "mine", "shared" }, Names(result));
        Assert.Equal(2, Total(result));
    }

    [Fact]
    public async Task Search_BoxSetType_HidesInvisibleCollections()
    {
        Returns(2, BoxSet("private", false), BoxSet("public", true));
        var tool = new SearchTool(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());

        var result = await tool.ExecuteAsync(Args("""{"query":"x","types":["box_set"]}"""), _principal, CancellationToken.None);

        Assert.Equal(new[] { "public" }, Names(result));
        Assert.Equal(1, Total(result));
    }

    [Fact]
    public async Task Search_WithoutTypes_ExcludesPlaylistsAndCollectionsInTheQuery_AndStillFiltersResults()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        Returns(5, movie, Playlist("secret", Guid.NewGuid()));
        var tool = new SearchTool(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());

        var result = await tool.ExecuteAsync(Args("""{"query":"x"}"""), _principal, CancellationToken.None);

        var q = Assert.Single(_queries);
        Assert.Contains(BaseItemKind.Playlist, q.ExcludeItemTypes);
        Assert.Contains(BaseItemKind.BoxSet, q.ExcludeItemTypes);
        Assert.Equal(new[] { "Movie" }, Names(result));
        Assert.Equal(4, Total(result));
    }

    [Fact]
    public async Task ListTools_NeverReturnHiddenPlaylists()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        Returns(2, movie, Playlist("secret", Guid.NewGuid()));
        var recent = new RecentlyAddedTool(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());
        var resume = new ContinueWatchingTool(_users.Object, _settings.Object, _userData.Object, _library.Object);
        var history = new WatchHistoryTool(_users.Object, _settings.Object, _userData.Object, _library.Object);

        foreach (var tool in new IMcpTool[] { recent, resume, history })
        {
            var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

            Assert.Equal(new[] { "Movie" }, Names(result));
            Assert.Equal(1, Total(result));
        }
    }

    [Fact]
    public async Task GetPerson_HidesInvisibleCollections()
    {
        var person = new Person { Id = Guid.NewGuid(), Name = "Al" };
        _library.Setup(l => l.GetItemById<BaseItem>(person.Id, _user)).Returns(person);
        Returns(2, BoxSet("private", false), new Movie { Id = Guid.NewGuid(), Name = "Heat" });
        var tool = new GetPersonTool(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());

        var result = await tool.ExecuteAsync(Args($$"""{"personId":"{{person.Id}}"}"""), _principal, CancellationToken.None);

        Assert.Equal(new[] { "Heat" }, Names(result));
        Assert.Equal(1, Total(result));
        Assert.Contains(BaseItemKind.BoxSet, Assert.Single(_queries).ExcludeItemTypes);
    }
}
