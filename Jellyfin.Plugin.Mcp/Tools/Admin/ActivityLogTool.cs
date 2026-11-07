using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Queries;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mcp.Tools.Admin;

/// <summary>
/// Reads the server activity log, newest first.
/// </summary>
public sealed class ActivityLogTool : AdminToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"since":{"type":"string","description":"Only entries from this ISO 8601 date-time on (UTC when no offset is given)."},"severity":{"type":"string","enum":["information","warning","error"],"description":"Only entries of exactly this severity."},"limit":{"type":"integer","minimum":1},"startIndex":{"type":"integer","minimum":0}}}""").RootElement.Clone();

    private static readonly JsonElement LogSchema = OutputSchemas.Object(
        "\"totalCount\":{\"type\":\"integer\"},\"items\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
            "\"id\":{\"type\":\"integer\"},\"date\":{\"type\":\"string\",\"description\":\"ISO 8601 UTC timestamp.\"},"
                + "\"name\":{\"type\":\"string\"},\"type\":{\"type\":\"string\"},"
                + "\"severity\":{\"type\":\"string\",\"description\":\"For example information, warning or error.\"},"
                + "\"shortOverview\":{\"type\":\"string\"},\"overview\":{\"type\":\"string\"},"
                + "\"userName\":{\"type\":\"string\"},\"itemId\":{\"type\":\"string\"}",
            "id",
            "date",
            "name",
            "type",
            "severity")),
        "totalCount",
        "items");

    private readonly IActivityManager _activityManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLogTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="activityManager">The activity manager.</param>
    public ActivityLogTool(IUserManager userManager, IPluginSettings settings, IActivityManager activityManager)
        : base(userManager, settings)
    {
        _activityManager = activityManager;
    }

    private enum Severity
    {
        Information,
        Warning,
        Error,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_activity_log";

    /// <inheritdoc />
    public override string Title => "Activity log";

    /// <inheritdoc />
    public override string Description => "Read the server activity log (sign-ins, playback, errors), newest first; administrators only, read-only.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => LogSchema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var since = args.GetUtcDateTime("since");
        var severity = args.GetEnum<Severity>("severity");
        var (startIndex, limit) = args.GetPaging(MaxPageSize);

        // Without OrderBy, v12.4 ActivityManager sorts by date descending; Severity is an exact match.
        var result = await _activityManager.GetPagedResultAsync(new ActivityLogQuery
        {
            MinDate = since,
            Severity = severity switch
            {
                Severity.Information => LogLevel.Information,
                Severity.Warning => LogLevel.Warning,
                Severity.Error => LogLevel.Error,
                _ => null,
            },
            Skip = startIndex,
            Limit = limit,
        }).ConfigureAwait(false);
        var items = result.Items.Select(Map).ToList();
        return ToolResult.Json(new { totalCount = result.TotalRecordCount, items });
    }

    private Dictionary<string, object> Map(ActivityLogEntry entry)
    {
        var result = new Dictionary<string, object>
        {
            ["id"] = entry.Id,
            ["date"] = ItemMapper.Utc(entry.Date)!,
            ["name"] = entry.Name ?? string.Empty,
            ["type"] = entry.Type ?? string.Empty,
            ["severity"] = SnakeCase.From(entry.Severity.ToString()),
        };
        AddText(result, "shortOverview", entry.ShortOverview);
        AddText(result, "overview", entry.Overview);
        if (entry.UserId != Guid.Empty)
        {
            AddText(result, "userName", UserManager.GetUserById(entry.UserId)?.Username);
        }

        AddText(result, "itemId", entry.ItemId);
        return result;
    }

    private static void AddText(Dictionary<string, object> target, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            target[key] = value;
        }
    }
}
