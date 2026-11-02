using System;
using System.Linq;
using Jellyfin.Plugin.Mcp.Api;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Manage;
using Jellyfin.Plugin.Mcp.Tools.Playback;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mcp;

/// <summary>
/// Registers the plugin services with Jellyfin's dependency injection container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        // State-holding services must be singletons: the token store lock and the limiter windows are per instance.
        serviceCollection.AddSingleton<IClock, SystemClock>();
        serviceCollection.AddSingleton<ITokenStore>(sp => new TokenStore(
            GetConfiguration,
            Save,
            sp.GetRequiredService<IClock>(),
            Plugin.ConfigurationLock));
        serviceCollection.AddSingleton(sp => new AuthFailureLimiter(sp.GetRequiredService<IClock>()));
        serviceCollection.AddSingleton(sp => new TokenAuthenticator(
            sp.GetRequiredService<ITokenStore>(),
            sp.GetRequiredService<IUserManager>(),
            sp.GetRequiredService<AuthFailureLimiter>(),
            sp.GetRequiredService<IClock>(),
            GetConfiguration));
        serviceCollection.AddSingleton(McpServerInfo.FromAssembly());
        serviceCollection.AddSingleton<IPluginSettings, PluginSettings>();
        serviceCollection.AddSingleton<ItemLookup>();
        serviceCollection.AddSingleton<SessionResolver>();
        serviceCollection.AddSingleton<PlaylistResolver>();

        foreach (var toolType in typeof(PluginServiceRegistrator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IMcpTool).IsAssignableFrom(t)))
        {
            serviceCollection.AddScoped(typeof(IMcpTool), toolType);
        }

        serviceCollection.AddScoped<ToolRegistry>();
        serviceCollection.AddScoped(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Jellyfin.Plugin.Mcp");
            return new JsonRpcDispatcher(
                sp.GetRequiredService<ToolRegistry>(),
                sp.GetRequiredService<McpServerInfo>(),
                (toolName, exception) => McpLog.ToolFailure(logger, exception, toolName),
                call =>
                {
                    if (Plugin.Instance?.Configuration.LogToolCalls == true)
                    {
                        McpLog.ToolCall(logger, call.ToolName, call.UserId, (long)call.Duration.TotalMilliseconds, !call.IsError);
                    }
                });
        });
    }

    private static PluginConfiguration GetConfiguration()
        => Plugin.Instance?.Configuration ?? throw new PluginNotReadyException();

    private static void Save(PluginConfiguration configuration)
        => (Plugin.Instance ?? throw new PluginNotReadyException()).SaveConfigurationWithTokens(configuration);
}
