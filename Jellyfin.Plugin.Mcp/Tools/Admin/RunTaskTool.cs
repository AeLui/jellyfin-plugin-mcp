using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Mcp.Tools.Admin;

/// <summary>
/// Starts a scheduled task.
/// </summary>
public sealed class RunTaskTool : AdminToolBase
{
    // Installs third-party plugin code, so it is never started through MCP.
    private const string PluginUpdatesKey = "PluginUpdates";

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"taskId":{"type":"string","description":"Task id, from jellyfin_list_tasks."},"key":{"type":"string","description":"Task key, from jellyfin_list_tasks. Used when taskId is not given."}}}""").RootElement.Clone();

    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunTaskTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="taskManager">The task manager.</param>
    public RunTaskTool(IUserManager userManager, IPluginSettings settings, ITaskManager taskManager)
        : base(userManager, settings)
    {
        _taskManager = taskManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_run_task";

    /// <inheritdoc />
    public override string Title => "Run scheduled task";

    /// <inheritdoc />
    public override string Description => "Start a scheduled task by taskId or key. The task runs in the background; use jellyfin_list_tasks to follow its progress. Some tasks delete cache, log or transcode files or clean up the database, and the plugin update task cannot be run.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, true, false);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var id = NullIfBlank(args.GetString("taskId"));
        var key = NullIfBlank(args.GetString("key"));
        if (id is null && key is null)
        {
            throw new ToolException("Invalid argument 'taskId': taskId or key is required");
        }

        var worker = FindTask(_taskManager.ScheduledTasks, id, key) ?? throw new ToolException("Task not found");
        if (string.Equals(worker.ScheduledTask?.Key, PluginUpdatesKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolException("This task cannot be run via MCP");
        }

        StartTask(_taskManager, worker);
        return Task.FromResult(ToolResult.Json(new Dictionary<string, object> { ["started"] = true }));
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
