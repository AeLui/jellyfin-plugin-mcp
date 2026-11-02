using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Auth;
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
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library;

public class CatalogueToolsTests
{
    private readonly User _user = TestUsers.Create();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new(MockBehavior.Strict);
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<IUserViewManager> _views = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly McpPrincipal _principal;

    public CatalogueToolsTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(50);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Library });
    }

    private void ViewsContain(params Guid[] ids)
        => _views.Setup(v => v.GetUserViews(It.IsAny<UserViewQuery>()))
            .Returns(ids.Select(i => (Folder)new CollectionFolder { Id = i }).ToArray());

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private ItemLookup Lookup() => new(_library.Object, _views.Object);

    private SearchTool Search() => new(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());

    private BrowseTool Browse() => new(_users.Object, _settings.Object, _userData.Object, Lookup());

    private GetItemTool GetItem() => new(_users.Object, _settings.Object, _userData.Object, Lookup(), _library.Object);

    [Fact]
    public async Task Search_PassesUserAndFiltersToQuery()
    {
        var queries = new List<InternalItemsQuery>();
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));
        var library = Guid.NewGuid();
        ViewsContain(library);

        var result = await Search().ExecuteAsync(
            Args($$"""{"query":"matrix","types":["movie","music_album"],"genres":["Action"],"yearFrom":1999,"yearTo":2003,"libraryId":"{{library}}","limit":10000,"startIndex":5}"""),
            _principal,
            CancellationToken.None);

        Assert.False(result.IsError);
        var q = Assert.Single(queries);
        Assert.Same(_user, q.User);
        Assert.Equal("matrix", q.SearchTerm);
        Assert.Equal(new[] { BaseItemKind.Movie, BaseItemKind.MusicAlbum }, q.IncludeItemTypes);
        Assert.Equal(new[] { "Action" }, q.Genres);
        Assert.Equal(new[] { 1999, 2000, 2001, 2002, 2003 }, q.Years);
        Assert.Null(q.MinPremiereDate);
        Assert.Null(q.MaxPremiereDate);
        Assert.Equal(library, q.ParentId);
        Assert.True(q.Recursive);
        Assert.Equal(50, q.Limit);
        Assert.Equal(5, q.StartIndex);
    }

    [Fact]
    public async Task Search_ReturnsTotalCountAndSummaries()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "The Matrix", ProductionYear = 1999 };
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(0, 42, new BaseItem[] { movie }));

        var result = await Search().ExecuteAsync(Args("""{"query":"matrix"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(42, doc.RootElement.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("The Matrix", item.GetProperty("name").GetString());
        Assert.Equal("movie", item.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Search_MissingQuery_IsError()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Search().ExecuteAsync(Args("{}"), _principal, CancellationToken.None));

        Assert.Contains("'query'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"query":"x","types":["banana"]}""", "'types'")]
    [InlineData("""{"query":"x","types":"movie"}""", "'types'")]
    [InlineData("""{"query":5}""", "'query'")]
    [InlineData("""{"query":"x","yearFrom":0}""", "'yearFrom'")]
    [InlineData("""{"query":"x","libraryId":"nope"}""", "'libraryId'")]
    public async Task Search_MalformedArguments_NameTheArgument(string json, string expected)
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Search().ExecuteAsync(Args(json), _principal, CancellationToken.None));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_UnknownType_IsErrorNamingArgument()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(
            () => Search().ExecuteAsync(Args("""{"query":"x","types":["banana"]}"""), _principal, CancellationToken.None));

        Assert.Contains("'types'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("music_album", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"query":"x","limit":0}""", 1)]
    [InlineData("""{"query":"x","limit":-4}""", 1)]
    [InlineData("""{"query":"x","limit":10000}""", 50)]
    [InlineData("""{"query":"x"}""", 20)]
    public async Task Search_ClampsLimit(string json, int expected)
    {
        InternalItemsQuery? captured = null;
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => captured = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        await Search().ExecuteAsync(Args(json), _principal, CancellationToken.None);

        Assert.Equal(expected, captured!.Limit);
    }

    [Fact]
    public async Task GetItem_NotVisibleToUser_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        // The user-aware overload says no, although the unrestricted lookup would find the item.
        _library.Setup(l => l.GetItemById<BaseItem>(id, _user)).Returns((BaseItem?)null);
        _library.Setup(l => l.GetItemById(id)).Returns(new Movie { Id = id });

        var ex = await Assert.ThrowsAsync<ToolException>(
            () => GetItem().ExecuteAsync(Args($$"""{"itemId":"{{id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemById(It.IsAny<Guid>()), Times.Never);
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task GetItem_Visible_ReturnsDetailWithPeople()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Heat", Overview = "Cops and robbers", Genres = ["Crime"] };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        var pacino = new Person { Id = Guid.NewGuid(), Name = "Al Pacino" };
        var hidden = new Fakes.HiddenPerson { Id = Guid.NewGuid(), Name = "Hidden" };
        _library.Setup(l => l.GetPeople(movie)).Returns(new List<PersonInfo>
        {
            // PersonInfo.Id is a credits-table row id, not the Person item id.
            new() { Name = "Al Pacino", Role = "Hanna", Type = PersonKind.Actor, Id = Guid.NewGuid() },
            new() { Name = "Hidden", Type = PersonKind.Actor, Id = Guid.NewGuid() },
            new() { Name = "Unknown", Type = PersonKind.Actor, Id = Guid.NewGuid() },
        });
        _library.Setup(l => l.GetPerson("Al Pacino")).Returns(pacino);
        _library.Setup(l => l.GetPerson("Hidden")).Returns(hidden);
        _library.Setup(l => l.GetPerson("Unknown")).Returns((Person?)null);

        var result = await GetItem().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal("Cops and robbers", doc.RootElement.GetProperty("overview").GetString());
        var people = doc.RootElement.GetProperty("people");
        Assert.Equal("Al Pacino", people[0].GetProperty("name").GetString());
        Assert.Equal(pacino.Id.ToString("N"), people[0].GetProperty("id").GetString());
        Assert.False(people[1].TryGetProperty("id", out _));
        Assert.False(people[2].TryGetProperty("id", out _));
    }

    [Fact]
    public async Task GetItem_MissingOrBadId_IsError()
    {
        var missing = await Assert.ThrowsAsync<ToolException>(() => GetItem().ExecuteAsync(Args("{}"), _principal, CancellationToken.None));
        var bad = await Assert.ThrowsAsync<ToolException>(() => GetItem().ExecuteAsync(Args("""{"itemId":"x"}"""), _principal, CancellationToken.None));

        Assert.Contains("'itemId'", missing.Message, StringComparison.Ordinal);
        Assert.Contains("'itemId'", bad.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browse_FavoritesFilter_CallsFolderGetItemsWithUserPagingAndSort()
    {
        var parent = new Fakes.RecordingFolder { Id = Guid.NewGuid(), Result = new QueryResult<BaseItem>(5, 12, new BaseItem[] { new Movie { Id = Guid.NewGuid(), Name = "Heat" } }) };
        _library.Setup(l => l.GetItemById<BaseItem>(parent.Id, _user)).Returns(parent);

        var result = await Browse().ExecuteAsync(
            Args($$"""{"parentId":"{{parent.Id}}","filter":"favorites","sortBy":"community_rating","sortOrder":"desc","limit":7,"startIndex":5}"""),
            _principal,
            CancellationToken.None);

        var q = Assert.Single(parent.Queries);
        Assert.True(q.IsFavorite);
        Assert.Null(q.IsPlayed);
        Assert.False(q.Recursive);
        Assert.Equal(parent.Id, q.ParentId);
        Assert.Empty(q.IncludeItemTypes);
        Assert.Same(_user, q.User);
        Assert.Equal(7, q.Limit);
        Assert.Equal(5, q.StartIndex);
        Assert.True(q.EnableTotalRecordCount);
        Assert.Equal((ItemSortBy.CommunityRating, SortOrder.Descending), Assert.Single(q.OrderBy));
        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(12, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Heat", doc.RootElement.GetProperty("items")[0].GetProperty("name").GetString());

        // Strict mock: ILibraryManager.GetItemsResult (a ParentId query) must never be used for browsing.
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Theory]
    [InlineData("unplayed", false)]
    [InlineData("played", true)]
    public async Task Browse_PlayedFilters_SetIsPlayed(string filter, bool expected)
    {
        var parent = new Fakes.RecordingFolder { Id = Guid.NewGuid() };
        _library.Setup(l => l.GetItemById<BaseItem>(parent.Id, _user)).Returns(parent);

        await Browse().ExecuteAsync(Args($$"""{"parentId":"{{parent.Id}}","filter":"{{filter}}"}"""), _principal, CancellationToken.None);

        var q = Assert.Single(parent.Queries);
        Assert.Equal(expected, q.IsPlayed);
        Assert.Null(q.IsFavorite);
        Assert.Equal((ItemSortBy.SortName, SortOrder.Ascending), Assert.Single(q.OrderBy));
    }

    [Theory]
    [InlineData(CollectionType.boxsets, BaseItemKind.BoxSet)]
    [InlineData(CollectionType.playlists, BaseItemKind.Playlist)]
    public async Task Browse_BoxSetAndPlaylistLibraries_QueryTheirItemTypes(CollectionType type, BaseItemKind kind)
    {
        var parent = new Fakes.RecordingCollectionFolder { Id = Guid.NewGuid(), CollectionType = type };
        _library.Setup(l => l.GetItemById<BaseItem>(parent.Id, _user)).Returns(parent);

        await Browse().ExecuteAsync(Args($$"""{"parentId":"{{parent.Id}}"}"""), _principal, CancellationToken.None);

        var q = Assert.Single(parent.Queries);
        Assert.Equal(new[] { kind }, q.IncludeItemTypes);
        Assert.True(q.Recursive);
    }

    [Fact]
    public async Task Browse_MovieLibrary_ListsDirectChildren()
    {
        var parent = new Fakes.RecordingCollectionFolder { Id = Guid.NewGuid(), CollectionType = CollectionType.movies };
        _library.Setup(l => l.GetItemById<BaseItem>(parent.Id, _user)).Returns(parent);

        await Browse().ExecuteAsync(Args($$"""{"parentId":"{{parent.Id}}"}"""), _principal, CancellationToken.None);

        var q = Assert.Single(parent.Queries);
        Assert.Empty(q.IncludeItemTypes);
        Assert.False(q.Recursive);
    }

    [Fact]
    public async Task Browse_ParentNotVisible_ReturnsNotFoundWithoutQuerying()
    {
        var parent = Guid.NewGuid();
        _library.Setup(l => l.GetItemById<BaseItem>(parent, _user)).Returns((BaseItem?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(
            () => Browse().ExecuteAsync(Args($$"""{"parentId":"{{parent}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task Browse_ParentIsNotAFolder_ReturnsNotFound()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);

        var ex = await Assert.ThrowsAsync<ToolException>(
            () => Browse().ExecuteAsync(Args($$"""{"parentId":"{{movie.Id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
    }

    [Fact]
    public async Task Search_HiddenLibraryId_ReturnsNotFoundWithoutQuerying()
    {
        var library = Guid.NewGuid();
        ViewsContain(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ToolException>(
            () => Search().ExecuteAsync(Args($$"""{"query":"x","libraryId":"{{library}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task Search_RootFolderIdAsLibrary_ReturnsNotFoundWithoutQuerying()
    {
        // Root folders are visible to every user but are not in the user's views.
        var root = new UserRootFolder { Id = Guid.NewGuid() };
        var aggregate = new AggregateFolder { Id = Guid.NewGuid() };
        ViewsContain(Guid.NewGuid());
        _library.Setup(l => l.GetItemById<BaseItem>(root.Id, _user)).Returns(root);
        _library.Setup(l => l.GetItemById<BaseItem>(aggregate.Id, _user)).Returns(aggregate);

        foreach (var id in new[] { root.Id, aggregate.Id })
        {
            var ex = await Assert.ThrowsAsync<ToolException>(
                () => Search().ExecuteAsync(Args($$"""{"query":"x","libraryId":"{{id}}"}"""), _principal, CancellationToken.None));
            Assert.Equal("Item not found", ex.Message);
        }

        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Browse_RootFolderIdAsParent_ReturnsNotFoundWithoutQuerying(bool userRoot)
    {
        BaseItem root = userRoot ? new UserRootFolder { Id = Guid.NewGuid() } : new AggregateFolder { Id = Guid.NewGuid() };
        _library.Setup(l => l.GetItemById<BaseItem>(root.Id, _user)).Returns(root);

        var ex = await Assert.ThrowsAsync<ToolException>(
            () => Browse().ExecuteAsync(Args($$"""{"parentId":"{{root.Id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task Search_YearFromOnly_RunsToCurrentYear()
    {
        InternalItemsQuery? captured = null;
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => captured = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));
        var now = DateTime.UtcNow.Year;

        await Search().ExecuteAsync(Args($$"""{"query":"x","yearFrom":{{now - 2}}}"""), _principal, CancellationToken.None);

        Assert.Equal(new[] { now - 2, now - 1, now }, captured!.Years);
    }

    [Fact]
    public async Task Search_YearToOnly_StartsAtFloor()
    {
        InternalItemsQuery? captured = null;
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => captured = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        await Search().ExecuteAsync(Args("""{"query":"x","yearTo":1890}"""), _principal, CancellationToken.None);

        Assert.Equal(1880, captured!.Years[0]);
        Assert.Equal(1890, captured.Years[^1]);
        Assert.Equal(11, captured.Years.Length);
    }

    [Fact]
    public async Task Search_YearFromAfterYearTo_IsErrorNamingArgument()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(
            () => Search().ExecuteAsync(Args("""{"query":"x","yearFrom":2010,"yearTo":2000}"""), _principal, CancellationToken.None));

        Assert.Contains("'yearFrom'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_PersonType_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(
            () => Search().ExecuteAsync(Args("""{"query":"x","types":["person"]}"""), _principal, CancellationToken.None));

        Assert.Contains("'types'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{}""", "'parentId'")]
    [InlineData("""{"parentId":"11111111111111111111111111111111","sortBy":"bogus"}""", "'sortBy'")]
    [InlineData("""{"parentId":"11111111111111111111111111111111","filter":"bogus"}""", "'filter'")]
    [InlineData("""{"parentId":"11111111111111111111111111111111","sortOrder":3}""", "'sortOrder'")]
    public async Task Browse_MalformedArguments_NameTheArgument(string json, string expected)
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Browse().ExecuteAsync(Args(json), _principal, CancellationToken.None));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListLibraries_ReturnsOnlyUserViews()
    {
        var movies = new CollectionFolder { Id = Guid.NewGuid(), Name = "Movies" };
        UserViewQuery? captured = null;
        _views.Setup(v => v.GetUserViews(It.IsAny<UserViewQuery>()))
            .Callback<UserViewQuery>(q => captured = q)
            .Returns(new Folder[] { movies });
        var tool = new ListLibrariesTool(_users.Object, _settings.Object, _userData.Object, _views.Object);

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        Assert.Same(_user, captured!.User);
        using var doc = JsonDocument.Parse(result.Text);
        var library = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(movies.Id.ToString("N"), library.GetProperty("id").GetString());
        Assert.Equal("Movies", library.GetProperty("name").GetString());
        _library.VerifyNoOtherCalls();
    }

    [Fact]
    public void Tools_DeclareLibraryScopeAndReadOnlyAnnotations()
    {
        IMcpTool[] tools =
        [
            Search(),
            Browse(),
            GetItem(),
            new ListLibrariesTool(_users.Object, _settings.Object, _userData.Object, _views.Object),
        ];

        Assert.Equal(
            new[] { "jellyfin_browse", "jellyfin_get_item", "jellyfin_list_libraries", "jellyfin_search" },
            tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(tools, t =>
        {
            Assert.Equal(McpScopes.Library, t.RequiredScope);
            Assert.Equal(new ToolAnnotations(true, false, true), t.Annotations);
        });
    }
}
