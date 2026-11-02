using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Playlists;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library;

/// <summary>
/// Pins the real <see cref="Playlist"/> owner/share/open-access rules that the tool filter relies on.
/// </summary>
[Collection("BaseItemStatics")]
public sealed class RealPlaylistVisibilityTests : IDisposable
{
    private readonly User _user = TestUsers.Create();

    public RealPlaylistVisibilityTests()
    {
        var paths = new Mock<IServerApplicationPaths>();
        paths.SetupGet(p => p.DataPath).Returns("/data");
        var config = new Mock<IServerConfigurationManager>();
        config.SetupGet(c => c.ApplicationPaths).Returns(paths.Object);
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.ContainsSubPath(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        BaseItem.ConfigurationManager = config.Object;
        BaseItem.FileSystem = fileSystem.Object;
    }

    public void Dispose()
    {
        BaseItem.ConfigurationManager = null!;
        BaseItem.FileSystem = null!;
    }

    private Playlist Make(Guid owner, bool open = false, params Guid[] shared)
    {
        var shares = new List<PlaylistUserPermissions>();
        foreach (var id in shared)
        {
            shares.Add(new PlaylistUserPermissions(id, false));
        }

        return new Playlist { Id = Guid.NewGuid(), Path = "/data/playlists/x", OwnerUserId = owner, OpenAccess = open, Shares = shares };
    }

    [Fact]
    public void OwnedSharedAndOpenPlaylists_AreVisible_OthersPrivateIsNot()
    {
        Assert.True(Make(_user.Id).IsVisibleStandalone(_user));
        Assert.True(Make(Guid.NewGuid(), false, _user.Id).IsVisibleStandalone(_user));
        Assert.True(Make(Guid.NewGuid(), true).IsVisibleStandalone(_user));
        Assert.False(Make(Guid.NewGuid()).IsVisibleStandalone(_user));
    }
}
