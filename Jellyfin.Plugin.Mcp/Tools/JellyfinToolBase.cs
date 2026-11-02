using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Base class for tools that act on Jellyfin as the user that owns the token.
/// </summary>
public abstract class JellyfinToolBase : IMcpTool
{
    private const int DefaultMaxPageSize = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinToolBase"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    protected JellyfinToolBase(IUserManager userManager)
    {
        UserManager = userManager;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract string RequiredScope { get; }

    /// <inheritdoc />
    public abstract JsonElement InputSchema { get; }

    /// <inheritdoc />
    public abstract ToolAnnotations Annotations { get; }

    /// <summary>
    /// Gets the configured maximum page size, never below 1.
    /// </summary>
    protected static int MaxPageSize => Math.Max(1, Plugin.Instance?.Configuration.MaxPageSize ?? DefaultMaxPageSize);

    /// <summary>
    /// Gets the user manager.
    /// </summary>
    protected IUserManager UserManager { get; }

    /// <inheritdoc />
    public abstract Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct);

    /// <summary>
    /// Resolves the Jellyfin user of the caller.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <returns>The user.</returns>
    /// <exception cref="ToolException">The user no longer exists.</exception>
    protected User GetUser(McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return UserManager.GetUserById(principal.UserId) ?? throw new ToolException("User not found");
    }
}
