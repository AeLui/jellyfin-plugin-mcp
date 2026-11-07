using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tests.Tools;
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Playback;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Playback;

public class PlaybackToolsTests
{
    private readonly User _user = TestUsers.Create();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly Mock<ISessionManager> _sessionManager = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly Mock<IMediaSourceManager> _mediaSources = new();
    private readonly List<SessionInfo> _sessions = [];
    private readonly McpPrincipal _principal;
    private readonly McpPrincipal _adminPrincipal;

    public PlaybackToolsTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _sessionManager.SetupGet(m => m.Sessions).Returns(() => _sessions);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Playback });
        _adminPrincipal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Playback, McpScopes.Admin });
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private SessionInfo AddSession(string id, Guid userId, bool remoteControl = true, bool isPaused = false, long positionTicks = 0)
    {
        var controller = new Mock<ISessionController>();
        controller.SetupGet(c => c.SupportsMediaControl).Returns(remoteControl);
        controller.SetupGet(c => c.IsSessionActive).Returns(true);
        var session = new SessionInfo(_sessionManager.Object, NullLogger.Instance)
        {
            Id = id,
            UserId = userId,
            UserName = "u-" + id,
            DeviceName = "dev-" + id,
            Client = "client",
            Capabilities = new ClientCapabilities { SupportsMediaControl = remoteControl },
            SessionControllers = [controller.Object],
            PlayState = new PlayerStateInfo { IsPaused = isPaused, PositionTicks = positionTicks },
        };
        _sessions.Add(session);
        return session;
    }

    private SessionResolver Resolver() => new(_sessionManager.Object, _users.Object);

    private ListSessionsTool List() => new(_users.Object, _settings.Object, Resolver(), _sessionManager.Object);

    private PlayTool Play() => new(_users.Object, _settings.Object, Resolver(), _sessionManager.Object, new ItemLookup(_library.Object, new Mock<IUserViewManager>().Object), new TrackValidator(_mediaSources.Object));

    private PlaybackControlTool Control() => new(_users.Object, _settings.Object, Resolver(), _sessionManager.Object, new ItemLookup(_library.Object, new Mock<IUserViewManager>().Object), new TrackValidator(_mediaSources.Object));

    private SendMessageTool Message() => new(_users.Object, _settings.Object, Resolver(), _sessionManager.Object);

    private void NeverSent()
    {
        _sessionManager.Verify(m => m.SendPlayCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _sessionManager.Verify(m => m.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _sessionManager.Verify(m => m.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static JsonElement Parse(ToolResult result) => JsonDocument.Parse(result.Text).RootElement;

    [Fact]
    public async Task ListSessions_NonAdmin_OnlyOwnSessions()
    {
        AddSession("mine", _user.Id, isPaused: true, positionTicks: 3 * TimeSpan.TicksPerMinute);
        AddSession("theirs", Guid.NewGuid());
        AddSession("no-remote", _user.Id, remoteControl: false);

        var result = await List().ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        var item = Assert.Single(Parse(result).GetProperty("items").EnumerateArray());
        Assert.Equal("mine", item.GetProperty("sessionId").GetString());
        Assert.True(item.GetProperty("isPaused").GetBoolean());
        Assert.Equal(3, item.GetProperty("positionMinutes").GetDouble());
        Assert.False(item.TryGetProperty("nowPlaying", out _));
    }

    [Fact]
    public async Task ListSessions_Admin_AllControllableSessions()
    {
        _user.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, true);
        AddSession("mine", _user.Id);
        AddSession("theirs", Guid.NewGuid());
        AddSession("no-remote", Guid.NewGuid(), remoteControl: false);

        var result = await List().ExecuteAsync(Args("{}"), _adminPrincipal, CancellationToken.None);

        Assert.Equal(new[] { "mine", "theirs" }, Parse(result).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Play_ForeignSession_NonAdmin_SessionNotFound()
    {
        AddSession("theirs", Guid.NewGuid());
        var item = new Movie { Id = Guid.NewGuid(), Name = "M" };
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);

        var foreign = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"theirs","itemIds":["{{item.Id}}"]}"""), _principal, CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"nope","itemIds":["{{item.Id}}"]}"""), _principal, CancellationToken.None));

        Assert.Equal("Session not found", foreign.Message);
        Assert.Equal(foreign.Message, unknown.Message);
        NeverSent();
    }

    [Fact]
    public async Task Admin_WithRemoteControlPermission_CanControlForeignSession_SendingOnce()
    {
        _user.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, true);
        AddSession("theirs", Guid.NewGuid());
        var item = new Movie { Id = Guid.NewGuid(), Name = "M" };
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);

        await Play().ExecuteAsync(Args($$"""{"sessionId":"theirs","itemIds":["{{item.Id}}"]}"""), _adminPrincipal, CancellationToken.None);
        await Control().ExecuteAsync(Args("""{"sessionId":"theirs","command":"pause"}"""), _adminPrincipal, CancellationToken.None);
        await Message().ExecuteAsync(Args("""{"sessionId":"theirs","text":"hi"}"""), _adminPrincipal, CancellationToken.None);

        _sessionManager.Verify(m => m.SendPlayCommand(It.IsAny<string>(), "theirs", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _sessionManager.Verify(m => m.SendPlaystateCommand(It.IsAny<string>(), "theirs", It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _sessionManager.Verify(m => m.SendMessageCommand(It.IsAny<string>(), "theirs", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Admin_WithoutRemoteControlPermission_ForeignSession_NotListedNotControllable()
    {
        _user.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, false);
        AddSession("mine", _user.Id);
        AddSession("theirs", Guid.NewGuid());
        var item = new Movie { Id = Guid.NewGuid(), Name = "M" };
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);

        var listed = await List().ExecuteAsync(Args("{}"), _adminPrincipal, CancellationToken.None);
        var play = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"theirs","itemIds":["{{item.Id}}"]}"""), _adminPrincipal, CancellationToken.None));
        var control = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"theirs","command":"pause"}"""), _adminPrincipal, CancellationToken.None));
        var message = await Assert.ThrowsAsync<ToolException>(() =>
            Message().ExecuteAsync(Args("""{"sessionId":"theirs","text":"hi"}"""), _adminPrincipal, CancellationToken.None));

        Assert.Equal("mine", Assert.Single(Parse(listed).GetProperty("items").EnumerateArray()).GetProperty("sessionId").GetString());
        Assert.All(new[] { play, control, message }, e => Assert.Equal("Session not found", e.Message));
        NeverSent();
    }

    [Fact]
    public async Task ServerRefusals_AreMappedWithoutServerMessage()
    {
        AddSession("mine", _user.Id);
        var item = new Movie { Id = Guid.NewGuid(), Name = "M" };
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);
        _sessionManager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("bob is not allowed to play media."));
        _sessionManager.Setup(m => m.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
            .Throws(new MediaBrowser.Common.Extensions.ResourceNotFoundException("Session bob-phone not found"));
        _sessionManager.Setup(m => m.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
            .Throws(new MediaBrowser.Common.Extensions.ResourceNotFoundException("Session bob-phone not found"));

        var play = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{item.Id}}"]}"""), _principal, CancellationToken.None));
        var control = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"pause"}"""), _principal, CancellationToken.None));
        var message = await Assert.ThrowsAsync<ToolException>(() =>
            Message().ExecuteAsync(Args("""{"sessionId":"mine","text":"hi"}"""), _principal, CancellationToken.None));

        Assert.Equal("Playback was refused by the server", play.Message);
        Assert.Equal("Session not found", control.Message);
        Assert.Equal("Session not found", message.Message);
    }

    [Fact]
    public async Task Control_And_Message_ForeignSession_NonAdmin_SessionNotFound()
    {
        AddSession("theirs", Guid.NewGuid());

        var control = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"theirs","command":"pause"}"""), _principal, CancellationToken.None));
        var message = await Assert.ThrowsAsync<ToolException>(() =>
            Message().ExecuteAsync(Args("""{"sessionId":"theirs","text":"hi"}"""), _principal, CancellationToken.None));

        Assert.Equal("Session not found", control.Message);
        Assert.Equal("Session not found", message.Message);
        NeverSent();
    }

    [Fact]
    public async Task Play_ItemNotVisible_IsError()
    {
        AddSession("mine", _user.Id);
        var id = Guid.NewGuid();
        _library.Setup(l => l.GetItemById<BaseItem>(id, _user)).Returns((BaseItem?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{id}}"]}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _library.Verify(l => l.GetItemById(It.IsAny<Guid>()), Times.Never);
        NeverSent();
    }

    [Fact]
    public async Task Play_ModeNext_MapsToPlayNext()
    {
        AddSession("mine", _user.Id);
        var item = new Movie { Id = Guid.NewGuid(), Name = "M" };
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);
        PlayRequest? sent = null;
        _sessionManager.Setup(m => m.SendPlayCommand(string.Empty, "mine", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, PlayRequest, CancellationToken>((_, _, r, _) => sent = r)
            .Returns(Task.CompletedTask);

        await Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{item.Id}}"],"mode":"next","startPositionMinutes":2}"""), _principal, CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal(PlayCommand.PlayNext, sent.PlayCommand);
        Assert.Equal(new[] { item.Id }, sent.ItemIds);
        Assert.Equal(2 * TimeSpan.TicksPerMinute, sent.StartPositionTicks);
    }

    [Fact]
    public async Task Play_DefaultsToPlayNow()
    {
        AddSession("mine", _user.Id);
        var item = new Movie { Id = Guid.NewGuid(), Name = "M" };
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);
        PlayRequest? sent = null;
        _sessionManager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), "mine", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, PlayRequest, CancellationToken>((_, _, r, _) => sent = r)
            .Returns(Task.CompletedTask);

        await Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{item.Id}}"]}"""), _principal, CancellationToken.None);

        Assert.Equal(PlayCommand.PlayNow, sent!.PlayCommand);
        Assert.Null(sent.StartPositionTicks);
    }

    [Fact]
    public async Task Play_NegativeStartOrEmptyItems_IsError()
    {
        AddSession("mine", _user.Id);
        var id = Guid.NewGuid();

        var negative = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{id}}"],"startPositionMinutes":-1}"""), _principal, CancellationToken.None));
        var empty = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args("""{"sessionId":"mine","itemIds":[]}"""), _principal, CancellationToken.None));

        Assert.Contains("startPositionMinutes", negative.Message, StringComparison.Ordinal);
        Assert.Contains("itemIds", empty.Message, StringComparison.Ordinal);
        NeverSent();
    }

    [Fact]
    public async Task Control_SeekWithoutPosition_IsError()
    {
        AddSession("mine", _user.Id);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"seek"}"""), _principal, CancellationToken.None));

        Assert.Contains("positionMinutes", ex.Message, StringComparison.Ordinal);
        NeverSent();
    }

    [Fact]
    public async Task Control_NegativeSeek_IsError()
    {
        AddSession("mine", _user.Id);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"seek","positionMinutes":-5}"""), _principal, CancellationToken.None));

        Assert.Contains("positionMinutes", ex.Message, StringComparison.Ordinal);
        NeverSent();
    }

    [Fact]
    public async Task Control_Seek_ConvertsMinutesToTicks()
    {
        AddSession("mine", _user.Id);
        PlaystateRequest? sent = null;
        _sessionManager.Setup(m => m.SendPlaystateCommand(string.Empty, "mine", It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, PlaystateRequest, CancellationToken>((_, _, r, _) => sent = r)
            .Returns(Task.CompletedTask);

        await Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"seek","positionMinutes":12.5}"""), _principal, CancellationToken.None);

        Assert.Equal(PlaystateCommand.Seek, sent!.Command);
        Assert.Equal(12 * TimeSpan.TicksPerMinute + (TimeSpan.TicksPerMinute / 2), sent.SeekPositionTicks);
    }

    [Theory]
    [InlineData("pause", PlaystateCommand.Pause)]
    [InlineData("unpause", PlaystateCommand.Unpause)]
    [InlineData("stop", PlaystateCommand.Stop)]
    [InlineData("next", PlaystateCommand.NextTrack)]
    [InlineData("previous", PlaystateCommand.PreviousTrack)]
    public async Task Control_MapsCommands(string command, PlaystateCommand expected)
    {
        AddSession("mine", _user.Id);
        PlaystateRequest? sent = null;
        _sessionManager.Setup(m => m.SendPlaystateCommand(It.IsAny<string>(), "mine", It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, PlaystateRequest, CancellationToken>((_, _, r, _) => sent = r)
            .Returns(Task.CompletedTask);

        await Control().ExecuteAsync(Args($$"""{"sessionId":"mine","command":"{{command}}"}"""), _principal, CancellationToken.None);

        Assert.Equal(expected, sent!.Command);
        Assert.Null(sent.SeekPositionTicks);
    }

    [Fact]
    public async Task Control_UnknownCommand_IsError()
    {
        AddSession("mine", _user.Id);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"explode"}"""), _principal, CancellationToken.None));

        Assert.Contains("command", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessage_TooLong_IsError()
    {
        AddSession("mine", _user.Id);
        var text = new string('x', 501);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Message().ExecuteAsync(Args($$"""{"sessionId":"mine","text":"{{text}}"}"""), _principal, CancellationToken.None));

        Assert.Contains("text", ex.Message, StringComparison.Ordinal);
        NeverSent();
    }

    [Fact]
    public async Task SendMessage_SendsWithDefaultTimeout()
    {
        AddSession("mine", _user.Id);
        MessageCommand? sent = null;
        _sessionManager.Setup(m => m.SendMessageCommand(string.Empty, "mine", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, MessageCommand, CancellationToken>((_, _, c, _) => sent = c)
            .Returns(Task.CompletedTask);

        await Message().ExecuteAsync(Args("""{"sessionId":"mine","text":"Dinner","header":"Hi"}"""), _principal, CancellationToken.None);

        Assert.Equal("Dinner", sent!.Text);
        Assert.Equal("Hi", sent.Header);
        Assert.Equal(10_000, sent.TimeoutMs);
    }

    [Fact]
    public void Tools_DeclareScopeAndAnnotations()
    {
        Assert.All(new PlaybackToolBase[] { List(), Play(), Control(), Message() }, t => Assert.Equal(McpScopes.Playback, t.RequiredScope));
        Assert.True(List().Annotations.ReadOnly);
        Assert.False(Play().Annotations.ReadOnly);
        Assert.True(Control().Annotations.Idempotent);
        Assert.False(Play().Annotations.Idempotent);
    }

    [Fact]
    public async Task ListSessions_WrapsInItemsAndConformsToOutputSchema()
    {
        var full = AddSession("full", _user.Id, positionTicks: 90 * TimeSpan.TicksPerSecond);
        full.FullNowPlayingItem = new Episode { Id = Guid.NewGuid(), Name = "Pilot", SeriesName = "Lost", ParentIndexNumber = 1, IndexNumber = 1 };
        var dtoOnly = AddSession("dto", _user.Id, isPaused: true);
        dtoOnly.NowPlayingItem = new MediaBrowser.Model.Dto.BaseItemDto { Id = Guid.NewGuid(), Name = "Song" };
        AddSession("idle", _user.Id);
        var tool = List();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        Assert.Equal(new[] { "full", "dto", "idle" }, Parse(result).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("sessionId").GetString()));
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task Play_ConformsToOutputSchema()
    {
        AddSession("mine", _user.Id);
        var item = new Movie { Id = Guid.NewGuid(), Name = "M" };
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);
        _sessionManager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), "mine", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var tool = Play();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{item.Id}}"]}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task Control_ConformsToOutputSchema()
    {
        AddSession("mine", _user.Id);
        _sessionManager.Setup(m => m.SendPlaystateCommand(string.Empty, "mine", It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var tool = Control();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args("""{"sessionId":"mine","command":"pause"}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task SendMessage_ConformsToOutputSchema()
    {
        AddSession("mine", _user.Id);
        _sessionManager.Setup(m => m.SendMessageCommand(string.Empty, "mine", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var tool = Message();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args("""{"sessionId":"mine","text":"Dinner"}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task ListSessions_NowPlayingDtoWithoutName_ConformsToOutputSchema()
    {
        var session = AddSession("dto", _user.Id);
        session.NowPlayingItem = new MediaBrowser.Model.Dto.BaseItemDto { Id = Guid.NewGuid(), Name = null };
        var tool = List();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        Assert.DoesNotContain("null", result.Text, StringComparison.Ordinal);
        SchemaAssert.Conforms(tool, result);
    }

    private Movie MovieWithTracks()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Two Voices" };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        _mediaSources.Setup(m => m.GetMediaStreams(movie.Id)).Returns(new[]
        {
            new MediaStream { Index = 0, Type = MediaStreamType.Video },
            new MediaStream { Index = 1, Type = MediaStreamType.Audio, Language = "eng" },
            new MediaStream { Index = 2, Type = MediaStreamType.Audio, Language = "spa" },
            new MediaStream { Index = 3, Type = MediaStreamType.Subtitle, Language = "eng" },
        });
        return movie;
    }

    private Func<PlayRequest?> CapturePlay()
    {
        PlayRequest? sent = null;
        _sessionManager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), "mine", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, PlayRequest, CancellationToken>((_, _, r, _) => sent = r)
            .Returns(Task.CompletedTask);
        return () => sent;
    }

    [Fact]
    public async Task Play_WithTracks_SetsRequestIndexes()
    {
        AddSession("mine", _user.Id);
        var movie = MovieWithTracks();
        var sent = CapturePlay();
        var tool = Play();

        var result = await tool.ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{movie.Id}}"],"audioStreamIndex":2,"subtitleStreamIndex":3}"""), _principal, CancellationToken.None);

        Assert.Equal(2, sent()!.AudioStreamIndex);
        Assert.Equal(3, sent()!.SubtitleStreamIndex);
        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(2, doc.RootElement.GetProperty("audioStreamIndex").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("subtitleStreamIndex").GetInt32());
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task Play_SubtitlesOff_SendsMinusOne()
    {
        AddSession("mine", _user.Id);
        var movie = MovieWithTracks();
        var sent = CapturePlay();

        await Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{movie.Id}}"],"subtitleStreamIndex":-1}"""), _principal, CancellationToken.None);

        Assert.Equal(-1, sent()!.SubtitleStreamIndex);
        Assert.Null(sent()!.AudioStreamIndex);
    }

    [Fact]
    public async Task Play_AudioMinusOne_IsError()
    {
        AddSession("mine", _user.Id);
        var movie = MovieWithTracks();

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{movie.Id}}"],"audioStreamIndex":-1}"""), _principal, CancellationToken.None));

        Assert.Contains("'audioStreamIndex'", ex.Message, StringComparison.Ordinal);
        NeverSent();
    }

    [Fact]
    public async Task Play_IndexOfWrongType_IsError()
    {
        AddSession("mine", _user.Id);
        var movie = MovieWithTracks();

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{movie.Id}}"],"subtitleStreamIndex":1}"""), _principal, CancellationToken.None));

        Assert.Equal("Invalid argument 'subtitleStreamIndex': no subtitle track with index 1", ex.Message);
        NeverSent();
    }

    [Fact]
    public async Task Play_UnknownIndex_IsError()
    {
        AddSession("mine", _user.Id);
        var movie = MovieWithTracks();

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{movie.Id}}"],"audioStreamIndex":9}"""), _principal, CancellationToken.None));

        Assert.Equal("Invalid argument 'audioStreamIndex': no audio track with index 9", ex.Message);
        NeverSent();
    }

    [Fact]
    public async Task Play_TrackOnNonVideo_IsError()
    {
        AddSession("mine", _user.Id);
        var series = new Series { Id = Guid.NewGuid() };
        _library.Setup(l => l.GetItemById<BaseItem>(series.Id, _user)).Returns(series);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{series.Id}}"],"audioStreamIndex":1}"""), _principal, CancellationToken.None));

        Assert.Equal("Invalid argument 'audioStreamIndex': the item has no selectable tracks", ex.Message);
        NeverSent();
    }

    [Theory]
    [InlineData(true, "now")]
    [InlineData(false, "next")]
    [InlineData(false, "last")]
    public async Task Play_TrackWithSeveralItemsOrQueueMode_IsError(bool twoItems, string mode)
    {
        AddSession("mine", _user.Id);
        var movie = MovieWithTracks();
        var ids = twoItems ? $"\"{movie.Id}\",\"{movie.Id}\"" : $"\"{movie.Id}\"";

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":[{{ids}}],"mode":"{{mode}}","audioStreamIndex":1}"""), _principal, CancellationToken.None));

        Assert.Equal("Invalid argument 'audioStreamIndex': only allowed when playing one item now", ex.Message);
        NeverSent();
    }

    [Fact]
    public async Task Play_WithoutTracks_Unchanged()
    {
        AddSession("mine", _user.Id);
        var movie = MovieWithTracks();
        var sent = CapturePlay();

        var result = await Play().ExecuteAsync(Args($$"""{"sessionId":"mine","itemIds":["{{movie.Id}}"]}"""), _principal, CancellationToken.None);

        Assert.Null(sent()!.AudioStreamIndex);
        Assert.Null(sent()!.SubtitleStreamIndex);
        using var doc = JsonDocument.Parse(result.Text);
        Assert.False(doc.RootElement.TryGetProperty("audioStreamIndex", out _));
        Assert.False(doc.RootElement.TryGetProperty("subtitleStreamIndex", out _));
        _mediaSources.Verify(m => m.GetMediaStreams(It.IsAny<Guid>()), Times.Never);
    }

    private Func<GeneralCommand?> CaptureGeneral()
    {
        GeneralCommand? sent = null;
        _sessionManager.Setup(m => m.SendGeneralCommand(It.IsAny<string>(), "mine", It.IsAny<GeneralCommand>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, GeneralCommand, CancellationToken>((_, _, c, _) => sent = c)
            .Returns(Task.CompletedTask);
        return () => sent;
    }

    private void GeneralNeverSent()
        => _sessionManager.Verify(m => m.SendGeneralCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeneralCommand>(), It.IsAny<CancellationToken>()), Times.Never);

    [Theory]
    [InlineData("set_audio_track", 2, GeneralCommandType.SetAudioStreamIndex)]
    [InlineData("set_subtitle_track", 3, GeneralCommandType.SetSubtitleStreamIndex)]
    [InlineData("set_subtitle_track", -1, GeneralCommandType.SetSubtitleStreamIndex)]
    public async Task Control_SetTrack_SendsGeneralCommand(string command, int index, GeneralCommandType expected)
    {
        var session = AddSession("mine", _user.Id);
        session.FullNowPlayingItem = MovieWithTracks();
        var sent = CaptureGeneral();
        var tool = Control();

        var result = await tool.ExecuteAsync(Args($$"""{"sessionId":"mine","command":"{{command}}","streamIndex":{{index}}}"""), _principal, CancellationToken.None);

        _sessionManager.Verify(m => m.SendGeneralCommand(string.Empty, "mine", It.IsAny<GeneralCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(expected, sent()!.Name);
        Assert.Equal(index.ToString(System.Globalization.CultureInfo.InvariantCulture), sent()!.Arguments["Index"]);
        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(command, doc.RootElement.GetProperty("command").GetString());
        Assert.Equal(index, doc.RootElement.GetProperty("streamIndex").GetInt32());
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task Control_SetSubtitleTrack_AudioIndex_IsError()
    {
        var session = AddSession("mine", _user.Id);
        session.FullNowPlayingItem = MovieWithTracks();

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"set_subtitle_track","streamIndex":1}"""), _principal, CancellationToken.None));

        Assert.Equal("Invalid argument 'streamIndex': no subtitle track with index 1", ex.Message);
        GeneralNeverSent();
    }

    [Fact]
    public async Task Control_SetTrack_UsesNowPlayingDtoWhenNoFullItem()
    {
        var session = AddSession("mine", _user.Id);
        var movie = MovieWithTracks();
        session.NowPlayingItem = new BaseItemDto { Id = movie.Id, Name = movie.Name };
        var sent = CaptureGeneral();

        await Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"set_audio_track","streamIndex":2}"""), _principal, CancellationToken.None);

        Assert.Equal(GeneralCommandType.SetAudioStreamIndex, sent()!.Name);
        _library.Verify(l => l.GetItemById<BaseItem>(movie.Id, _user), Times.Once);
    }

    [Fact]
    public async Task Control_SetTrack_NothingPlaying_IsError()
    {
        AddSession("mine", _user.Id);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"set_audio_track","streamIndex":1}"""), _principal, CancellationToken.None));

        Assert.Equal("Nothing is playing on this session", ex.Message);
        GeneralNeverSent();
    }

    [Fact]
    public async Task Control_SetTrack_HiddenNowPlaying_IsNotFound()
    {
        var session = AddSession("mine", _user.Id);
        var hidden = new Movie { Id = Guid.NewGuid(), Name = "Hidden" };
        _library.Setup(l => l.GetItemById<BaseItem>(hidden.Id, _user)).Returns((BaseItem?)null);
        session.FullNowPlayingItem = hidden;

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"mine","command":"set_audio_track","streamIndex":1}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        GeneralNeverSent();
    }

    [Theory]
    [InlineData("""{"sessionId":"mine","command":"set_audio_track"}""")]
    [InlineData("""{"sessionId":"mine","command":"set_audio_track","streamIndex":-1}""")]
    [InlineData("""{"sessionId":"mine","command":"set_subtitle_track","streamIndex":-2}""")]
    [InlineData("""{"sessionId":"mine","command":"pause","streamIndex":1}""")]
    [InlineData("""{"sessionId":"mine","command":"set_audio_track","streamIndex":"x"}""")]
    public async Task Control_StreamIndexArgumentErrors_NameTheArgument(string json)
    {
        AddSession("mine", _user.Id).FullNowPlayingItem = MovieWithTracks();

        var ex = await Assert.ThrowsAsync<ToolException>(() => Control().ExecuteAsync(Args(json), _principal, CancellationToken.None));

        Assert.Contains("'streamIndex'", ex.Message, StringComparison.Ordinal);
        GeneralNeverSent();
    }

    [Fact]
    public async Task Control_SetTrack_OtherUsersSession_IsNotFound()
    {
        AddSession("theirs", Guid.NewGuid()).FullNowPlayingItem = MovieWithTracks();

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Control().ExecuteAsync(Args("""{"sessionId":"theirs","command":"set_audio_track","streamIndex":1}"""), _principal, CancellationToken.None));

        Assert.Equal("Session not found", ex.Message);
        GeneralNeverSent();
    }
}
