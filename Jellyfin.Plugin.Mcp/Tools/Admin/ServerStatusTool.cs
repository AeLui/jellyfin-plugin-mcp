using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Mcp.Tools.Admin;

/// <summary>
/// Shows the server's name, version, activity and disk space.
/// </summary>
public sealed class ServerStatusTool : AdminToolBase
{
    private const string FolderProperties = "\"path\":{\"type\":\"string\"},\"freeBytes\":{\"type\":\"integer\"},\"usedBytes\":{\"type\":\"integer\"}";

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{}}""").RootElement.Clone();

    private static readonly JsonElement StatusSchema = OutputSchemas.Object(
        "\"serverName\":{\"type\":\"string\"},\"version\":{\"type\":\"string\"},\"hasPendingRestart\":{\"type\":\"boolean\"},"
            + "\"activeSessionCount\":{\"type\":\"integer\"},\"runningTaskCount\":{\"type\":\"integer\"},"
            + "\"storage\":" + OutputSchemas.ObjectJson(
                "\"folders\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
                    "\"name\":{\"type\":\"string\",\"enum\":[\"program_data\",\"web\",\"image_cache\",\"cache\",\"log\",\"internal_metadata\",\"transcoding_temp\"]},"
                        + FolderProperties,
                    "name",
                    "path")) + ","
                    + "\"libraries\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
                        "\"id\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},\"folders\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(FolderProperties, "path")),
                        "id",
                        "name",
                        "folders")),
                "folders",
                "libraries"),
        "serverName",
        "version",
        "hasPendingRestart",
        "activeSessionCount",
        "runningTaskCount",
        "storage");

    private readonly IServerApplicationHost _host;
    private readonly ISystemManager _systemManager;
    private readonly ISessionManager _sessionManager;
    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerStatusTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="host">The server application host.</param>
    /// <param name="systemManager">The system manager.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="taskManager">The task manager.</param>
    public ServerStatusTool(IUserManager userManager, IPluginSettings settings, IServerApplicationHost host, ISystemManager systemManager, ISessionManager sessionManager, ITaskManager taskManager)
        : base(userManager, settings)
    {
        _host = host;
        _systemManager = systemManager;
        _sessionManager = sessionManager;
        _taskManager = taskManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_server_status";

    /// <inheritdoc />
    public override string Title => "Server status";

    /// <inheritdoc />
    public override string Description => "Show the server name, version, pending restart, active sessions, running tasks and disk space of the server and library folders (administrators only, read-only).";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => StatusSchema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        // ISystemManager.GetSystemInfo needs an HttpRequest, which tools do not have; the host carries the same values.
        var storage = _systemManager.GetSystemStorageInfo();
        var folders = new[]
        {
            Folder("program_data", storage.ProgramDataFolder),
            Folder("web", storage.WebFolder),
            Folder("image_cache", storage.ImageCacheFolder),
            Folder("cache", storage.CacheFolder),
            Folder("log", storage.LogFolder),
            Folder("internal_metadata", storage.InternalMetadataFolder),
            Folder("transcoding_temp", storage.TranscodingTempFolder),
        };
        var libraries = storage.Libraries.Select(l => new
        {
            id = l.Id.ToString("N", CultureInfo.InvariantCulture),
            name = l.Name,
            folders = l.Folders.Select(f => Folder(null, f)).ToList(),
        }).ToList();
        return Task.FromResult(ToolResult.Json(new
        {
            serverName = _host.FriendlyName,
            version = _host.ApplicationVersionString,
            hasPendingRestart = _host.HasPendingRestart,
            activeSessionCount = _sessionManager.Sessions.Count(s => s.IsActive),
            runningTaskCount = _taskManager.ScheduledTasks.Count(t => t.State == TaskState.Running),
            storage = new { folders, libraries },
        }));
    }

    // Jellyfin reports -1 for both sizes when it cannot read the drive (StorageHelper.GetFreeSpaceOf, v12.4); those are left out.
    private static Dictionary<string, object> Folder(string? name, FolderStorageInfo info)
    {
        var result = new Dictionary<string, object>();
        if (name is not null)
        {
            result["name"] = name;
        }

        result["path"] = info.Path;
        if (info.FreeSpace >= 0)
        {
            result["freeBytes"] = info.FreeSpace;
        }

        if (info.UsedSpace >= 0)
        {
            result["usedBytes"] = info.UsedSpace;
        }

        return result;
    }
}
