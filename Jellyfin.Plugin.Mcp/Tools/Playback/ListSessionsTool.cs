using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Lists the sessions the caller can control.
/// </summary>
public sealed class ListSessionsTool : PlaybackToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private static readonly JsonElement SessionsSchema = OutputSchemas.Object(
        "\"items\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
            "\"sessionId\":{\"type\":\"string\"},\"deviceName\":{\"type\":\"string\"},\"client\":{\"type\":\"string\"},"
                + "\"userName\":{\"type\":\"string\"},\"nowPlaying\":{\"anyOf\":[" + OutputSchemas.ItemSummary + ","
                + OutputSchemas.ObjectJson("\"id\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"}", "id") + "]},"
                + "\"isPaused\":{\"type\":\"boolean\"},\"positionMinutes\":{\"type\":\"number\"}",
            "sessionId",
            "isPaused",
            "positionMinutes")),
        "items");

    /// <summary>
    /// Initializes a new instance of the <see cref="ListSessionsTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessions">The session resolver.</param>
    /// <param name="sessionManager">The session manager.</param>
    public ListSessionsTool(IUserManager userManager, IPluginSettings settings, SessionResolver sessions, ISessionManager sessionManager)
        : base(userManager, settings, sessions, sessionManager)
    {
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_list_sessions";

    /// <inheritdoc />
    public override string Title => "List devices";

    /// <inheritdoc />
    public override string Description => "List the active player sessions you can control, with what each is playing.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => SessionsSchema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var result = Sessions.Controllable(principal).Select(s => new
        {
            sessionId = s.Id,
            deviceName = s.DeviceName,
            client = s.Client,
            userName = s.UserName,
            nowPlaying = NowPlaying(s),
            isPaused = s.PlayState?.IsPaused ?? false,
            positionMinutes = Math.Round((double)(s.PlayState?.PositionTicks ?? 0) / TimeSpan.TicksPerMinute, 1),
        }).ToList();
        return Task.FromResult(ToolResult.Json(new { items = result }));
    }

    private static object? NowPlaying(SessionInfo session)
    {
        if (session.FullNowPlayingItem is { } item)
        {
            return ItemMapper.Summary(item, null);
        }

        if (session.NowPlayingItem is { } dto)
        {
            // Null dictionary values are serialized (WhenWritingNull only covers properties), so a missing name is left out.
            var playing = new Dictionary<string, object> { ["id"] = dto.Id.ToString("N", CultureInfo.InvariantCulture) };
            if (dto.Name is not null)
            {
                playing["name"] = dto.Name;
            }

            return playing;
        }

        return null;
    }
}
