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
using Jellyfin.Plugin.Mcp.Tools.Admin;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Admin;

public class AdminToolsTests
{
    private readonly User _user = TestUsers.Create(admin: true);
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly Mock<IProviderManager> _providers = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private readonly Mock<ITaskManager> _tasks = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly McpPrincipal _principal;

    public AdminToolsTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(100);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Admin });
        _tasks.Setup(t => t.Execute(It.IsAny<IScheduledTaskWorker>(), It.IsAny<TaskOptions>())).Returns(Task.CompletedTask);
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private static JsonElement Parse(ToolResult result) => JsonDocument.Parse(result.Text).RootElement;

    private static Mock<IScheduledTaskWorker> Worker(string id, string key, TaskState state = TaskState.Idle)
    {
        var task = new Mock<IScheduledTask>();
        task.SetupGet(t => t.Key).Returns(key);
        var worker = new Mock<IScheduledTaskWorker>();
        worker.SetupGet(w => w.Id).Returns(id);
        worker.SetupGet(w => w.Name).Returns("Name " + key);
        worker.SetupGet(w => w.Category).Returns("Maintenance");
        worker.SetupGet(w => w.State).Returns(state);
        worker.SetupGet(w => w.ScheduledTask).Returns(task.Object);
        return worker;
    }

    private void SetTasks(params Mock<IScheduledTaskWorker>[] workers)
        => _tasks.SetupGet(t => t.ScheduledTasks).Returns(workers.Select(w => w.Object).ToList());

    private RefreshItemTool Refresh() => new(_users.Object, _settings.Object, new ItemLookup(_library.Object), _providers.Object, _fileSystem.Object);

    private Movie AddMovie()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        return movie;
    }

    [Fact]
    public void AllAdminTools_RequireAdminScope()
    {
        var types = typeof(AdminToolBase).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && t.Namespace == typeof(AdminToolBase).Namespace && typeof(IMcpTool).IsAssignableFrom(t))
            .ToList();

        Assert.Equal(5, types.Count);
        var tools = new IMcpTool[]
        {
            Refresh(),
            new ScanLibrariesTool(_users.Object, _settings.Object, _tasks.Object),
            new ListTasksTool(_users.Object, _settings.Object, _tasks.Object),
            new RunTaskTool(_users.Object, _settings.Object, _tasks.Object),
            new ListUsersTool(_users.Object, _settings.Object),
        };
        Assert.Equal(types.Select(t => t.Name).Order(), tools.Select(t => t.GetType().Name).Order());
        Assert.All(tools, t => Assert.Equal(McpScopes.Admin, t.RequiredScope));
        Assert.Equal(
            ["jellyfin_list_tasks", "jellyfin_list_users"],
            tools.Where(t => t.Annotations.ReadOnly).Select(t => t.Name).Order());
    }

    [Fact]
    public async Task RefreshItem_ReplaceImages_SetsFlags()
    {
        var movie = AddMovie();
        MetadataRefreshOptions? captured = null;
        _providers.Setup(p => p.QueueRefresh(movie.Id, It.IsAny<MetadataRefreshOptions>(), RefreshPriority.High))
            .Callback<Guid, MetadataRefreshOptions, RefreshPriority>((_, o, _) => captured = o);

        var result = await Refresh().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","replaceImages":true}"""), _principal, CancellationToken.None);

        Assert.True(Parse(result).GetProperty("queued").GetBoolean());
        Assert.NotNull(captured);
        Assert.True(captured.ReplaceAllImages);
        Assert.False(captured.ReplaceAllMetadata);
        Assert.Equal(MetadataRefreshMode.FullRefresh, captured.MetadataRefreshMode);
        Assert.Equal(MetadataRefreshMode.FullRefresh, captured.ImageRefreshMode);
        Assert.True(captured.ForceSave);
        Assert.False(captured.IsAutomated);
    }

    [Fact]
    public async Task RefreshItem_Defaults_UseDefaultMode()
    {
        var movie = AddMovie();
        MetadataRefreshOptions? captured = null;
        _providers.Setup(p => p.QueueRefresh(movie.Id, It.IsAny<MetadataRefreshOptions>(), It.IsAny<RefreshPriority>()))
            .Callback<Guid, MetadataRefreshOptions, RefreshPriority>((_, o, _) => captured = o);

        await Refresh().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(MetadataRefreshMode.Default, captured.MetadataRefreshMode);
        Assert.False(captured.ReplaceAllImages);
        Assert.False(captured.ReplaceAllMetadata);
        Assert.False(captured.ForceSave);
    }

    [Fact]
    public async Task RefreshItem_ItemNotVisibleToUser_IsErrorAndNothingQueued()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Refresh().ExecuteAsync(Args($$"""{"itemId":"{{Guid.NewGuid()}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _providers.Verify(p => p.QueueRefresh(It.IsAny<Guid>(), It.IsAny<MetadataRefreshOptions>(), It.IsAny<RefreshPriority>()), Times.Never);
    }

    [Fact]
    public async Task ScanLibraries_ExecutesScanTask()
    {
        var scan = Worker("id-scan", "RefreshLibrary");
        SetTasks(Worker("id-other", "Other"), scan);

        var result = await new ScanLibrariesTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        Assert.True(Parse(result).GetProperty("queued").GetBoolean());
        _tasks.Verify(t => t.Execute(scan.Object, It.IsAny<TaskOptions>()), Times.Once);
    }

    [Fact]
    public async Task ScanLibraries_AlreadyRunning_DoesNotStartSecondScan()
    {
        var scan = Worker("id-scan", "RefreshLibrary", TaskState.Running);
        SetTasks(scan);

        var result = await new ScanLibrariesTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        Assert.True(Parse(result).GetProperty("queued").GetBoolean());
        _tasks.Verify(t => t.Execute(It.IsAny<IScheduledTaskWorker>(), It.IsAny<TaskOptions>()), Times.Never);
    }

    [Fact]
    public async Task ListTasks_MapsWorkers()
    {
        var worker = Worker("id-1", "KeyOne", TaskState.Running);
        worker.SetupGet(w => w.CurrentProgress).Returns(42.5);
        worker.SetupGet(w => w.LastExecutionResult).Returns(new TaskResult
        {
            Status = TaskCompletionStatus.Completed,
            EndTimeUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified),
            ErrorMessage = "server text",
        });
        SetTasks(worker, Worker("id-2", "KeyTwo"));

        var result = await new ListTasksTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        var items = Parse(result).EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var first = items[0];
        Assert.Equal("id-1", first.GetProperty("id").GetString());
        Assert.Equal("KeyOne", first.GetProperty("key").GetString());
        Assert.Equal("Name KeyOne", first.GetProperty("name").GetString());
        Assert.Equal("Maintenance", first.GetProperty("category").GetString());
        Assert.Equal("Running", first.GetProperty("state").GetString());
        Assert.Equal(42.5, first.GetProperty("progress").GetDouble());
        var last = first.GetProperty("lastResult");
        Assert.Equal("Completed", last.GetProperty("status").GetString());
        Assert.Equal("2026-01-02T03:04:05.0000000Z", last.GetProperty("endTime").GetString());
        Assert.Equal(["endTime", "status"], last.EnumerateObject().Select(p => p.Name).Order());
        Assert.False(items[1].TryGetProperty("lastResult", out _));
        Assert.False(items[1].TryGetProperty("progress", out _));
    }

    [Fact]
    public async Task RunTask_ByKey_ExecutesMatchingWorker()
    {
        var match = Worker("id-1", "MyKey");
        SetTasks(Worker("id-0", "Other"), match);

        var result = await new RunTaskTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("""{"key":"mykey"}"""), _principal, CancellationToken.None);

        Assert.True(Parse(result).GetProperty("started").GetBoolean());
        _tasks.Verify(t => t.Execute(match.Object, It.IsAny<TaskOptions>()), Times.Once);
        _tasks.Verify(t => t.Execute(It.IsNotIn(match.Object), It.IsAny<TaskOptions>()), Times.Never);
    }

    [Fact]
    public async Task RunTask_ById_ExecutesMatchingWorker()
    {
        var match = Worker("ID-1", "MyKey");
        SetTasks(match);

        await new RunTaskTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("""{"taskId":"id-1"}"""), _principal, CancellationToken.None);

        _tasks.Verify(t => t.Execute(match.Object, It.IsAny<TaskOptions>()), Times.Once);
    }

    [Fact]
    public async Task RunTask_Unknown_IsError()
    {
        SetTasks(Worker("id-1", "MyKey"));

        var ex = await Assert.ThrowsAsync<ToolException>(() => new RunTaskTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("""{"key":"nope"}"""), _principal, CancellationToken.None));

        Assert.Equal("Task not found", ex.Message);
        _tasks.Verify(t => t.Execute(It.IsAny<IScheduledTaskWorker>(), It.IsAny<TaskOptions>()), Times.Never);
    }

    [Fact]
    public async Task RunTask_NoIdentifier_NamesArgument()
    {
        SetTasks(Worker("id-1", "MyKey"));

        var ex = await Assert.ThrowsAsync<ToolException>(() => new RunTaskTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("{}"), _principal, CancellationToken.None));

        Assert.Contains("'taskId'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTask_AlreadyRunning_IsErrorWithFixedText()
    {
        SetTasks(Worker("id-1", "MyKey", TaskState.Running));

        var ex = await Assert.ThrowsAsync<ToolException>(() => new RunTaskTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("""{"key":"MyKey"}"""), _principal, CancellationToken.None));

        Assert.Equal("Task is already running", ex.Message);
    }

    [Fact]
    public async Task RunTask_FaultingTask_DoesNotThrowFromTool()
    {
        var match = Worker("id-1", "MyKey");
        SetTasks(match);
        _tasks.Setup(t => t.Execute(match.Object, It.IsAny<TaskOptions>())).Returns(Task.FromException(new InvalidOperationException("server secret")));

        var result = await new RunTaskTool(_users.Object, _settings.Object, _tasks.Object)
            .ExecuteAsync(Args("""{"key":"MyKey"}"""), _principal, CancellationToken.None);

        Assert.True(Parse(result).GetProperty("started").GetBoolean());
    }

    [Fact]
    public async Task ListUsers_ExposesNoPasswordOrPolicyDetails()
    {
        var admin = TestUsers.Create(admin: true);
        admin.LastActivityDate = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Unspecified);
        admin.LastLoginDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        admin.Password = "secret-hash";
        var disabled = TestUsers.Create(disabled: true);
        _users.Setup(u => u.GetUsers()).Returns([disabled, admin]);

        var result = await new ListUsersTool(_users.Object, _settings.Object)
            .ExecuteAsync(Args("{}"), _principal, CancellationToken.None);

        var items = Parse(result).EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var expected = new[] { "id", "isAdministrator", "isDisabled", "lastActivityDate", "lastLoginDate", "name" };
        var adminItem = items.Single(i => i.GetProperty("isAdministrator").GetBoolean());
        Assert.Equal(expected, adminItem.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(admin.Id, adminItem.GetProperty("id").GetGuid());
        Assert.Equal("2026-05-06T07:08:09.0000000Z", adminItem.GetProperty("lastActivityDate").GetString());
        Assert.Equal("2026-05-01T00:00:00.0000000Z", adminItem.GetProperty("lastLoginDate").GetString());
        var disabledItem = items.Single(i => i.GetProperty("isDisabled").GetBoolean());
        Assert.Equal(["id", "isAdministrator", "isDisabled", "name"], disabledItem.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("secret-hash", result.Text, StringComparison.Ordinal);
    }
}
