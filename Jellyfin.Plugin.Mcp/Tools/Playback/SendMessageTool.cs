using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Shows a message on a session's screen.
/// </summary>
public sealed class SendMessageTool : PlaybackToolBase
{
    private const int MaxTextLength = 500;
    private const int DefaultTimeoutSeconds = 10;
    private const int MaxTimeoutSeconds = 3600;

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"sessionId":{"type":"string","description":"Session id from jellyfin_list_sessions."},"text":{"type":"string","minLength":1,"maxLength":500},"header":{"type":"string"},"timeoutSeconds":{"type":"integer","minimum":1,"description":"How long the message stays visible. Default 10."}},"required":["sessionId","text"]}""").RootElement.Clone();

    /// <summary>
    /// Initializes a new instance of the <see cref="SendMessageTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessions">The session resolver.</param>
    /// <param name="sessionManager">The session manager.</param>
    public SendMessageTool(IUserManager userManager, IPluginSettings settings, SessionResolver sessions, ISessionManager sessionManager)
        : base(userManager, settings, sessions, sessionManager)
    {
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_send_message";

    /// <inheritdoc />
    public override string Title => "Send message to device";

    /// <inheritdoc />
    public override string Description => "Show a message on the screen of one of your sessions.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, false, false);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var sessionId = args.GetRequiredString("sessionId");
        var text = args.GetRequiredString("text");
        if (text.Length > MaxTextLength)
        {
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"Invalid argument 'text': at most {MaxTextLength} characters"));
        }

        var timeout = args.GetInt("timeoutSeconds") ?? DefaultTimeoutSeconds;
        if (timeout is < 1 or > MaxTimeoutSeconds)
        {
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"Invalid argument 'timeoutSeconds': must be between 1 and {MaxTimeoutSeconds}"));
        }

        var session = Sessions.Resolve(principal, sessionId);
        var command = new MessageCommand
        {
            Header = args.GetString("header") ?? string.Empty,
            Text = text,
            TimeoutMs = timeout * 1000L,
        };
        await SendAsync(() => SessionManager.SendMessageCommand(ControllingSessionId, session.Id, command, ct)).ConfigureAwait(false);
        return ToolResult.Json(new { sessionId = session.Id, sent = true });
    }
}
