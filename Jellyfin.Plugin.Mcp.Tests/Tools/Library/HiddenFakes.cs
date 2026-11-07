using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library.Fakes;

// Named "Playlist" / "BoxSet" on purpose: BaseItem.GetBaseItemKind parses the runtime type name, which a Moq proxy would break.
internal sealed class Playlist : MediaBrowser.Controller.Playlists.Playlist
{
    public bool Visible { get; set; }

    // Deliberately different from IsVisibleStandalone so the tests pin the predicate production code uses.
    public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => true;

    public override bool IsVisibleStandalone(User user) => Visible;
}

internal sealed class BoxSet : MediaBrowser.Controller.Entities.Movies.BoxSet
{
    public bool Visible { get; set; }

    public IReadOnlyList<BaseItem> VisibleChildren { get; init; } = [];

    // Deliberately different from IsVisibleStandalone so the tests pin the predicate production code uses.
    public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => true;

    public override bool IsVisibleStandalone(User user) => Visible;

    // The real override sorts through the static library manager; the tests only need the per-user child list.
    public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery query) => VisibleChildren;
}

// Folder.GetItems is not virtual; its protected GetItemsInternal is the seam where Jellyfin's real folder logic would run.
internal sealed class RecordingFolder : Folder
{
    public List<InternalItemsQuery> Queries { get; } = [];

    public QueryResult<BaseItem> Result { get; set; } = new(0, 0, []);

    protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
    {
        Queries.Add(query);
        return Result;
    }
}

internal sealed class RecordingCollectionFolder : CollectionFolder
{
    public List<InternalItemsQuery> Queries { get; } = [];

    protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
    {
        Queries.Add(query);
        return new QueryResult<BaseItem>(0, 0, []);
    }
}

internal sealed class HiddenPerson : Person
{
    public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => false;
}
