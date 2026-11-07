using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Admin;

/// <summary>
/// Lists the active sessions of all users, read-only.
/// </summary>
public sealed class ListAllSessionsTool : AdminToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"limit":{"type":"integer","minimum":1},"startIndex":{"type":"integer","minimum":0}}}""").RootElement.Clone();

    private static readonly JsonElement SessionsSchema = OutputSchemas.Object(
        "\"totalCount\":{\"type\":\"integer\"},\"items\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
            "\"sessionId\":{\"type\":\"string\"},\"userName\":{\"type\":\"string\"},\"deviceName\":{\"type\":\"string\"},\"client\":{\"type\":\"string\"},"
                + "\"lastActivity\":{\"type\":\"string\",\"description\":\"ISO 8601 UTC timestamp.\"},"
                + "\"nowPlaying\":" + SessionMapper.NowPlayingSchema + ","
                + "\"isPaused\":{\"type\":\"boolean\"},\"positionMinutes\":{\"type\":\"number\"},"
                + "\"playMethod\":{\"type\":\"string\",\"enum\":[\"transcode\",\"direct_stream\",\"direct_play\"]},"
                + "\"transcoding\":" + OutputSchemas.ObjectJson(
                    "\"videoCodec\":{\"type\":\"string\"},\"audioCodec\":{\"type\":\"string\"},\"isVideoDirect\":{\"type\":\"boolean\"},"
                        + "\"isAudioDirect\":{\"type\":\"boolean\"},\"bitrate\":{\"type\":\"integer\"},"
                        + "\"reasons\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}",
                    "isVideoDirect",
                    "isAudioDirect",
                    "reasons"),
            "sessionId",
            "lastActivity",
            "isPaused",
            "positionMinutes")),
        "totalCount",
        "items");

    private readonly ISessionManager _sessionManager;
    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListAllSessionsTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public ListAllSessionsTool(IUserManager userManager, IPluginSettings settings, ISessionManager sessionManager, ItemLookup lookup)
        : base(userManager, settings)
    {
        _sessionManager = sessionManager;
        _lookup = lookup;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_list_all_sessions";

    /// <inheritdoc />
    public override string Title => "List all sessions";

    /// <inheritdoc />
    public override string Description => "List the active sessions of all users with what they play and whether it is transcoded (administrators only, read-only).";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => SessionsSchema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var user = GetScopedUser(principal);
        var active = _sessionManager.Sessions
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.LastActivityDate)
            .ToList();
        var items = active.Skip(startIndex).Take(limit).Select(s => Map(s, user)).ToList();
        return Task.FromResult(ToolResult.Json(new { totalCount = active.Count, items }));
    }

    // The client IP (RemoteEndPoint) is deliberately not returned.
    private Dictionary<string, object> Map(SessionInfo session, ScopedUser user)
    {
        var result = new Dictionary<string, object> { ["sessionId"] = session.Id };
        AddText(result, "userName", session.UserName);
        AddText(result, "deviceName", session.DeviceName);
        AddText(result, "client", session.Client);
        result["lastActivity"] = ItemMapper.Utc(session.LastActivityDate)!;

        // The full summary only for items the calling administrator can see; otherwise just the id and name.
        if (SessionMapper.NowPlaying(session, item => _lookup.FindVisible(user, item.Id) is not null) is { } nowPlaying)
        {
            result["nowPlaying"] = nowPlaying;
        }

        result["isPaused"] = session.PlayState?.IsPaused ?? false;
        result["positionMinutes"] = Math.Round((double)(session.PlayState?.PositionTicks ?? 0) / TimeSpan.TicksPerMinute, 1);
        if (session.PlayState?.PlayMethod is { } method)
        {
            result["playMethod"] = SnakeCase.From(method.ToString());
        }

        if (session.TranscodingInfo is { } transcoding)
        {
            result["transcoding"] = Transcoding(transcoding);
        }

        return result;
    }

    private static Dictionary<string, object> Transcoding(TranscodingInfo info)
    {
        var result = new Dictionary<string, object>();
        AddText(result, "videoCodec", info.VideoCodec);
        AddText(result, "audioCodec", info.AudioCodec);
        result["isVideoDirect"] = info.IsVideoDirect;
        result["isAudioDirect"] = info.IsAudioDirect;
        if (info.Bitrate is { } bitrate)
        {
            result["bitrate"] = bitrate;
        }

        result["reasons"] = Enum.GetValues<TranscodeReason>()
            .Where(r => r != 0 && info.TranscodeReasons.HasFlag(r))
            .Select(r => SnakeCase.From(r.ToString()))
            .ToList();
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
