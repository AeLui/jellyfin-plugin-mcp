using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tests.Tools;
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Manage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Playlists;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Manage;

public sealed class ManageToolsTests : IDisposable
{
    private readonly User _user = TestUsers.Create();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<IPlaylistManager> _playlists = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly McpPrincipal _principal;

    public ManageToolsTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(100);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Manage });

        // Playlist.GetManageableItems resolves its entries through the static BaseItem.LibraryManager.
        // Only this class touches it and xunit runs the tests of one class sequentially.
        BaseItem.LibraryManager = _library.Object;
    }

    public void Dispose() => BaseItem.LibraryManager = null!;

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private static JsonElement Parse(ToolResult result) => JsonDocument.Parse(result.Text).RootElement;

    private ItemLookup Lookup() => new(_library.Object, new Mock<IUserViewManager>().Object);

    private PlaylistResolver Resolver() => new(Lookup(), _playlists.Object);

    private SetPlayedTool SetPlayed() => new(_users.Object, _settings.Object, Lookup(), _userData.Object);

    private SetFavoriteTool SetFavorite() => new(_users.Object, _settings.Object, Lookup(), _userData.Object);

    private ListPlaylistsTool List() => new(_users.Object, _settings.Object, _playlists.Object, Resolver(), _userData.Object);

    private ManagePlaylistTool Manage() => new(_users.Object, _settings.Object, Lookup(), _playlists.Object, Resolver(), _library.Object);

    private Fakes.Movie AddMovie()
    {
        var movie = new Fakes.Movie { Id = Guid.NewGuid(), Name = "Movie" };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        return movie;
    }

    private Mock<Playlist> AddPlaylist(Guid ownerId, IReadOnlyList<PlaylistUserPermissions>? shares = null, bool visibleToUser = true)
    {
        var playlist = new Mock<Playlist> { CallBase = true };
        playlist.Object.Id = Guid.NewGuid();
        playlist.Object.Name = "List";
        playlist.Object.OwnerUserId = ownerId;
        playlist.Object.Shares = shares ?? [];
        playlist.Setup(p => p.CanDelete(It.IsAny<User>())).Returns(true);
        _library.Setup(l => l.GetItemById<BaseItem>(playlist.Object.Id, _user)).Returns(visibleToUser ? playlist.Object : null);
        _playlists.Setup(p => p.GetPlaylistForUser(playlist.Object.Id, _user.Id)).Returns(playlist.Object);
        return playlist;
    }

    private void NeverModified()
    {
        _playlists.Verify(p => p.CreatePlaylist(It.IsAny<PlaylistCreationRequest>()), Times.Never);
        _playlists.Verify(p => p.UpdatePlaylist(It.IsAny<PlaylistUpdateRequest>()), Times.Never);
        _playlists.Verify(p => p.AddItemToPlaylistAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int?>(), It.IsAny<Guid>()), Times.Never);
        _playlists.Verify(p => p.RemoveItemFromPlaylistAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _library.Verify(l => l.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task SetPlayed_SavesForTokenUserOnly()
    {
        var movie = AddMovie();

        await SetPlayed().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","played":true}"""), _principal, CancellationToken.None);
        await SetPlayed().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","played":false}"""), _principal, CancellationToken.None);

        Assert.Equal((_user, (DateTime?)null, true), Assert.Single(movie.Played));
        Assert.Same(_user, Assert.Single(movie.Unplayed));
    }

    [Fact]
    public async Task SetPlayed_ItemNotVisible_IsError_AndMissingPlayedIsInvalid()
    {
        var id = Guid.NewGuid();
        var movie = AddMovie();

        var hidden = await Assert.ThrowsAsync<ToolException>(() =>
            SetPlayed().ExecuteAsync(Args($$"""{"itemId":"{{id}}","played":true}"""), _principal, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<ToolException>(() =>
            SetPlayed().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", hidden.Message);
        Assert.Contains("'played'", missing.Message, StringComparison.Ordinal);
        Assert.Empty(movie.Played);
    }

    [Fact]
    public async Task SetFavorite_SavesForTokenUser()
    {
        var movie = AddMovie();
        var data = new UserItemData { Key = "k" };
        _userData.Setup(d => d.GetUserData(_user, movie)).Returns(data);

        var result = await SetFavorite().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","favorite":true}"""), _principal, CancellationToken.None);

        Assert.True(data.IsFavorite);
        _userData.Verify(d => d.SaveUserData(_user, movie, data, UserDataSaveReason.UpdateUserRating, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(Parse(result).GetProperty("favorite").GetBoolean());
    }

    [Fact]
    public async Task SetFavorite_ItemNotVisible_IsError()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            SetFavorite().ExecuteAsync(Args($$"""{"itemId":"{{Guid.NewGuid()}}","favorite":true}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _userData.Verify(d => d.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ManagePlaylist_Create_PassesUserId()
    {
        var movie = AddMovie();
        PlaylistCreationRequest? captured = null;
        _playlists.Setup(p => p.CreatePlaylist(It.IsAny<PlaylistCreationRequest>()))
            .Callback<PlaylistCreationRequest>(r => captured = r)
            .ReturnsAsync(new PlaylistCreationResult("abc"));

        var result = await Manage().ExecuteAsync(
            Args($$"""{"action":"create","name":"  Mix ","itemIds":["{{movie.Id}}"]}"""), _principal, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(_user.Id, captured.UserId);
        Assert.Equal("Mix", captured.Name);
        Assert.Equal(new[] { movie.Id }, captured.ItemIdList);
        Assert.False(captured.Public);
        Assert.Equal("abc", Parse(result).GetProperty("playlistId").GetString());
    }

    [Fact]
    public async Task ManagePlaylist_Create_HiddenItem_IsItemNotFound()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args($$"""{"action":"create","name":"Mix","itemIds":["{{Guid.NewGuid()}}"]}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        NeverModified();
    }

    [Fact]
    public async Task ManagePlaylist_AddItems_RequiresItemIds()
    {
        var playlist = AddPlaylist(_user.Id);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args($$"""{"action":"add_items","playlistId":"{{playlist.Object.Id}}"}"""), _principal, CancellationToken.None));

        Assert.Contains("'itemIds'", ex.Message, StringComparison.Ordinal);
        NeverModified();
    }

    [Fact]
    public async Task ManagePlaylist_AddItems_AsOwnerAndAsEditor_Adds_ButNotAsReadOnlyShare()
    {
        var movie = AddMovie();
        var owned = AddPlaylist(_user.Id);
        var edit = AddPlaylist(Guid.NewGuid(), [new PlaylistUserPermissions(_user.Id, true)]);
        var readOnly = AddPlaylist(Guid.NewGuid(), [new PlaylistUserPermissions(_user.Id, false)]);

        foreach (var playlist in new[] { owned, edit })
        {
            await Manage().ExecuteAsync(
                Args($$"""{"action":"add_items","playlistId":"{{playlist.Object.Id}}","itemIds":["{{movie.Id}}"]}"""), _principal, CancellationToken.None);
            _playlists.Verify(p => p.AddItemToPlaylistAsync(playlist.Object.Id, It.Is<IReadOnlyCollection<Guid>>(c => c.Single() == movie.Id), null, _user.Id), Times.Once);
        }

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args($$"""{"action":"add_items","playlistId":"{{readOnly.Object.Id}}","itemIds":["{{movie.Id}}"]}"""), _principal, CancellationToken.None));
        Assert.Equal("Playlist not found", ex.Message);
        _playlists.Verify(p => p.AddItemToPlaylistAsync(readOnly.Object.Id, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int?>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ManagePlaylist_DeleteOtherUsersPlaylist_NotFound()
    {
        var shared = AddPlaylist(Guid.NewGuid(), [new PlaylistUserPermissions(_user.Id, true)]);
        var hidden = AddPlaylist(Guid.NewGuid(), visibleToUser: false);

        var notOwner = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args($$"""{"action":"delete","playlistId":"{{shared.Object.Id}}"}"""), _principal, CancellationToken.None));
        var notVisible = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args($$"""{"action":"delete","playlistId":"{{hidden.Object.Id}}"}"""), _principal, CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args($$"""{"action":"delete","playlistId":"{{Guid.NewGuid()}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Playlist not found", notOwner.Message);
        Assert.Equal(notOwner.Message, notVisible.Message);
        Assert.Equal(notOwner.Message, unknown.Message);
        NeverModified();
    }

    [Fact]
    public async Task ManagePlaylist_DeleteOwnPlaylist_Deletes()
    {
        var playlist = AddPlaylist(_user.Id);

        await Manage().ExecuteAsync(Args($$"""{"action":"delete","playlistId":"{{playlist.Object.Id}}"}"""), _principal, CancellationToken.None);

        _library.Verify(l => l.DeleteItem(playlist.Object, It.Is<DeleteOptions>(o => o.DeleteFileLocation), true), Times.Once);
    }

    [Fact]
    public async Task ManagePlaylist_UnknownAction_IsError()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args("""{"action":"explode"}"""), _principal, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args("{}"), _principal, CancellationToken.None));

        Assert.Contains("'action'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'action'", missing.Message, StringComparison.Ordinal);
        NeverModified();
    }

    [Fact]
    public async Task ManagePlaylist_Rename_ChangesOnlyTheName()
    {
        var playlist = AddPlaylist(_user.Id);
        PlaylistUpdateRequest? captured = null;
        _playlists.Setup(p => p.UpdatePlaylist(It.IsAny<PlaylistUpdateRequest>())).Callback<PlaylistUpdateRequest>(r => captured = r).Returns(Task.CompletedTask);

        await Manage().ExecuteAsync(Args($$"""{"action":"rename","playlistId":"{{playlist.Object.Id}}","name":"New"}"""), _principal, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(playlist.Object.Id, captured.Id);
        Assert.Equal(_user.Id, captured.UserId);
        Assert.Equal("New", captured.Name);
        Assert.Null(captured.Ids);
        Assert.Null(captured.Users);
        Assert.Null(captured.Public);
    }

    [Fact]
    public async Task ManagePlaylist_ServerFailure_DoesNotLeakItsMessage()
    {
        var movie = AddMovie();
        var playlist = AddPlaylist(_user.Id);
        _playlists.Setup(p => p.AddItemToPlaylistAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int?>(), It.IsAny<Guid>()))
            .ThrowsAsync(new ArgumentException("No item exists for user alice"));

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args($$"""{"action":"add_items","playlistId":"{{playlist.Object.Id}}","itemIds":["{{movie.Id}}"]}"""), _principal, CancellationToken.None));

        Assert.DoesNotContain("alice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManagePlaylist_RemoveItems_OnlyKnownVisibleEntries()
    {
        var movie = AddMovie();
        var playlist = AddPlaylist(_user.Id);
        playlist.Object.LinkedChildren = [LinkedChild.Create(movie)];
        _library.Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([movie]);
        var entry = movie.Id.ToString("N");

        await Manage().ExecuteAsync(
            Args($$"""{"action":"remove_items","playlistId":"{{playlist.Object.Id}}","entryIds":["{{entry}}"]}"""), _principal, CancellationToken.None);
        _playlists.Verify(p => p.RemoveItemFromPlaylistAsync(playlist.Object.Id.ToString("N"), It.Is<IEnumerable<string>>(e => e.Single() == entry)), Times.Once);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Manage().ExecuteAsync(Args($$"""{"action":"remove_items","playlistId":"{{playlist.Object.Id}}","entryIds":["{{Guid.NewGuid()}}"]}"""), _principal, CancellationToken.None));
        Assert.Contains("'entryIds'", ex.Message, StringComparison.Ordinal);
        _playlists.Verify(p => p.RemoveItemFromPlaylistAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Fact]
    public async Task ListPlaylists_WithId_ReturnsEntryIds()
    {
        var movie = AddMovie();
        var playlist = AddPlaylist(_user.Id);
        playlist.Object.LinkedChildren = [LinkedChild.Create(movie)];
        _library.Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([movie]);

        var result = await List().ExecuteAsync(Args($$"""{"playlistId":"{{playlist.Object.Id}}"}"""), _principal, CancellationToken.None);

        var json = Parse(result);
        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        var entry = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(movie.Id.ToString("N"), entry.GetProperty("entryId").GetString());
        Assert.Equal(movie.Id.ToString("N"), entry.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ListPlaylists_WithoutId_ListsAccessiblePlaylists_AndHiddenIdIsNotFound()
    {
        var mine = AddPlaylist(_user.Id);
        var hidden = AddPlaylist(Guid.NewGuid(), visibleToUser: false);
        _playlists.Setup(p => p.GetPlaylists(_user.Id)).Returns([mine.Object]);

        var result = await List().ExecuteAsync(Args("{}"), _principal, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            List().ExecuteAsync(Args($$"""{"playlistId":"{{hidden.Object.Id}}"}"""), _principal, CancellationToken.None));

        var item = Assert.Single(Parse(result).GetProperty("items").EnumerateArray());
        Assert.Equal(mine.Object.Id.ToString("N"), item.GetProperty("id").GetString());
        Assert.True(item.GetProperty("isOwner").GetBoolean());
        Assert.Equal("Playlist not found", ex.Message);
    }

    [Fact]
    public void Tools_DeclareManageScopeAndAnnotations()
    {
        Assert.All(new IMcpTool[] { SetPlayed(), SetFavorite(), List(), Manage() }, t => Assert.Equal(McpScopes.Manage, t.RequiredScope));
        Assert.True(List().Annotations.ReadOnly);
        Assert.True(Manage().Annotations.Destructive);
        Assert.True(SetPlayed().Annotations.Idempotent);
        Assert.True(SetPlayed().Annotations.Destructive);
        Assert.Contains("cannot be undone", SetPlayed().Description, StringComparison.Ordinal);
        Assert.True(SetFavorite().Annotations.Idempotent);
    }

    [Fact]
    public async Task SetPlayed_ConformsToOutputSchema()
    {
        var movie = AddMovie();
        var tool = SetPlayed();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","played":true}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task SetFavorite_ConformsToOutputSchema()
    {
        var movie = AddMovie();
        _userData.Setup(d => d.GetUserData(_user, movie)).Returns(new UserItemData { Key = "k" });
        var tool = SetFavorite();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","favorite":true}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task ListPlaylists_BothShapes_ConformToOutputSchema()
    {
        var movie = AddMovie();
        var playlist = AddPlaylist(_user.Id);
        playlist.Object.LinkedChildren = [LinkedChild.Create(movie)];
        _library.Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([movie]);
        _playlists.Setup(p => p.GetPlaylists(_user.Id)).Returns([playlist.Object]);
        var tool = List();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args("{}"), _principal, CancellationToken.None));
        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args($$"""{"playlistId":"{{playlist.Object.Id}}"}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task ManagePlaylist_Create_ConformsToOutputSchema()
    {
        var movie = AddMovie();
        _playlists.Setup(p => p.CreatePlaylist(It.IsAny<PlaylistCreationRequest>())).ReturnsAsync(new PlaylistCreationResult("abc"));
        var tool = Manage();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args($$"""{"action":"create","name":"Mix","itemIds":["{{movie.Id}}"]}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task ManagePlaylist_AddItems_ConformsToOutputSchema()
    {
        var movie = AddMovie();
        var playlist = AddPlaylist(_user.Id);
        var tool = Manage();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(
            Args($$"""{"action":"add_items","playlistId":"{{playlist.Object.Id}}","itemIds":["{{movie.Id}}"]}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task ManagePlaylist_RemoveItems_ConformsToOutputSchema()
    {
        var movie = AddMovie();
        var playlist = AddPlaylist(_user.Id);
        playlist.Object.LinkedChildren = [LinkedChild.Create(movie)];
        _library.Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>())).Returns([movie]);
        var tool = Manage();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(
            Args($$"""{"action":"remove_items","playlistId":"{{playlist.Object.Id}}","entryIds":["{{movie.Id:N}}"]}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task ManagePlaylist_Rename_ConformsToOutputSchema()
    {
        var playlist = AddPlaylist(_user.Id);
        _playlists.Setup(p => p.UpdatePlaylist(It.IsAny<PlaylistUpdateRequest>())).Returns(Task.CompletedTask);
        var tool = Manage();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args($$"""{"action":"rename","playlistId":"{{playlist.Object.Id}}","name":"New"}"""), _principal, CancellationToken.None));
    }

    [Fact]
    public async Task ManagePlaylist_Delete_ConformsToOutputSchema()
    {
        var playlist = AddPlaylist(_user.Id);
        var tool = Manage();

        SchemaAssert.Conforms(tool, await tool.ExecuteAsync(Args($$"""{"action":"delete","playlistId":"{{playlist.Object.Id}}"}"""), _principal, CancellationToken.None));
    }
}
