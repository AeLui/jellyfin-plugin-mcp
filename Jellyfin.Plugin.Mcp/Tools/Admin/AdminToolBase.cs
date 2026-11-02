using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Mcp.Tools.Admin;

/// <summary>
/// Base class for tools that need the <c>admin</c> scope.
/// </summary>
public abstract class AdminToolBase : JellyfinToolBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdminToolBase"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    protected AdminToolBase(IUserManager userManager, IPluginSettings settings)
        : base(userManager, settings)
    {
    }

    /// <inheritdoc />
    public sealed override string RequiredScope => McpScopes.Admin;

    /// <summary>
    /// Starts a scheduled task without waiting for it, as the server's own API does.
    /// </summary>
    /// <param name="taskManager">The task manager.</param>
    /// <param name="worker">The task worker.</param>
    /// <exception cref="ToolException">The task is already running.</exception>
    protected static void StartTask(ITaskManager taskManager, IScheduledTaskWorker worker)
    {
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentNullException.ThrowIfNull(worker);
        if (worker.State != TaskState.Idle)
        {
            throw new ToolException("Task is already running");
        }

        // Execute completes only when the task ends, so it must not be awaited. The task records its own failures
        // in its last result; observing the fault here keeps it from going unobserved.
        var running = taskManager.Execute(worker, new TaskOptions());
        _ = running.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Finds a task worker by id or key.
    /// </summary>
    /// <param name="workers">The workers.</param>
    /// <param name="id">The task id, if given.</param>
    /// <param name="key">The task key, if given.</param>
    /// <returns>The worker, or <c>null</c> when none matches.</returns>
    protected static IScheduledTaskWorker? FindTask(IEnumerable<IScheduledTaskWorker> workers, string? id, string? key)
        => workers.FirstOrDefault(w =>
            (id is not null && string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase))
            || (key is not null && string.Equals(w.ScheduledTask?.Key, key, StringComparison.OrdinalIgnoreCase)));
}
