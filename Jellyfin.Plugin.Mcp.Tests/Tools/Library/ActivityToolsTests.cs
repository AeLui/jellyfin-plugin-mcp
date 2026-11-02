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
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library;

public class ActivityToolsTests
{
    private readonly User _user = TestUsers.Create();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new(MockBehavior.Strict);
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<IUserViewManager> _views = new();
    private readonly Mock<ITVSeriesManager> _tv = new(MockBehavior.Strict);
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly McpPrincipal _principal;
    private readonly List<InternalItemsQuery> _queries = [];

    public ActivityToolsTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(50);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Library });
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_queries.Add)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private async Task<ToolResult> Run(IMcpTool tool, string json)
        => await tool.ExecuteAsync(Args(json), _principal, CancellationToken.None);

    private ItemLookup Lookup() => new(_library.Object, _views.Object);

    private RecentlyAddedTool Recent() => new(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());

    private ContinueWatchingTool Continue() => new(_users.Object, _settings.Object, _userData.Object, _library.Object);

    private NextUpTool NextUp() => new(_users.Object, _settings.Object, _userData.Object, _tv.Object, Lookup());

    private WatchHistoryTool History() => new(_users.Object, _settings.Object, _userData.Object, _library.Object);

    private GetPersonTool Person() => new(_users.Object, _settings.Object, _userData.Object, _library.Object, Lookup());

    [Fact]
    public async Task ContinueWatching_QueriesResumableForUser()
    {
        await Run(Continue(), """{"limit":10000}""");

        var q = Assert.Single(_queries);
        Assert.Same(_user, q.User);
        Assert.True(q.IsResumable);
        Assert.True(q.Recursive);
        Assert.Equal(50, q.Limit);
        Assert.Equal(Guid.Empty, q.ParentId);
        Assert.Empty(q.ItemIds);
        Assert.Equal(new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Video }, q.IncludeItemTypes);
        Assert.Equal((ItemSortBy.DatePlayed, SortOrder.Descending), Assert.Single(q.OrderBy));
    }

    [Fact]
    public async Task NextUp_PassesSeriesIdAndUser()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Show" };
        var episode = new Episode { Id = Guid.NewGuid(), Name = "Pilot", ParentIndexNumber = 1, IndexNumber = 1 };
        NextUpQuery? captured = null;
        _library.Setup(l => l.GetItemById<BaseItem>(series.Id, _user)).Returns(series);
        _tv.Setup(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, DtoOptions>((q, _) => captured = q)
            .Returns(new QueryResult<BaseItem>(0, 1, new BaseItem[] { episode }));

        var result = await Run(NextUp(), $$"""{"seriesId":"{{series.Id}}","limit":5,"startIndex":2}""");

        Assert.Same(_user, captured!.User);
        Assert.Equal(series.Id, captured.SeriesId);
        Assert.Equal(5, captured.Limit);
        Assert.Equal(2, captured.StartIndex);
        Assert.Null(captured.ParentId);
        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Pilot", doc.RootElement.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task NextUp_WithoutSeries_QueriesAllUserLibraries()
    {
        NextUpQuery? captured = null;
        _tv.Setup(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, DtoOptions>((q, _) => captured = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        await Run(NextUp(), "{}");

        Assert.Same(_user, captured!.User);
        Assert.Null(captured.SeriesId);
        Assert.Null(captured.ParentId);
        Assert.Equal(20, captured.Limit);
    }

    [Fact]
    public async Task NextUp_HiddenSeries_ReturnsNotFoundWithoutQuerying()
    {
        var id = Guid.NewGuid();
        _library.Setup(l => l.GetItemById<BaseItem>(id, _user)).Returns((BaseItem?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(NextUp(), $$"""{"seriesId":"{{id}}"}"""));

        Assert.Equal("Item not found", ex.Message);
        _tv.Verify(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()), Times.Never);
    }

    [Fact]
    public async Task NextUp_NonSeriesId_IsErrorWithoutQuerying()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(NextUp(), $$"""{"seriesId":"{{movie.Id}}"}"""));

        Assert.Contains("'seriesId'", ex.Message, StringComparison.Ordinal);
        _tv.Verify(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()), Times.Never);
    }

    [Fact]
    public async Task WatchHistory_OrdersByDatePlayedDesc()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Heat" };
        var played = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_queries.Add)
            .Returns(new QueryResult<BaseItem>(0, 7, new BaseItem[] { movie }));
        _userData.Setup(d => d.GetUserData(_user, movie)).Returns(new UserItemData { Key = "k", Played = true, LastPlayedDate = played });

        var result = await Run(History(), """{"limit":3,"startIndex":4}""");

        var q = _queries.Last();
        Assert.Same(_user, q.User);
        Assert.True(q.IsPlayed);
        Assert.True(q.Recursive);
        Assert.Equal(3, q.Limit);
        Assert.Equal(4, q.StartIndex);
        Assert.Equal((ItemSortBy.DatePlayed, SortOrder.Descending), Assert.Single(q.OrderBy));
        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(7, doc.RootElement.GetProperty("totalCount").GetInt32());
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.Equal("2026-03-04T05:06:07.0000000Z", item.GetProperty("lastPlayedDate").GetString());
    }

    [Fact]
    public async Task RecentlyAdded_ScopedToLibrary()
    {
        var library = new CollectionFolder { Id = Guid.NewGuid() };
        _views.Setup(v => v.GetUserViews(It.IsAny<UserViewQuery>())).Returns(new Folder[] { library });

        await Run(Recent(), $$"""{"libraryId":"{{library.Id}}","types":["movie","episode"],"limit":7}""");

        var q = Assert.Single(_queries);
        Assert.Same(_user, q.User);
        Assert.Equal(library.Id, q.ParentId);
        Assert.True(q.Recursive);
        Assert.Equal(7, q.Limit);
        Assert.Equal(new[] { BaseItemKind.Movie, BaseItemKind.Episode }, q.IncludeItemTypes);
        Assert.False(q.IsFolder);
        Assert.Equal((ItemSortBy.DateCreated, SortOrder.Descending), Assert.Single(q.OrderBy));
    }

    [Fact]
    public async Task RecentlyAdded_WithoutLibrary_LeavesScopingToUserAccess()
    {
        await Run(Recent(), "{}");

        var q = Assert.Single(_queries);
        Assert.Equal(Guid.Empty, q.ParentId);
        Assert.Empty(q.ItemIds);
        Assert.Same(_user, q.User);
        Assert.Equal(20, q.Limit);
    }

    [Fact]
    public async Task RecentlyAdded_HiddenLibrary_ReturnsNotFoundWithoutQuerying()
    {
        var id = Guid.NewGuid();
        _views.Setup(v => v.GetUserViews(It.IsAny<UserViewQuery>())).Returns(new Folder[] { new CollectionFolder { Id = Guid.NewGuid() } });

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(Recent(), $$"""{"libraryId":"{{id}}"}"""));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task RecentlyAdded_RootFolderIdAsLibrary_ReturnsNotFoundWithoutQuerying()
    {
        var root = new UserRootFolder { Id = Guid.NewGuid() };
        var aggregate = new AggregateFolder { Id = Guid.NewGuid() };
        _views.Setup(v => v.GetUserViews(It.IsAny<UserViewQuery>())).Returns(new Folder[] { new CollectionFolder { Id = Guid.NewGuid() } });
        _library.Setup(l => l.GetItemById<BaseItem>(root.Id, _user)).Returns(root);
        _library.Setup(l => l.GetItemById<BaseItem>(aggregate.Id, _user)).Returns(aggregate);

        foreach (var id in new[] { root.Id, aggregate.Id })
        {
            var ex = await Assert.ThrowsAsync<ToolException>(() => Run(Recent(), $$"""{"libraryId":"{{id}}"}"""));
            Assert.Equal("Item not found", ex.Message);
        }

        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task RecentlyAdded_UnknownType_NamesArgument()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(Recent(), """{"types":["banana"]}"""));

        Assert.Contains("'types'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPerson_ItemsAreUserScoped()
    {
        var person = new Person { Id = Guid.NewGuid(), Name = "Al Pacino", Overview = "Actor", PremiereDate = new DateTime(1940, 4, 25, 0, 0, 0, DateTimeKind.Utc) };
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Heat" };
        _library.Setup(l => l.GetItemById<BaseItem>(person.Id, _user)).Returns(person);
        _library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_queries.Add)
            .Returns(new QueryResult<BaseItem>(0, 1, new BaseItem[] { movie }));

        var result = await Run(Person(), $$"""{"personId":"{{person.Id}}"}""");

        var q = Assert.Single(_queries);
        Assert.Same(_user, q.User);
        Assert.Equal(new[] { person.Id }, q.PersonIds);
        Assert.Equal((ItemSortBy.SortName, SortOrder.Ascending), Assert.Single(q.OrderBy));
        Assert.True(q.Recursive);
        Assert.Equal(Guid.Empty, q.ParentId);
        Assert.Empty(q.ItemIds);
        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal("Al Pacino", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("Actor", doc.RootElement.GetProperty("overview").GetString());
        Assert.Equal("1940-04-25T00:00:00.0000000Z", doc.RootElement.GetProperty("birthDate").GetString());
        Assert.Equal("Heat", doc.RootElement.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GetPerson_UnspecifiedBirthDate_KeepsCalendarDate()
    {
        var person = new Person { Id = Guid.NewGuid(), Name = "Al Pacino", PremiereDate = new DateTime(1940, 4, 25, 0, 0, 0, DateTimeKind.Unspecified) };
        _library.Setup(l => l.GetItemById<BaseItem>(person.Id, _user)).Returns(person);

        var result = await Run(Person(), $$"""{"personId":"{{person.Id}}"}""");

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal("1940-04-25T00:00:00.0000000Z", doc.RootElement.GetProperty("birthDate").GetString());
    }

    [Fact]
    public async Task GetPerson_HiddenId_ReturnsNotFoundWithoutQuerying()
    {
        var id = Guid.NewGuid();
        _library.Setup(l => l.GetItemById<BaseItem>(id, _user)).Returns((BaseItem?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(Person(), $$"""{"personId":"{{id}}"}"""));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task GetPerson_IdOfNonPerson_ReturnsNotFound()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(Person(), $$"""{"personId":"{{movie.Id}}"}"""));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task GetPerson_ByName_ResolvesThenChecksVisibility()
    {
        var person = new Person { Id = Guid.NewGuid(), Name = "Al Pacino" };
        _library.Setup(l => l.GetPerson("Al Pacino")).Returns(person);
        _library.Setup(l => l.GetItemById<BaseItem>(person.Id, _user)).Returns((BaseItem?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(Person(), """{"name":"Al Pacino"}"""));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task GetPerson_UnknownName_ReturnsNotFound()
    {
        _library.Setup(l => l.GetPerson("Nobody")).Returns((Person?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(Person(), """{"name":"Nobody"}"""));

        Assert.Equal("Item not found", ex.Message);
    }

    [Fact]
    public async Task GetPerson_NeitherIdNorName_IsError()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(Person(), "{}"));

        Assert.Contains("'personId'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'name'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tools_DeclareLibraryScopeAndReadOnlyAnnotations()
    {
        IMcpTool[] tools = [Recent(), Continue(), NextUp(), History(), Person()];

        Assert.Equal(
            new[] { "jellyfin_continue_watching", "jellyfin_get_person", "jellyfin_next_up", "jellyfin_recently_added", "jellyfin_watch_history" },
            tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(tools, t =>
        {
            Assert.Equal(McpScopes.Library, t.RequiredScope);
            Assert.Equal(new ToolAnnotations(true, false, true), t.Annotations);
        });
    }
}
