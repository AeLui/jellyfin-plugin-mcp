using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Base class for tools that need the <c>playback</c> scope.
/// </summary>
public abstract class PlaybackToolBase : JellyfinToolBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackToolBase"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessions">The session resolver.</param>
    /// <param name="sessionManager">The session manager.</param>
    protected PlaybackToolBase(IUserManager userManager, IPluginSettings settings, SessionResolver sessions, ISessionManager sessionManager)
        : base(userManager, settings)
    {
        Sessions = sessions;
        SessionManager = sessionManager;
    }

    /// <inheritdoc />
    public sealed override string RequiredScope => McpScopes.Playback;

    /// <summary>
    /// Gets the controlling-session id passed to the session manager.
    /// </summary>
    /// <remarks>
    /// <c>SessionManager</c> only looks up and permission-checks a controlling session when this id is non-empty;
    /// an empty id is the server-originated (API key) case. Ownership has already been enforced by
    /// <see cref="SessionResolver"/>, so no controlling session is needed and none is invented.
    /// </remarks>
    protected static string ControllingSessionId => string.Empty;

    /// <summary>
    /// Gets the session resolver.
    /// </summary>
    protected SessionResolver Sessions { get; }

    /// <summary>
    /// Gets the session manager.
    /// </summary>
    protected ISessionManager SessionManager { get; }

    /// <summary>
    /// Runs a session-manager send and maps server failures to messages that do not leak the server's text.
    /// </summary>
    /// <param name="send">The send operation.</param>
    /// <param name="isPlay">Whether this is a play command, whose argument failures mean play access was refused.</param>
    /// <returns>A task that completes when the command was sent.</returns>
    protected static async Task SendAsync(Func<Task> send, bool isPlay = false)
    {
        ArgumentNullException.ThrowIfNull(send);
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (ResourceNotFoundException)
        {
            throw new ToolException("Session not found");
        }
        catch (ArgumentException) when (isPlay)
        {
            throw new ToolException("Playback was refused by the server");
        }
        catch (ArgumentException)
        {
            throw new ToolException("Session not found");
        }
    }
}
