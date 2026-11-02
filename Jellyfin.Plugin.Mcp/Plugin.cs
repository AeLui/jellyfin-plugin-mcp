using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.Mcp.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Mcp;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private static readonly object ConfigLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the lock serialising every configuration write, shared by settings saves and token mutations.
    /// </summary>
    internal static object ConfigurationLock => ConfigLock;

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "MCP Server";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("08dd6fbf-c026-48c9-9405-824c3906df09");

    /// <summary>
    /// Replaces the configuration but keeps the current <see cref="PluginConfiguration.Tokens"/>.
    /// Jellyfin's configuration endpoint replaces the whole object, so a stale client must not be able
    /// to resurrect revoked tokens or drop new ones. Tokens are only written by <see cref="SaveConfigurationWithTokens"/>.
    /// </summary>
    /// <param name="configuration">The new configuration.</param>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (ConfigLock)
        {
            if (configuration is PluginConfiguration incoming && !ReferenceEquals(incoming, Configuration))
            {
                incoming.Tokens = new Collection<McpToken>(Configuration.Tokens.ToList());
            }

            base.UpdateConfiguration(configuration);
        }
    }

    /// <summary>
    /// Persists a configuration including its tokens. Used by the token store only.
    /// </summary>
    /// <param name="configuration">The configuration, whose tokens are authoritative.</param>
    public void SaveConfigurationWithTokens(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (ConfigLock)
        {
            base.UpdateConfiguration(configuration);
        }
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "McpServer",
                EmbeddedResourcePath = "Jellyfin.Plugin.Mcp.Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "McpServerJs",
                EmbeddedResourcePath = "Jellyfin.Plugin.Mcp.Configuration.configPage.js"
            }
        ];
    }
}
