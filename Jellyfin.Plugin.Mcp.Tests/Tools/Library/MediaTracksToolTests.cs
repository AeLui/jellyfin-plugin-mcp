using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library;

public class MediaTracksToolTests
{
    private readonly User _user = TestUsers.Create();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new(MockBehavior.Strict);
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<IUserViewManager> _views = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly Mock<IMediaSourceManager> _mediaSources = new(MockBehavior.Strict);
    private readonly McpPrincipal _principal;

    public MediaTracksToolTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(50);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Library });
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private GetMediaTracksTool Tool() => new(_users.Object, _settings.Object, _userData.Object, new ItemLookup(_library.Object, _views.Object), _mediaSources.Object);

    private Movie VisibleMovie(params MediaStream[] streams)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Two Voices" };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        _mediaSources.Setup(m => m.GetMediaStreams(movie.Id)).Returns(streams);
        return movie;
    }

    [Fact]
    public async Task MediaTracks_SplitsAudioAndSubtitles()
    {
        var movie = VisibleMovie(
            new MediaStream { Index = 0, Type = MediaStreamType.Video, Codec = "h264" },
            new MediaStream { Index = 1, Type = MediaStreamType.Audio, Language = "eng", Codec = "aac", Channels = 2, IsDefault = true },
            new MediaStream { Index = 2, Type = MediaStreamType.Audio, Language = "spa", Title = "Spanish", Codec = "aac", Channels = 6 },
            new MediaStream { Index = 3, Type = MediaStreamType.Subtitle, Language = "eng", Codec = "subrip", IsForced = true },
            new MediaStream { Index = 4, Type = MediaStreamType.Subtitle, Language = "spa", Codec = "srt", IsExternal = true, IsHearingImpaired = true });
        var tool = Tool();

        var result = await tool.ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var root = doc.RootElement;
        Assert.Equal(movie.Id.ToString("N"), root.GetProperty("itemId").GetString());
        var audio = root.GetProperty("audioTracks").EnumerateArray().ToList();
        Assert.Equal(new[] { 1, 2 }, audio.Select(a => a.GetProperty("index").GetInt32()));
        Assert.Equal("eng", audio[0].GetProperty("language").GetString());
        Assert.True(audio[0].GetProperty("isDefault").GetBoolean());
        Assert.Equal(6, audio[1].GetProperty("channels").GetInt32());
        Assert.Equal("Spanish", audio[1].GetProperty("title").GetString());
        var subtitles = root.GetProperty("subtitleTracks").EnumerateArray().ToList();
        Assert.Equal(new[] { 3, 4 }, subtitles.Select(s => s.GetProperty("index").GetInt32()));
        Assert.True(subtitles[0].GetProperty("isForced").GetBoolean());
        Assert.True(subtitles[1].GetProperty("isExternal").GetBoolean());
        Assert.True(subtitles[1].GetProperty("isHearingImpaired").GetBoolean());
        Assert.All(audio.Concat(subtitles), t => Assert.False(string.IsNullOrEmpty(t.GetProperty("displayTitle").GetString())));
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task MediaTracks_OmitsMissingOptionalFields()
    {
        var movie = VisibleMovie(new MediaStream { Index = 1, Type = MediaStreamType.Audio });
        var tool = Tool();

        var result = await tool.ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var track = Assert.Single(doc.RootElement.GetProperty("audioTracks").EnumerateArray());
        Assert.False(track.TryGetProperty("language", out _));
        Assert.False(track.TryGetProperty("title", out _));
        Assert.False(track.TryGetProperty("codec", out _));
        Assert.False(track.TryGetProperty("channels", out _));
        Assert.Equal("Track 1", track.GetProperty("displayTitle").GetString());
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task MediaTracks_HiddenItem_IsNotFound()
    {
        var id = Guid.NewGuid();
        _library.Setup(l => l.GetItemById<BaseItem>(id, _user)).Returns((BaseItem?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Tool().ExecuteAsync(Args($$"""{"itemId":"{{id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _mediaSources.VerifyNoOtherCalls();
    }

    public static TheoryData<BaseItem> NonVideoItems() => new()
    {
        new Audio { Id = Guid.NewGuid() },
        new Series { Id = Guid.NewGuid() },
        new MusicAlbum { Id = Guid.NewGuid() },
    };

    [Theory]
    [MemberData(nameof(NonVideoItems))]
    public async Task MediaTracks_NonVideo_IsError(BaseItem item)
    {
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Tool().ExecuteAsync(Args($$"""{"itemId":"{{item.Id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Tracks are only available for video items", ex.Message);
    }

    [Fact]
    public async Task MediaTracks_Episode_IsAccepted()
    {
        var episode = new Episode { Id = Guid.NewGuid() };
        _library.Setup(l => l.GetItemById<BaseItem>(episode.Id, _user)).Returns(episode);
        _mediaSources.Setup(m => m.GetMediaStreams(episode.Id)).Returns(Array.Empty<MediaStream>());

        var result = await Tool().ExecuteAsync(Args($$"""{"itemId":"{{episode.Id}}"}"""), _principal, CancellationToken.None);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task MediaTracks_NoStreams_Conforms()
    {
        var movie = VisibleMovie();
        var tool = Tool();

        var result = await tool.ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Empty(doc.RootElement.GetProperty("audioTracks").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("subtitleTracks").EnumerateArray());
        SchemaAssert.Conforms(tool, result);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"itemId":"x"}""")]
    public async Task MediaTracks_MalformedArguments_NameTheArgument(string json)
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Tool().ExecuteAsync(Args(json), _principal, CancellationToken.None));

        Assert.Contains("'itemId'", ex.Message, StringComparison.Ordinal);
    }
}
