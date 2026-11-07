using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Admin;
using Jellyfin.Plugin.Mcp.Tools.Library;
using Jellyfin.Plugin.Mcp.Tools.Manage;
using Jellyfin.Plugin.Mcp.Tools.Playback;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools;

/// <summary>
/// Every tool with a token limited to the Movies library: items of the Kids library look like missing ones, and lists
/// leave them out with an exact count.
/// </summary>
public class RestrictedToolsTests
{
    private readonly RestrictedWorld _w = new();
    private readonly Mock<ISimilarItemsManager> _similar = new();
    private readonly Mock<ITVSeriesManager> _tv = new();
    private readonly Mock<ICollectionManager> _collections = new();
    private readonly Mock<IPlaylistManager> _playlists = new();
    private readonly Mock<ISessionManager> _sessions = new();
    private readonly Mock<IMediaSourceManager> _mediaSources = new();
    private readonly List<SessionInfo> _active = [];

    public RestrictedToolsTests()
    {
        _sessions.SetupGet(s => s.Sessions).Returns(() => _active);
        _w.Library.Setup(l => l.GetPeople(It.IsAny<BaseItem>())).Returns([]);
        _w.Library.Setup(l => l.GetLibraryOptions(It.IsAny<BaseItem>())).Returns(new LibraryOptions());
    }

    private ItemLookup Lookup => _w.Lookup();

    private McpPrincipal Principal => _w.Principal;

    private static ToolArguments Args(string json) => RestrictedWorld.Args(json);

    private static JsonElement Json(ToolResult result) => JsonDocument.Parse(result.Text).RootElement;

    private static List<string> Names(ToolResult result)
        => Json(result).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();

    private static int Total(ToolResult result) => Json(result).GetProperty("totalCount").GetInt32();

    private async Task AssertNotFound(IMcpTool tool, string json)
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => tool.ExecuteAsync(Args(json), Principal, CancellationToken.None));
        Assert.Equal("Item not found", ex.Message);
    }

    private void AssertNarrowed(Guid[] topParentIds)
    {
        Assert.Contains(_w.MoviesPhysical, topParentIds);
        Assert.DoesNotContain(_w.KidsPhysical, topParentIds);
    }

    private Task<ToolResult> Run(IMcpTool tool, string json) => tool.ExecuteAsync(Args(json), Principal, CancellationToken.None);

    private RestrictedWorld.Movie AllowedMovie(string name)
        => _w.Add(new RestrictedWorld.Movie { Id = Guid.NewGuid(), Name = name }, _w.Movies);

    private RestrictedWorld.Movie HiddenMovie(string name)
        => _w.Add(new RestrictedWorld.Movie { Id = Guid.NewGuid(), Name = name }, _w.Kids);

    private SessionInfo AddSession(BaseItem? playing)
    {
        var controller = new Mock<ISessionController>();
        controller.SetupGet(c => c.SupportsMediaControl).Returns(true);
        controller.SetupGet(c => c.IsSessionActive).Returns(true);
        var session = new SessionInfo(_sessions.Object, NullLogger.Instance)
        {
            Id = "mine",
            UserId = _w.User.Id,
            UserName = "tester",
            DeviceName = "TV",
            Client = "client",
            Capabilities = new ClientCapabilities { SupportsMediaControl = true },
            SessionControllers = [controller.Object],
            PlayState = new PlayerStateInfo { IsPaused = true, PositionTicks = 10 * TimeSpan.TicksPerMinute },
            FullNowPlayingItem = playing,
            NowPlayingItem = playing is null ? null : new BaseItemDto { Id = playing.Id, Name = playing.Name },
        };
        _active.Add(session);
        return session;
    }

    private SessionResolver Sessions() => new(_sessions.Object, _w.Users.Object);

    [Fact]
    public async Task GetItem_HiddenLibraryItem_LooksMissing()
    {
        var tool = new GetItemTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, Lookup, _w.Library.Object);

        await AssertNotFound(tool, $$"""{"itemId":"{{_w.HiddenMovie.Id}}"}""");
        await AssertNotFound(tool, $$"""{"itemId":"{{Guid.NewGuid()}}"}""");
        Assert.Equal("Allowed Movie", Json(await Run(tool, $$"""{"itemId":"{{_w.AllowedMovie.Id}}"}""")).GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetMediaTracks_HiddenLibraryItem_LooksMissing()
    {
        var tool = new GetMediaTracksTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, Lookup, _mediaSources.Object);

        await AssertNotFound(tool, $$"""{"itemId":"{{_w.HiddenMovie.Id}}"}""");
        _mediaSources.Verify(m => m.GetMediaStreams(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ListLibraries_OnlyAllowedMediaLibraries()
    {
        var tool = new ListLibrariesTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Views.Object, Lookup);

        var items = Json(await Run(tool, "{}")).GetProperty("items").EnumerateArray().ToList();

        var only = Assert.Single(items);
        Assert.Equal(_w.Movies.Id.ToString("N"), only.GetProperty("id").GetString());
        Assert.Equal("movies", only.GetProperty("collectionType").GetString());
        _w.Views.Verify(v => v.GetUserViews(It.IsAny<UserViewQuery>()), Times.Never);
    }

    [Fact]
    public async Task Browse_HiddenLibrary_LooksMissing()
    {
        var tool = new BrowseTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, Lookup);

        await AssertNotFound(tool, $$"""{"parentId":"{{_w.Kids.Id}}"}""");
        await AssertNotFound(tool, $$"""{"parentId":"{{_w.Collections.Id}}"}""");
    }

    [Fact]
    public async Task Browse_Collection_ListsOnlyAllowedEntriesWithExactCount()
    {
        var second = AllowedMovie("Second");
        var set = _w.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Name = "Set", Entries = [_w.HiddenMovie, _w.AllowedMovie, HiddenMovie("Other"), second] }, _w.Collections);
        var tool = new BrowseTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, Lookup);

        var result = await Run(tool, $$"""{"parentId":"{{set.Id}}","limit":1,"startIndex":1}""");

        Assert.Equal(2, Total(result));
        Assert.Equal(new[] { "Second" }, Names(result));
    }

    [Fact]
    public async Task Search_HiddenLibraryId_LooksMissing_AndQueryNarrowed()
    {
        _w.QueryReturns([_w.AllowedMovie]);
        var tool = new SearchTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Library.Object, Lookup);

        await AssertNotFound(tool, $$"""{"query":"a","libraryId":"{{_w.Kids.Id}}"}""");
        var result = await Run(tool, """{"query":"movie"}""");

        Assert.Equal(1, Total(result));
        Assert.Equal(new[] { "Allowed Movie" }, Names(result));
        var query = Assert.Single(_w.Queries);
        AssertNarrowed(query.TopParentIds);
    }

    [Fact]
    public async Task Search_AllowedLibraryId_ScopedToThatLibrary()
    {
        _w.QueryReturns([_w.AllowedMovie]);
        var tool = new SearchTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Library.Object, Lookup);

        await Run(tool, $$"""{"query":"movie","libraryId":"{{_w.Movies.Id}}"}""");

        var query = Assert.Single(_w.Queries);
        Assert.Equal(_w.Movies.Id, query.ParentId);
    }

    [Fact]
    public async Task Search_Artists_OnlyThoseWithAllowedItems()
    {
        var shared = _w.Add(new RestrictedWorld.MusicArtist { Id = Guid.NewGuid(), Name = "Shared" });
        var kidsOnly = _w.Add(new RestrictedWorld.MusicArtist { Id = Guid.NewGuid(), Name = "Kids Only" });
        _w.QueryReturns([kidsOnly, shared], byName: [shared]);
        var tool = new SearchTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Library.Object, Lookup);

        var result = await Run(tool, """{"query":"s","types":["music_artist"]}""");

        Assert.Equal(1, Total(result));
        Assert.Equal(new[] { "Shared" }, Names(result));
    }

    [Fact]
    public async Task RecentlyAdded_HiddenLibraryId_LooksMissing_AndQueryNarrowed()
    {
        _w.QueryReturns([_w.AllowedMovie]);
        var tool = new RecentlyAddedTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Library.Object, Lookup);

        await AssertNotFound(tool, $$"""{"libraryId":"{{_w.Kids.Id}}"}""");
        var result = await Run(tool, "{}");

        Assert.Equal(1, Total(result));
        AssertNarrowed(Assert.Single(_w.Queries).TopParentIds);
    }

    [Fact]
    public async Task ContinueWatching_QueryNarrowedToAllowedLibraries()
    {
        _w.QueryReturns([_w.AllowedMovie]);
        var tool = new ContinueWatchingTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Library.Object);

        var result = await Run(tool, "{}");

        Assert.Equal(1, Total(result));
        var query = Assert.Single(_w.Queries);
        AssertNarrowed(query.TopParentIds);
    }

    [Fact]
    public async Task WatchHistory_QueryNarrowedToAllowedLibraries()
    {
        _w.QueryReturns([_w.AllowedMovie]);
        var tool = new WatchHistoryTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Library.Object);

        var result = await Run(tool, "{}");

        Assert.Equal(1, Total(result));
        AssertNarrowed(Assert.Single(_w.Queries).TopParentIds);
    }

    [Fact]
    public async Task NextUp_WalksOnlyAllowedLibraries_AndCountsExactly()
    {
        var allowed = _w.Add(new Episode { Id = Guid.NewGuid(), Name = "Allowed Episode" }, _w.Movies);
        var hidden = _w.Add(new Episode { Id = Guid.NewGuid(), Name = "Hidden Episode" }, _w.Kids);
        BaseItem[]? parents = null;
        NextUpQuery? sent = null;
        _tv.Setup(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<BaseItem[]>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, BaseItem[], DtoOptions>((q, p, _) => (sent, parents) = (q, p))
            .Returns(new QueryResult<BaseItem>(0, 2, new BaseItem[] { hidden, allowed }));
        var tool = new NextUpTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _tv.Object, Lookup);

        var result = await Run(tool, """{"limit":5}""");

        Assert.Equal(new BaseItem[] { _w.Movies }, parents);
        Assert.Null(sent!.Limit);
        Assert.Equal(1, Total(result));
        Assert.Equal(new[] { "Allowed Episode" }, Names(result));
        _tv.Verify(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()), Times.Never);
    }

    [Fact]
    public async Task NextUp_HiddenSeries_LooksMissing()
    {
        var series = _w.Add(new Series { Id = Guid.NewGuid(), Name = "Hidden Show" }, _w.Kids);
        var tool = new NextUpTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _tv.Object, Lookup);

        await AssertNotFound(tool, $$"""{"seriesId":"{{series.Id}}"}""");
        _tv.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SimilarItems_HiddenItem_LooksMissing_AndResultsFiltered()
    {
        var other = AllowedMovie("Other Allowed");
        _similar.Setup(s => s.GetSimilarItemsAsync(It.IsAny<BaseItem>(), It.IsAny<IReadOnlyList<Guid>>(), _w.User, It.IsAny<DtoOptions>(), It.IsAny<int?>(), It.IsAny<LibraryOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_w.HiddenMovie, other]);
        var tool = new SimilarItemsTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, Lookup, _w.Library.Object, _similar.Object);

        await AssertNotFound(tool, $$"""{"itemId":"{{_w.HiddenMovie.Id}}"}""");
        var result = await Run(tool, $$"""{"itemId":"{{_w.AllowedMovie.Id}}","limit":5}""");

        Assert.Equal(1, Total(result));
        Assert.Equal(new[] { "Other Allowed" }, Names(result));
        _similar.Verify(s => s.GetSimilarItemsAsync(_w.AllowedMovie, It.IsAny<IReadOnlyList<Guid>>(), _w.User, It.IsAny<DtoOptions>(), 50, It.IsAny<LibraryOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MovieRecommendations_BasedOnAllowedLibrariesOnly_AndFiltered()
    {
        _similar.Setup(s => s.GetMovieRecommendationsAsync(It.IsAny<User?>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DtoOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SimilarItemsRecommendation { RecommendationType = RecommendationType.SimilarToRecentlyPlayed, BaselineItemName = "Allowed Movie", CategoryId = Guid.NewGuid(), Items = [_w.HiddenMovie, AllowedMovie("Pick")] },
                new SimilarItemsRecommendation { RecommendationType = RecommendationType.HasActorFromRecentlyPlayed, BaselineItemName = "Actor", CategoryId = Guid.NewGuid(), Items = [_w.HiddenMovie] },
            ]);
        var tool = new MovieRecommendationsTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, Lookup, _similar.Object);

        await AssertNotFound(tool, $$"""{"libraryId":"{{_w.Kids.Id}}"}""");
        var categories = Json(await Run(tool, "{}")).GetProperty("categories").EnumerateArray().ToList();

        var only = Assert.Single(categories);
        Assert.Equal("Pick", Assert.Single(only.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        _similar.Verify(s => s.GetMovieRecommendationsAsync(_w.User, _w.Movies.Id, 5, 8, It.IsAny<DtoOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _similar.Verify(s => s.GetMovieRecommendationsAsync(It.IsAny<User?>(), Guid.Empty, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DtoOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPerson_OnlyCreditedOnHiddenItems_LooksMissing()
    {
        var person = new Person { Id = Guid.NewGuid(), Name = "Kids Actor" };
        _w.Library.Setup(l => l.GetItemById(person.Id)).Returns(person);
        _w.Library.Setup(l => l.GetPerson("Kids Actor")).Returns(person);
        _w.QueryReturns([], byName: []);
        var tool = new GetPersonTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Library.Object, Lookup);

        await AssertNotFound(tool, $$"""{"personId":"{{person.Id}}"}""");
        await AssertNotFound(tool, """{"name":"Kids Actor"}""");
        Assert.All(_w.Queries, q => AssertNarrowed(q.TopParentIds));
    }

    [Fact]
    public async Task GetPerson_Filmography_OnlyAllowedLibraries()
    {
        var person = new Person { Id = Guid.NewGuid(), Name = "Shared Actor" };
        _w.Library.Setup(l => l.GetItemById(person.Id)).Returns(person);
        _w.QueryReturns([_w.AllowedMovie], byName: [person]);
        var tool = new GetPersonTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, _w.Library.Object, Lookup);

        var result = await Run(tool, $$"""{"personId":"{{person.Id}}"}""");

        Assert.Equal(1, Total(result));
        Assert.All(_w.Queries, q => AssertNarrowed(q.TopParentIds));
    }

    [Fact]
    public async Task ListCollections_HiddenItem_LooksMissing_AndEntriesCounted()
    {
        var set = _w.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Name = "Set", Entries = [_w.HiddenMovie, _w.AllowedMovie] }, _w.Collections);
        var hiddenSet = _w.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Name = "Kids Set", Entries = [_w.HiddenMovie] }, _w.Collections);
        _w.QueryReturns([hiddenSet, set]);
        var tool = new ListCollectionsTool(_w.Users.Object, _w.Settings.Object, _w.UserData.Object, Lookup, _w.Library.Object, _collections.Object);

        await AssertNotFound(tool, $$"""{"itemId":"{{_w.HiddenMovie.Id}}"}""");
        var result = await Run(tool, "{}");

        Assert.Equal(1, Total(result));
        var only = Assert.Single(Json(result).GetProperty("items").EnumerateArray());
        Assert.Equal("Set", only.GetProperty("name").GetString());
        Assert.Equal(1, only.GetProperty("childCount").GetInt32());
        _collections.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListPlaylists_HidesPlaylistsWithOnlyHiddenEntries()
    {
        var mixed = _w.Add(new RestrictedWorld.Playlist { Id = Guid.NewGuid(), Name = "Mixed", OwnerUserId = _w.User.Id, Entries = [_w.HiddenMovie, _w.AllowedMovie] });
        var kids = _w.Add(new RestrictedWorld.Playlist { Id = Guid.NewGuid(), Name = "Kids", OwnerUserId = _w.User.Id, Entries = [_w.HiddenMovie] });
        var empty = _w.Add(new RestrictedWorld.Playlist { Id = Guid.NewGuid(), Name = "New", OwnerUserId = _w.User.Id });
        _playlists.Setup(p => p.GetPlaylists(_w.User.Id)).Returns([mixed, kids, empty]);
        _playlists.Setup(p => p.GetPlaylistForUser(kids.Id, _w.User.Id)).Returns(kids);
        var tool = new ListPlaylistsTool(_w.Users.Object, _w.Settings.Object, _playlists.Object, new PlaylistResolver(Lookup, _playlists.Object), _w.UserData.Object);

        var result = await Run(tool, "{}");
        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(tool, $$"""{"playlistId":"{{kids.Id}}"}"""));

        Assert.Equal(2, Total(result));
        Assert.Equal(new[] { "Mixed", "New" }, Names(result));
        Assert.Equal(PlaylistResolver.NotFound, ex.Message);
    }

    [Fact]
    public async Task ManagePlaylist_HiddenItem_LooksMissing_AndContainersExpandToAllowedEntries()
    {
        var set = _w.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Name = "Set", Entries = [_w.HiddenMovie, _w.AllowedMovie] }, _w.Collections);
        PlaylistCreationRequest? created = null;
        _playlists.Setup(p => p.CreatePlaylist(It.IsAny<PlaylistCreationRequest>()))
            .Callback<PlaylistCreationRequest>(r => created = r)
            .ReturnsAsync(new PlaylistCreationResult(Guid.NewGuid().ToString("N")));
        var tool = new ManagePlaylistTool(_w.Users.Object, _w.Settings.Object, Lookup, _playlists.Object, new PlaylistResolver(Lookup, _playlists.Object), _w.Library.Object);

        await AssertNotFound(tool, $$"""{"action":"create","name":"x","itemIds":["{{_w.HiddenMovie.Id}}"]}""");
        await Run(tool, $$"""{"action":"create","name":"x","itemIds":["{{set.Id}}"]}""");

        Assert.Equal(new[] { _w.AllowedMovie.Id }, created!.ItemIdList);
    }

    [Fact]
    public async Task SetFavorite_HiddenItem_LooksMissing()
    {
        _w.UserData.Setup(u => u.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>())).Returns(new UserItemData { Key = "k" });
        var tool = new SetFavoriteTool(_w.Users.Object, _w.Settings.Object, Lookup, _w.UserData.Object);

        await AssertNotFound(tool, $$"""{"itemId":"{{_w.HiddenMovie.Id}}","favorite":true}""");
        _w.UserData.Verify(u => u.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetPlayed_HiddenItem_LooksMissing_AndCollectionMarksOnlyAllowedEntries()
    {
        var allowed = _w.Add(new Manage.Fakes.Movie { Id = Guid.NewGuid(), Name = "A" }, _w.Movies);
        var hidden = _w.Add(new Manage.Fakes.Movie { Id = Guid.NewGuid(), Name = "H" }, _w.Kids);
        var set = _w.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Name = "Set", Entries = [hidden, allowed] }, _w.Collections);
        var tool = new SetPlayedTool(_w.Users.Object, _w.Settings.Object, Lookup, _w.UserData.Object);

        await AssertNotFound(tool, $$"""{"itemId":"{{hidden.Id}}","played":true}""");
        await Run(tool, $$"""{"itemId":"{{set.Id}}","played":true}""");

        Assert.Single(allowed.Played);
        Assert.Empty(hidden.Played);
    }

    [Fact]
    public async Task Play_HiddenItem_LooksMissing_AndCollectionPlaysOnlyAllowedEntries()
    {
        AddSession(null);
        var set = _w.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Name = "Set", Entries = [_w.HiddenMovie, _w.AllowedMovie] }, _w.Collections);
        PlayRequest? sent = null;
        _sessions.Setup(s => s.SendPlayCommand(It.IsAny<string>(), "mine", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, PlayRequest, CancellationToken>((_, _, r, _) => sent = r)
            .Returns(Task.CompletedTask);
        var tool = new PlayTool(_w.Users.Object, _w.Settings.Object, Sessions(), _sessions.Object, Lookup, new TrackValidator(_mediaSources.Object));

        await AssertNotFound(tool, $$"""{"sessionId":"mine","itemIds":["{{_w.AllowedMovie.Id}}","{{_w.HiddenMovie.Id}}"]}""");
        Assert.Null(sent);
        var result = await Run(tool, $$"""{"sessionId":"mine","itemIds":["{{set.Id}}"]}""");

        Assert.Equal(new[] { _w.AllowedMovie.Id }, sent!.ItemIds);
        Assert.Equal(1, Json(result).GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task PlaybackControl_TrackOfHiddenItem_LooksMissing()
    {
        AddSession(_w.HiddenMovie);
        var tool = new PlaybackControlTool(_w.Users.Object, _w.Settings.Object, Sessions(), _sessions.Object, Lookup, new TrackValidator(_mediaSources.Object));

        await AssertNotFound(tool, """{"sessionId":"mine","command":"set_audio_track","streamIndex":1}""");
        _sessions.Verify(s => s.SendGeneralCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeneralCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListSessions_HiddenNowPlaying_Blanked()
    {
        AddSession(_w.HiddenMovie);
        var tool = new ListSessionsTool(_w.Users.Object, _w.Settings.Object, Sessions(), _sessions.Object, Lookup);

        var result = await Run(tool, "{}");

        var session = Assert.Single(Json(result).GetProperty("items").EnumerateArray());
        Assert.Equal("TV", session.GetProperty("deviceName").GetString());
        Assert.False(session.TryGetProperty("nowPlaying", out var playing) && playing.ValueKind != JsonValueKind.Null);
        Assert.Equal(0, session.GetProperty("positionMinutes").GetDouble());
        Assert.False(session.GetProperty("isPaused").GetBoolean());
        Assert.DoesNotContain("Hidden", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(_w.HiddenMovie.Id.ToString("N"), result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSessions_AllowedNowPlaying_Shown()
    {
        AddSession(_w.AllowedMovie);
        var tool = new ListSessionsTool(_w.Users.Object, _w.Settings.Object, Sessions(), _sessions.Object, Lookup);

        var session = Assert.Single(Json(await Run(tool, "{}")).GetProperty("items").EnumerateArray());

        Assert.Equal("Allowed Movie", session.GetProperty("nowPlaying").GetProperty("name").GetString());
        Assert.Equal(10, session.GetProperty("positionMinutes").GetDouble());
    }

    [Fact]
    public async Task RefreshItem_HiddenItem_LooksMissing()
    {
        // A restricted token never holds the admin scope; the tool is still checked in case it is reached.
        var providers = new Mock<IProviderManager>();
        var tool = new RefreshItemTool(_w.Users.Object, _w.Settings.Object, Lookup, providers.Object, new Mock<IFileSystem>().Object);

        await AssertNotFound(tool, $$"""{"itemId":"{{_w.HiddenMovie.Id}}"}""");
        providers.VerifyNoOtherCalls();
    }
}
