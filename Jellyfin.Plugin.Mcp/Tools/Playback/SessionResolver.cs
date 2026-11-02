using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Finds the sessions a token may control.
/// </summary>
/// <remarks>
/// A session is controllable when it is active, supports remote control and either belongs to the token's user
/// or the token carries the <c>admin</c> scope and its Jellyfin user may remote control other users
/// (<c>EnableRemoteControlOfOtherUsers</c>). The scope, not the user's admin flag, decides the first part, because
/// the authenticator already drops the scope for users that are not administrators. The permission is checked here
/// because commands are sent without a controlling session, which makes the session manager skip its own check.
/// </remarks>
public sealed class SessionResolver
{
    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionResolver"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="userManager">The user manager.</param>
    public SessionResolver(ISessionManager sessionManager, IUserManager userManager)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
    }

    /// <summary>
    /// Lists the sessions the caller may control.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <returns>The sessions.</returns>
    public IReadOnlyList<SessionInfo> Controllable(McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var canControlOthers = principal.Scopes.Contains(McpScopes.Admin)
            && _userManager.GetUserById(principal.UserId) is { } user
            && user.HasPermission(PermissionKind.EnableRemoteControlOfOtherUsers);
        return _sessionManager.Sessions
            .Where(s => s.IsActive && s.SupportsRemoteControl && (canControlOthers || s.UserId == principal.UserId))
            .ToList();
    }

    /// <summary>
    /// Finds one controllable session.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="sessionId">The session id.</param>
    /// <returns>The session.</returns>
    /// <exception cref="ToolException">The session is unknown or not controllable by the caller; both look the same.</exception>
    public SessionInfo Resolve(McpPrincipal principal, string sessionId)
        => Controllable(principal).FirstOrDefault(s => string.Equals(s.Id, sessionId, StringComparison.Ordinal))
            ?? throw new ToolException("Session not found");
}
