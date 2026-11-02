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
/// Starts a scan of all libraries.
/// </summary>
public sealed class ScanLibrariesTool : AdminToolBase
{
    // Key of the server's "Scan Media Library" scheduled task (RefreshMediaLibraryTask).
    private const string ScanTaskKey = "RefreshLibrary";

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{}}""").RootElement.Clone();

    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanLibrariesTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="taskManager">The task manager.</param>
    public ScanLibrariesTool(IUserManager userManager, IPluginSettings settings, ITaskManager taskManager)
        : base(userManager, settings)
    {
        _taskManager = taskManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_scan_libraries";

    /// <inheritdoc />
    public override string Description => "Start a scan of all media libraries for new, changed and removed files. The scan runs in the background.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var worker = FindTask(_taskManager.ScheduledTasks, null, ScanTaskKey)
            ?? throw new ToolException("Library scan task not found");

        // A scan that is already running covers this request.
        if (worker.State == TaskState.Idle)
        {
            StartTask(_taskManager, worker);
        }

        return Task.FromResult(ToolResult.Json(new Dictionary<string, object> { ["queued"] = true }));
    }
}
