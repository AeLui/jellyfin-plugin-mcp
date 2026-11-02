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

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private SearchTool Search() => new(_users.Object, _settings.Object, _userData.Object, _library.Object);

    private BrowseTool Browse() => new(_users.Object, _settings.Object, _userData.Object, _library.Object, new ItemLookup(_library.Object));

    private GetItemTool GetItem() => new(_users.Object, _settings.Object, _userData.Object, new ItemLookup(_library.Object), _library.Object);

    [Fact]
    public async Task Search_PassesUserAndFiltersToQuery()
    {
        var queries = new List<InternalItemsQuery>();
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));
        var library = Guid.NewGuid();

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
        Assert.Equal(1999, q.MinPremiereDate!.Value.Year);
        Assert.Equal(2003, q.MaxPremiereDate!.Value.Year);
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
        InternalItemsQuery? captured = null;
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => captured = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        // GetItemById is not set up on the strict mock: using it would throw a MockException instead of "Item not found".
        var ex = await Assert.ThrowsAsync<ToolException>(
            () => GetItem().ExecuteAsync(Args($$"""{"itemId":"{{id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        Assert.Same(_user, captured!.User);
        Assert.Equal(new[] { id }, captured.ItemIds);
        _library.Verify(l => l.GetItemById(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task GetItem_Visible_ReturnsDetailWithPeople()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Heat", Overview = "Cops and robbers", Genres = ["Crime"] };
        _library.Setup(l => l.GetItemsResult(It.Is<InternalItemsQuery>(q => q.ItemIds.Contains(movie.Id))))
            .Returns(new QueryResult<BaseItem>(0, 1, new BaseItem[] { movie }));
        _library.Setup(l => l.GetPeople(movie)).Returns(new List<PersonInfo> { new() { Name = "Al Pacino", Role = "Hanna", Type = PersonKind.Actor } });

        var result = await GetItem().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal("Cops and robbers", doc.RootElement.GetProperty("overview").GetString());
        Assert.Equal("Al Pacino", doc.RootElement.GetProperty("people")[0].GetProperty("name").GetString());
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
    public async Task Browse_FavoritesFilter_SetsIsFavorite()
    {
        var parent = new Folder { Id = Guid.NewGuid() };
        var queries = new List<InternalItemsQuery>();
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns((InternalItemsQuery q) => new QueryResult<BaseItem>(0, 1, q.ItemIds.Length > 0 ? new BaseItem[] { parent } : Array.Empty<BaseItem>()));

        await Browse().ExecuteAsync(
            Args($$"""{"parentId":"{{parent.Id}}","filter":"favorites","sortBy":"community_rating","sortOrder":"desc"}"""),
            _principal,
            CancellationToken.None);

        var q = queries.Last();
        Assert.True(q.IsFavorite);
        Assert.Null(q.IsPlayed);
        Assert.False(q.Recursive);
        Assert.Equal(parent.Id, q.ParentId);
        Assert.Same(_user, q.User);
        Assert.Equal((ItemSortBy.CommunityRating, SortOrder.Descending), Assert.Single(q.OrderBy));
    }

    [Theory]
    [InlineData("unplayed", false)]
    [InlineData("played", true)]
    public async Task Browse_PlayedFilters_SetIsPlayed(string filter, bool expected)
    {
        var parent = new Folder { Id = Guid.NewGuid() };
        var queries = new List<InternalItemsQuery>();
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns(new QueryResult<BaseItem>(0, 1, new BaseItem[] { parent }));

        await Browse().ExecuteAsync(Args($$"""{"parentId":"{{parent.Id}}","filter":"{{filter}}"}"""), _principal, CancellationToken.None);

        Assert.Equal(expected, queries.Last().IsPlayed);
        Assert.Null(queries.Last().IsFavorite);
        Assert.Equal((ItemSortBy.SortName, SortOrder.Ascending), Assert.Single(queries.Last().OrderBy));
    }

    [Fact]
    public async Task Browse_ParentNotVisible_ReturnsNotFound()
    {
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        var ex = await Assert.ThrowsAsync<ToolException>(
            () => Browse().ExecuteAsync(Args($$"""{"parentId":"{{Guid.NewGuid()}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
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
