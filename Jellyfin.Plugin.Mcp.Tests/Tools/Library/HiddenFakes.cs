using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library.Fakes;

// Named "Playlist" / "BoxSet" on purpose: BaseItem.GetBaseItemKind parses the runtime type name, which a Moq proxy would break.
internal sealed class Playlist : MediaBrowser.Controller.Playlists.Playlist
{
    public bool Visible { get; set; }

    public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => Visible;

    public override bool IsVisibleStandalone(User user) => Visible;
}

internal sealed class BoxSet : MediaBrowser.Controller.Entities.Movies.BoxSet
{
    public bool Visible { get; set; }

    public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => Visible;

    public override bool IsVisibleStandalone(User user) => Visible;
}
