using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Library;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library;

public class CollectionToolsTests
{
    private readonly User _user = TestUsers.Create();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new(MockBehavior.Strict);
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<IUserViewManager> _views = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly Mock<ICollectionManager> _collections = new(MockBehavior.Strict);
    private readonly McpPrincipal _principal;

    public CollectionToolsTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(50);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Library });
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private ListCollectionsTool Tool() => new(_users.Object, _settings.Object, _userData.Object, new ItemLookup(_library.Object, _views.Object), _library.Object, _collections.Object);

    private static Fakes.BoxSet Collection(string name, bool visible = true, params BaseItem[] children)
        => new() { Id = Guid.NewGuid(), Name = name, Visible = visible, VisibleChildren = children };

    private List<InternalItemsQuery> QueryReturns(params BaseItem[] items)
    {
        var queries = new List<InternalItemsQuery>();
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(queries.Add)
            .Returns(new QueryResult<BaseItem>(0, items.Length, items));
        return queries;
    }

    private Movie VisibleMovie()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Night Signal" };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        return movie;
    }

    [Fact]
    public async Task ListCollections_QueriesUserBoxSets()
    {
        var queries = QueryReturns();

        await Tool().ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        var q = Assert.Single(queries);
        Assert.Same(_user, q.User);
        Assert.Equal(new[] { BaseItemKind.BoxSet }, q.IncludeItemTypes);
        Assert.True(q.Recursive);
        Assert.Equal((ItemSortBy.SortName, SortOrder.Ascending), Assert.Single(q.OrderBy));
    }

    [Fact]
    public async Task ListCollections_ReturnsChildCount()
    {
        QueryReturns(Collection("Trilogy", true, new Movie { Id = Guid.NewGuid() }, new Movie { Id = Guid.NewGuid() }));
        var tool = Tool();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Trilogy", item.GetProperty("name").GetString());
        Assert.Equal("box_set", item.GetProperty("type").GetString());
        Assert.Equal(2, item.GetProperty("childCount").GetInt32());
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task ListCollections_PagesVisibleOnly()
    {
        QueryReturns(Collection("A"), Collection("B", visible: false), Collection("C"));

        var result = await Tool().ExecuteAsync(Args("""{"limit":1,"startIndex":1}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("C", Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task ListCollections_ForItem_UsesCollectionManager()
    {
        var movie = VisibleMovie();
        _collections.Setup(c => c.GetCollectionsContainingItem(_user, movie.Id)).Returns(new BoxSet[] { Collection("Trilogy", true, movie) });
        var tool = Tool();

        var result = await tool.ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal("Trilogy", Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task ListCollections_ForItem_DropsInvisibleCollectionsAndSortsByName()
    {
        var movie = VisibleMovie();
        _collections.Setup(c => c.GetCollectionsContainingItem(_user, movie.Id))
            .Returns(new BoxSet[] { Collection("Zeta"), Collection("Hidden", visible: false), Collection("alpha") });

        var result = await Tool().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "alpha", "Zeta" }, doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ListCollections_ForItem_Pages()
    {
        var movie = VisibleMovie();
        _collections.Setup(c => c.GetCollectionsContainingItem(_user, movie.Id))
            .Returns(new BoxSet[] { Collection("A"), Collection("B"), Collection("C") });

        var result = await Tool().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","limit":1,"startIndex":2}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("C", Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task ListCollections_HiddenItem_IsNotFound()
    {
        var id = Guid.NewGuid();
        _library.Setup(l => l.GetItemById<BaseItem>(id, _user)).Returns((BaseItem?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Tool().ExecuteAsync(Args($$"""{"itemId":"{{id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _collections.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListCollections_Empty_Conforms()
    {
        QueryReturns();
        var tool = Tool();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Empty(doc.RootElement.GetProperty("items").EnumerateArray());
        SchemaAssert.Conforms(tool, result);
    }

    [Theory]
    [InlineData("""{"itemId":"x"}""", "'itemId'")]
    [InlineData("""{"limit":"x"}""", "'limit'")]
    public async Task ListCollections_MalformedArguments_NameTheArgument(string json, string expected)
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Tool().ExecuteAsync(Args(json), _principal, CancellationToken.None));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }
}
