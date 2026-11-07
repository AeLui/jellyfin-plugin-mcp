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
using Jellyfin.Plugin.Mcp.Tools.Admin;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Tasks;
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
            PlayState = new MediaBrowser.Model.Session.PlayerStateInfo(),
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

    private IMcpTool[] AdminViews() => [Status()];

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
}
