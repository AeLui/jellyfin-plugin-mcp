using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Admin;

/// <summary>
/// Lists the server's users with a minimal set of fields.
/// </summary>
public sealed class ListUsersTool : AdminToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{}}""").RootElement.Clone();

    /// <summary>
    /// Initializes a new instance of the <see cref="ListUsersTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    public ListUsersTool(IUserManager userManager, IPluginSettings settings)
        : base(userManager, settings)
    {
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_list_users";

    /// <inheritdoc />
    public override string Description => "List the server's users: id, name, administrator and disabled flags, and last activity and login dates. Read-only.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        // Only these fields are exposed: no password, policy or authentication-provider details.
        var users = UserManager.GetUsers()
            .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(Map)
            .ToList();
        return Task.FromResult(ToolResult.Json(users));
    }

    private static Dictionary<string, object> Map(User user)
    {
        var result = new Dictionary<string, object>
        {
            ["id"] = user.Id,
            ["name"] = user.Username,
            ["isAdministrator"] = user.HasPermission(PermissionKind.IsAdministrator),
            ["isDisabled"] = user.HasPermission(PermissionKind.IsDisabled),
        };
        if (ItemMapper.Utc(user.LastActivityDate) is { } activity)
        {
            result["lastActivityDate"] = activity;
        }

        if (ItemMapper.Utc(user.LastLoginDate) is { } login)
        {
            result["lastLoginDate"] = login;
        }

        return result;
    }
}
