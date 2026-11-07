using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Queries;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Admin;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Admin;

public class AdminViewsTests
{
    private readonly User _user = TestUsers.Create(admin: true);
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly Mock<IServerApplicationHost> _host = new();
    private readonly Mock<ISystemManager> _system = new();
    private readonly Mock<ISessionManager> _sessionManager = new();
    private readonly Mock<ITaskManager> _tasks = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly Mock<IActivityManager> _activity = new();
    private readonly List<SessionInfo> _sessions = [];
    private readonly McpPrincipal _principal;

    public AdminViewsTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(100);
        _sessionManager.SetupGet(m => m.Sessions).Returns(() => _sessions);
        _tasks.SetupGet(t => t.ScheduledTasks).Returns(Array.Empty<IScheduledTaskWorker>());
        _host.SetupGet(h => h.FriendlyName).Returns("Test");
        _host.SetupGet(h => h.ApplicationVersionString).Returns("12.4.0");
        _system.Setup(s => s.GetSystemStorageInfo()).Returns(Storage());
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Admin });
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private static FolderStorageInfo Folder(string path, long free = 10, long used = 5)
        => new() { Path = path, ResolvedPath = path, FreeSpace = free, UsedSpace = used };

    private static SystemStorageInfo Storage(long programDataFree = 10)
        => new()
        {
            ProgramDataFolder = Folder("/config", programDataFree, programDataFree < 0 ? -1 : 5),
            WebFolder = Folder("/web"),
            ImageCacheFolder = Folder("/cache/images"),
            CacheFolder = Folder("/cache"),
            LogFolder = Folder("/config/log"),
            InternalMetadataFolder = Folder("/config/metadata"),
            TranscodingTempFolder = Folder("/cache/transcodes"),
            Libraries = [new LibraryStorageInfo { Id = Guid.NewGuid(), Name = "Movies", Folders = [Folder("/media/movies", 100, 50)] }],
        };

    private SessionInfo AddSession(string id, Guid userId, bool active = true)
    {
        var controller = new Mock<ISessionController>();
        controller.SetupGet(c => c.IsSessionActive).Returns(active);
        var session = new SessionInfo(_sessionManager.Object, NullLogger.Instance)
        {
            Id = id,
            UserId = userId,
            SessionControllers = [controller.Object],
            PlayState = new PlayerStateInfo(),
        };
        _sessions.Add(session);
        return session;
    }

    private static Mock<IScheduledTaskWorker> Worker(TaskState state)
    {
        var worker = new Mock<IScheduledTaskWorker>();
        worker.SetupGet(w => w.State).Returns(state);
        return worker;
    }

    private ServerStatusTool Status() => new(_users.Object, _settings.Object, _host.Object, _system.Object, _sessionManager.Object, _tasks.Object);

    [Fact]
    public async Task ServerStatus_ReportsHostSessionsTasksAndStorage()
    {
        _host.SetupGet(h => h.HasPendingRestart).Returns(true);
        AddSession("a", _user.Id);
        AddSession("b", Guid.NewGuid());
        AddSession("c", _user.Id, active: false);
        _tasks.SetupGet(t => t.ScheduledTasks).Returns(new[] { Worker(TaskState.Running).Object, Worker(TaskState.Idle).Object });
        var tool = Status();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var root = doc.RootElement;
        Assert.Equal("Test", root.GetProperty("serverName").GetString());
        Assert.Equal("12.4.0", root.GetProperty("version").GetString());
        Assert.True(root.GetProperty("hasPendingRestart").GetBoolean());
        Assert.Equal(2, root.GetProperty("activeSessionCount").GetInt32());
        Assert.Equal(1, root.GetProperty("runningTaskCount").GetInt32());
        var folders = root.GetProperty("storage").GetProperty("folders").EnumerateArray().ToList();
        Assert.Equal(
            new[] { "program_data", "web", "image_cache", "cache", "log", "internal_metadata", "transcoding_temp" },
            folders.Select(f => f.GetProperty("name").GetString()));
        Assert.Equal("/config", folders[0].GetProperty("path").GetString());
        Assert.Equal(10, folders[0].GetProperty("freeBytes").GetInt64());
        Assert.Equal(5, folders[0].GetProperty("usedBytes").GetInt64());
        var library = Assert.Single(root.GetProperty("storage").GetProperty("libraries").EnumerateArray());
        Assert.Equal("Movies", library.GetProperty("name").GetString());
        Assert.Equal(100, Assert.Single(library.GetProperty("folders").EnumerateArray()).GetProperty("freeBytes").GetInt64());
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task ServerStatus_UnreadableDrive_OmitsBytes()
    {
        _system.Setup(s => s.GetSystemStorageInfo()).Returns(Storage(programDataFree: -1));
        var tool = Status();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var programData = doc.RootElement.GetProperty("storage").GetProperty("folders")[0];
        Assert.False(programData.TryGetProperty("freeBytes", out _));
        Assert.False(programData.TryGetProperty("usedBytes", out _));
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public void ServerStatus_Annotations_ReadOnlyAdmin()
    {
        var tool = Status();

        Assert.Equal(McpScopes.Admin, tool.RequiredScope);
        Assert.True(tool.Annotations.ReadOnly);
        Assert.False(tool.Annotations.Destructive);
        Assert.True(tool.Annotations.Idempotent);
    }

    private ListAllSessionsTool AllSessions() => new(_users.Object, _settings.Object, _sessionManager.Object, new ItemLookup(_library.Object, new Mock<IUserViewManager>().Object));

    private ActivityLogTool Activity() => new(_users.Object, _settings.Object, _activity.Object);

    private IMcpTool[] AdminViews() => [Status(), AllSessions(), Activity()];

    [Fact]
    public void AdminViews_HiddenWithoutAdminScope()
    {
        var tools = AdminViews();
        var registry = new ToolRegistry(tools);
        var nonAdmin = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Library, McpScopes.Playback, McpScopes.Manage });

        Assert.Empty(registry.ListFor(nonAdmin));
        Assert.All(tools, t => Assert.Null(registry.Find(t.Name, nonAdmin)));
        Assert.Equal(tools.Length, registry.ListFor(_principal).Count);
    }

    private SessionInfo NamedSession(string id, string user, DateTime lastActivity)
    {
        var session = AddSession(id, Guid.NewGuid());
        session.UserName = user;
        session.DeviceName = "TV " + id;
        session.Client = "Jellyfin Android TV";
        session.LastActivityDate = lastActivity;
        return session;
    }

    [Fact]
    public async Task AllSessions_ListsEveryUsersActiveSessions()
    {
        NamedSession("old", "alice", new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc));
        NamedSession("new", "bob", new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
        AddSession("gone", Guid.NewGuid(), active: false);
        var tool = AllSessions();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "new", "old" }, items.Select(i => i.GetProperty("sessionId").GetString()));
        Assert.Equal("bob", items[0].GetProperty("userName").GetString());
        Assert.Equal("TV new", items[0].GetProperty("deviceName").GetString());
        Assert.Equal("Jellyfin Android TV", items[0].GetProperty("client").GetString());
        Assert.StartsWith("2026-11-07T12:00:00", items[0].GetProperty("lastActivity").GetString(), StringComparison.Ordinal);
        Assert.False(items[0].TryGetProperty("nowPlaying", out _));
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task AllSessions_TranscodeDetails()
    {
        var session = NamedSession("a", "alice", DateTime.UtcNow);
        session.PlayState.PlayMethod = PlayMethod.Transcode;
        session.TranscodingInfo = new TranscodingInfo
        {
            VideoCodec = "h264",
            AudioCodec = "aac",
            IsVideoDirect = false,
            IsAudioDirect = true,
            Bitrate = 4000000,
            TranscodeReasons = TranscodeReason.AudioCodecNotSupported | TranscodeReason.ContainerNotSupported,
        };
        var tool = AllSessions();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("transcode", item.GetProperty("playMethod").GetString());
        var transcoding = item.GetProperty("transcoding");
        Assert.Equal("h264", transcoding.GetProperty("videoCodec").GetString());
        Assert.False(transcoding.GetProperty("isVideoDirect").GetBoolean());
        Assert.True(transcoding.GetProperty("isAudioDirect").GetBoolean());
        Assert.Equal(4000000, transcoding.GetProperty("bitrate").GetInt32());
        Assert.Equal(new[] { "container_not_supported", "audio_codec_not_supported" }, transcoding.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task AllSessions_VisibleNowPlaying_ShowsSummary()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Night Signal", ProductionYear = 2019 };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        var session = NamedSession("a", "alice", DateTime.UtcNow);
        session.FullNowPlayingItem = movie;
        session.PlayState.PositionTicks = 3 * TimeSpan.TicksPerMinute;
        var tool = AllSessions();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(2019, item.GetProperty("nowPlaying").GetProperty("year").GetInt32());
        Assert.Equal(3, item.GetProperty("positionMinutes").GetDouble());
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task AllSessions_InvisibleNowPlaying_ShowsIdAndName()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Private", ProductionYear = 2020 };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns((BaseItem?)null);
        var session = NamedSession("a", "alice", DateTime.UtcNow);
        session.FullNowPlayingItem = movie;
        session.NowPlayingItem = new BaseItemDto { Id = movie.Id, Name = "Private" };
        var tool = AllSessions();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var nowPlaying = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("nowPlaying");
        Assert.Equal(movie.Id.ToString("N"), nowPlaying.GetProperty("id").GetString());
        Assert.Equal("Private", nowPlaying.GetProperty("name").GetString());
        Assert.False(nowPlaying.TryGetProperty("year", out _));
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task AllSessions_DoesNotExposeRemoteEndPoint()
    {
        NamedSession("a", "alice", DateTime.UtcNow).RemoteEndPoint = "10.0.0.5";

        var result = await AllSessions().ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        Assert.DoesNotContain("10.0.0.5", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllSessions_Pages()
    {
        NamedSession("old", "alice", new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc));
        NamedSession("new", "bob", new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));

        var result = await AllSessions().ExecuteAsync(Args("""{"limit":1,"startIndex":1}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("old", Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("sessionId").GetString());
    }

    private List<ActivityLogQuery> ActivityReturns(int total, params ActivityLogEntry[] entries)
    {
        var queries = new List<ActivityLogQuery>();
        _activity.Setup(a => a.GetPagedResultAsync(It.IsAny<ActivityLogQuery>()))
            .Callback<ActivityLogQuery>(queries.Add)
            .ReturnsAsync(new QueryResult<ActivityLogEntry>(0, total, entries));
        return queries;
    }

    [Fact]
    public async Task ActivityLog_PassesPagingAndFilters()
    {
        var queries = ActivityReturns(0);

        await Activity().ExecuteAsync(Args("""{"since":"2026-11-01T00:00:00Z","severity":"warning","limit":10,"startIndex":5}"""), _principal, CancellationToken.None);

        var q = Assert.Single(queries);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), q.MinDate);
        Assert.Equal(LogLevel.Warning, q.Severity);
        Assert.Equal(5, q.Skip);
        Assert.Equal(10, q.Limit);
        Assert.Null(q.OrderBy);
    }

    [Fact]
    public async Task ActivityLog_SinceWithOffset_QueriesUtc()
    {
        var queries = ActivityReturns(0);

        await Activity().ExecuteAsync(Args("""{"since":"2026-11-01T02:00:00+02:00"}"""), _principal, CancellationToken.None);

        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), Assert.Single(queries).MinDate);
    }

    [Fact]
    public async Task ActivityLog_DefaultsToFirstPageWithoutFilters()
    {
        var queries = ActivityReturns(0);

        await Activity().ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        var q = Assert.Single(queries);
        Assert.Null(q.MinDate);
        Assert.Null(q.Severity);
        Assert.Equal(0, q.Skip);
        Assert.Equal(20, q.Limit);
    }

    [Fact]
    public async Task ActivityLog_MapsEntries()
    {
        var itemId = Guid.NewGuid().ToString("N");
        ActivityReturns(
            42,
            new ActivityLogEntry("tester is playing Night Signal", "VideoPlayback", _user.Id)
            {
                Id = 7,
                Date = new DateTime(2026, 11, 7, 9, 30, 0, DateTimeKind.Unspecified),
                Severity = LogLevel.Information,
                ShortOverview = "On TV",
                ItemId = itemId,
            });
        var tool = Activity();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(42, doc.RootElement.GetProperty("totalCount").GetInt32());
        var entry = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(7, entry.GetProperty("id").GetInt64());
        Assert.StartsWith("2026-11-07T09:30:00", entry.GetProperty("date").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", entry.GetProperty("date").GetString(), StringComparison.Ordinal);
        Assert.Equal("VideoPlayback", entry.GetProperty("type").GetString());
        Assert.Equal("information", entry.GetProperty("severity").GetString());
        Assert.Equal("On TV", entry.GetProperty("shortOverview").GetString());
        Assert.False(entry.TryGetProperty("overview", out _));
        Assert.Equal("tester", entry.GetProperty("userName").GetString());
        Assert.Equal(itemId, entry.GetProperty("itemId").GetString());
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task ActivityLog_SystemAndDeletedUser_OmitUserName()
    {
        ActivityReturns(
            2,
            new ActivityLogEntry("Scan finished", "TaskCompleted", Guid.Empty) { Id = 1, Severity = LogLevel.Information },
            new ActivityLogEntry("gone signed in", "AuthenticationSucceeded", Guid.NewGuid()) { Id = 2, Severity = LogLevel.Error });
        var tool = Activity();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.False(i.TryGetProperty("userName", out _)));
        Assert.Equal("error", items[1].GetProperty("severity").GetString());
        SchemaAssert.Conforms(tool, result);
    }

    [Theory]
    [InlineData("""{"since":"yesterday"}""", "'since'")]
    [InlineData("""{"severity":"fatal"}""", "'severity'")]
    [InlineData("""{"limit":"x"}""", "'limit'")]
    public async Task ActivityLog_MalformedArguments_NameTheArgument(string json, string expected)
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Activity().ExecuteAsync(Args(json), _principal, CancellationToken.None));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        _activity.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivityLog_Empty_Conforms()
    {
        ActivityReturns(0);
        var tool = Activity();

        var result = await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        Assert.Equal("""{"totalCount":0,"items":[]}""", result.Text);
        SchemaAssert.Conforms(tool, result);
    }
}
