using System;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Manage;

/// <summary>
/// Base class for tools that need the <c>manage</c> scope.
/// </summary>
public abstract class ManageToolBase : JellyfinToolBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManageToolBase"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    protected ManageToolBase(IUserManager userManager, IPluginSettings settings)
        : base(userManager, settings)
    {
    }

    /// <inheritdoc />
    public sealed override string RequiredScope => McpScopes.Manage;

    /// <summary>
    /// Runs a server operation and maps its failures to a fixed message, so server text never reaches the client.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="failureMessage">The message returned when the server refuses or fails the operation.</param>
    /// <returns>A task that completes when the operation finished.</returns>
    protected static async Task RunAsync(Func<Task> operation, string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or ResourceNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new ToolException(failureMessage);
        }
    }
}
