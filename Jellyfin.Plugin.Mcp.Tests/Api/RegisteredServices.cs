using Jellyfin.Plugin.Mcp.Auth;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Jellyfin.Plugin.Mcp.Tests.Api;

/// <summary>
/// Builds the plugin's real service registrations over mocked Jellyfin services.
/// </summary>
internal static class RegisteredServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Mock<IUserManager>().Object);
        services.AddSingleton(new Mock<ILibraryManager>().Object);
        services.AddSingleton(new Mock<IUserDataManager>().Object);
        services.AddSingleton(new Mock<IUserViewManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.TV.ITVSeriesManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Session.ISessionManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Playlists.IPlaylistManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Providers.IProviderManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Model.IO.IFileSystem>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Model.Tasks.ITaskManager>().Object);
        services.AddSingleton(new Mock<ISimilarItemsManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Collections.ICollectionManager>().Object);
        services.AddSingleton(new Mock<IMediaSourceManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.ISystemManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Model.Activity.IActivityManager>().Object);
        services.AddSingleton(new Mock<IServerApplicationHost>().Object);
        new PluginServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
