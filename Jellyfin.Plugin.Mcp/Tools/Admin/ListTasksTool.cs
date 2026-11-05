using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Mcp.Tools.Admin;

/// <summary>
/// Lists the server's scheduled tasks.
/// </summary>
public sealed class ListTasksTool : AdminToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{}}""").RootElement.Clone();

    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListTasksTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="taskManager">The task manager.</param>
    public ListTasksTool(IUserManager userManager, IPluginSettings settings, ITaskManager taskManager)
        : base(userManager, settings)
    {
        _taskManager = taskManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_list_tasks";

    /// <inheritdoc />
    public override string Title => "List scheduled tasks";

    /// <inheritdoc />
    public override string Description => "List the server's scheduled tasks with their state, progress (percent) and last result.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var tasks = _taskManager.ScheduledTasks.Select(Map).ToList();
        return Task.FromResult(ToolResult.Json(tasks));
    }

    private static Dictionary<string, object> Map(IScheduledTaskWorker worker)
    {
        var result = new Dictionary<string, object>();
        Add(result, "id", worker.Id);
        Add(result, "key", worker.ScheduledTask?.Key);
        Add(result, "name", worker.Name);
        Add(result, "category", worker.Category);
        result["state"] = worker.State.ToString();
        Add(result, "progress", worker.CurrentProgress);
        var last = worker.LastExecutionResult;
        if (last is not null)
        {
            var lastResult = new Dictionary<string, object> { ["status"] = last.Status.ToString() };
            Add(lastResult, "endTime", ItemMapper.Utc(last.EndTimeUtc));
            result["lastResult"] = lastResult;
        }

        return result;
    }

    private static void Add(Dictionary<string, object> target, string key, object? value)
    {
        if (value is not null)
        {
            target[key] = value;
        }
    }
}
