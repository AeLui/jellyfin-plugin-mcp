using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tools;
using MediaBrowser.Controller.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools;

public class LibraryRestrictionTests
{
    private readonly RestrictedWorld _world = new();

    [Fact]
    public void SelectableLibraries_AreTheUsersMediaLibraries()
    {
        var libraries = LibraryRestriction.GetSelectableLibraries(_world.Library.Object, _world.User);

        Assert.Equal(new[] { _world.Movies.Id, _world.Kids.Id }, libraries.Select(l => l.Id));
    }

    [Fact]
    public void GetVisible_ItemInAllowedLibrary_Returned()
    {
        Assert.Same(_world.AllowedMovie, _world.Lookup().GetVisible(_world.Scoped, _world.AllowedMovie.Id));
    }

    [Fact]
    public void GetVisible_ItemInOtherLibrary_LooksMissing()
    {
        var hidden = Assert.Throws<ToolException>(() => _world.Lookup().GetVisible(_world.Scoped, _world.HiddenMovie.Id));
        var missing = Assert.Throws<ToolException>(() => _world.Lookup().GetVisible(_world.Scoped, Guid.NewGuid()));

        Assert.Equal(missing.Message, hidden.Message);
        Assert.Null(_world.Lookup().FindVisible(_world.Scoped, _world.HiddenMovie.Id));
    }

    [Fact]
    public void GetVisible_Unrestricted_SeesEveryLibrary()
    {
        var all = ScopedUser.Unrestricted(_world.User);

        Assert.Same(_world.HiddenMovie, _world.Lookup().GetVisible(all, _world.HiddenMovie.Id));
        _world.Library.Verify(l => l.GetUserRootFolder(), Times.Never);
    }

    [Fact]
    public void GetVisible_LibraryFolders_OnlyAllowedOnes()
    {
        var lookup = _world.Lookup();

        Assert.Same(_world.Movies, lookup.GetVisibleContainer(_world.Scoped, _world.Movies.Id));
        Assert.Throws<ToolException>(() => lookup.GetVisible(_world.Scoped, _world.Kids.Id));
        Assert.Throws<ToolException>(() => lookup.GetVisible(_world.Scoped, _world.Collections.Id));
        Assert.Throws<ToolException>(() => lookup.GetVisible(_world.Scoped, _world.Playlists.Id));
    }

    [Fact]
    public void GetVisible_ItemOutsideEveryLibrary_LooksMissing()
    {
        var loose = _world.Add(new RestrictedWorld.Movie { Id = Guid.NewGuid() });

        Assert.Throws<ToolException>(() => _world.Lookup().GetVisible(_world.Scoped, loose.Id));
    }

    [Fact]
    public void GetVisible_LibraryNoLongerAccessibleToUser_Hidden()
    {
        _world.Root.Libraries = [_world.Kids];

        Assert.Throws<ToolException>(() => _world.Lookup().GetVisible(_world.Scoped, _world.AllowedMovie.Id));
    }

    [Fact]
    public void Collection_VisibleOnlyWithAnAllowedEntry()
    {
        var mixed = _world.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Entries = [_world.HiddenMovie, _world.AllowedMovie] }, _world.Collections);
        var hiddenOnly = _world.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Entries = [_world.HiddenMovie] }, _world.Collections);
        var empty = _world.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid() }, _world.Collections);
        var lookup = _world.Lookup();

        Assert.Same(mixed, lookup.GetVisible(_world.Scoped, mixed.Id));
        Assert.Same(empty, lookup.GetVisible(_world.Scoped, empty.Id));
        Assert.Throws<ToolException>(() => lookup.GetVisible(_world.Scoped, hiddenOnly.Id));
        Assert.Equal(new[] { _world.AllowedMovie }, lookup.VisibleChildren(_world.Scoped, mixed));
    }

    [Fact]
    public void Playlist_VisibleOnlyWithAnAllowedEntry()
    {
        var mixed = _world.Add(new RestrictedWorld.Playlist { Id = Guid.NewGuid(), Entries = [_world.HiddenMovie, _world.AllowedMovie] });
        var hiddenOnly = _world.Add(new RestrictedWorld.Playlist { Id = Guid.NewGuid(), Entries = [_world.HiddenMovie] });
        var lookup = _world.Lookup();

        Assert.Same(mixed, lookup.GetVisible(_world.Scoped, mixed.Id));
        Assert.Throws<ToolException>(() => lookup.GetVisible(_world.Scoped, hiddenOnly.Id));
    }

    [Fact]
    public void Artist_VisibleOnlyWithItemsInAllowedLibraries()
    {
        var shared = _world.Add(new RestrictedWorld.MusicArtist { Id = Guid.NewGuid(), Name = "Shared" });
        var other = _world.Add(new RestrictedWorld.MusicArtist { Id = Guid.NewGuid(), Name = "Other" });
        _world.QueryReturns([], byName: [shared]);

        Assert.Same(shared, _world.Lookup().GetVisible(_world.Scoped, shared.Id));
        Assert.Throws<ToolException>(() => _world.Lookup().GetVisible(_world.Scoped, other.Id));
        var probe = _world.Queries.Last();
        Assert.Equal(new[] { _world.MoviesPhysical }, probe.TopParentIds);
        Assert.Same(_world.User, probe.User);
    }

    [Fact]
    public void EnsureUserLibrary_OnlyAllowedLibraries()
    {
        var lookup = _world.Lookup();

        lookup.EnsureUserLibrary(_world.Scoped, _world.Movies.Id);
        Assert.Throws<ToolException>(() => lookup.EnsureUserLibrary(_world.Scoped, _world.Kids.Id));
        _world.Views.Verify(v => v.GetUserViews(It.IsAny<MediaBrowser.Model.Library.UserViewQuery>()), Times.Never);
    }

    [Fact]
    public void VisibleItems_RestrictedQuery_ScopedToAllowedLibrariesAndContainerFolders()
    {
        _world.QueryReturns([_world.AllowedMovie]);
        var query = new InternalItemsQuery(_world.User) { Recursive = true, IncludeItemTypes = [BaseItemKind.Movie], Limit = 5 };

        var result = _world.Library.Object.GetVisibleItemsResult(query, _world.Scoped);

        Assert.Equal(new BaseItem[] { _world.AllowedMovie }, result.Items);
        var sent = Assert.Single(_world.Queries);
        Assert.Equal(new[] { _world.MoviesPhysical, _world.CollectionsPhysical, _world.Playlists.Id }, sent.TopParentIds);
        Assert.Equal(5, sent.Limit);
    }

    [Fact]
    public void VisibleItems_Unrestricted_QueryUntouched()
    {
        _world.QueryReturns([_world.AllowedMovie, _world.HiddenMovie]);
        var query = new InternalItemsQuery(_world.User) { Recursive = true, IncludeItemTypes = [BaseItemKind.Movie] };

        var result = _world.Library.Object.GetVisibleItemsResult(query, ScopedUser.Unrestricted(_world.User));

        Assert.Equal(2, result.TotalRecordCount);
        Assert.Empty(Assert.Single(_world.Queries).TopParentIds);
    }

    [Fact]
    public void VisibleItems_NoAllowedLibraryLeft_MatchesNothing()
    {
        _world.Root.Libraries = [_world.Kids];
        _world.QueryReturns([]);
        var query = new InternalItemsQuery(_world.User) { Recursive = true, IncludeItemTypes = [BaseItemKind.Movie] };

        _world.Library.Object.GetVisibleItemsResult(query, _world.Scoped);

        var sent = Assert.Single(_world.Queries).TopParentIds;
        Assert.Single(sent);
        Assert.DoesNotContain(_world.KidsPhysical, sent);
    }

    [Fact]
    public void VisibleItems_RestrictedArtists_FilteredWithExactCount()
    {
        var shared = _world.Add(new RestrictedWorld.MusicArtist { Id = Guid.NewGuid(), Name = "A" });
        var other = _world.Add(new RestrictedWorld.MusicArtist { Id = Guid.NewGuid(), Name = "B" });
        var third = _world.Add(new RestrictedWorld.MusicArtist { Id = Guid.NewGuid(), Name = "C" });
        _world.QueryReturns([shared, other, third], byName: [shared, third]);
        var query = new InternalItemsQuery(_world.User) { Recursive = true, IncludeItemTypes = [BaseItemKind.MusicArtist], StartIndex = 1, Limit = 1 };

        var result = _world.Library.Object.GetVisibleItemsResult(query, _world.Scoped);

        Assert.Equal(2, result.TotalRecordCount);
        Assert.Equal(new BaseItem[] { third }, result.Items);
    }

    [Fact]
    public void VisibleItems_RestrictedCollections_HideThoseWithoutAllowedEntries()
    {
        var visible = _world.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Entries = [_world.AllowedMovie] }, _world.Collections);
        var hidden = _world.Add(new RestrictedWorld.BoxSet { Id = Guid.NewGuid(), Entries = [_world.HiddenMovie] }, _world.Collections);
        _world.QueryReturns([hidden, visible]);
        var query = new InternalItemsQuery(_world.User) { Recursive = true, IncludeItemTypes = [BaseItemKind.BoxSet], Limit = 10 };

        var result = _world.Library.Object.GetVisibleItemsResult(query, _world.Scoped);

        Assert.Equal(1, result.TotalRecordCount);
        Assert.Equal(new BaseItem[] { visible }, result.Items);
    }

    [Fact]
    public void VisibleItems_RestrictedQueryWithUnverifiedScope_Throws()
    {
        var query = new InternalItemsQuery(_world.User) { ItemIds = [_world.HiddenMovie.Id] };

        Assert.Throws<InvalidOperationException>(() => _world.Library.Object.GetVisibleItemsResult(query, _world.Scoped));
    }

    [Fact]
    public void VisibleItems_ScopeForOtherUser_Throws()
    {
        var query = new InternalItemsQuery(Auth.TestUsers.Create());

        Assert.Throws<InvalidOperationException>(() => _world.Library.Object.GetVisibleItemsResult(query, _world.Scoped));
    }

    [Fact]
    public void ScopedUser_FromPrincipal_CarriesLibraries()
    {
        var principal = new McpPrincipal(_world.User.Id, new HashSet<string>()) { LibraryIds = new HashSet<Guid> { _world.Kids.Id } };

        var scoped = new ScopedUser(_world.User, principal.LibraryIds);

        Assert.True(scoped.IsRestricted);
        Assert.False(ScopedUser.Unrestricted(_world.User).IsRestricted);
    }
}
