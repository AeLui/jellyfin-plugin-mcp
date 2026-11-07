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
using Jellyfin.Plugin.Mcp.Tools;
using Jellyfin.Plugin.Mcp.Tools.Library;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Library;

public class DiscoveryToolsTests
{
    private readonly User _user = TestUsers.Create();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new(MockBehavior.Strict);
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<IUserViewManager> _views = new();
    private readonly Mock<IPluginSettings> _settings = new();
    private readonly Mock<ISimilarItemsManager> _similar = new();
    private readonly McpPrincipal _principal;

    public DiscoveryToolsTests()
    {
        _users.Setup(u => u.GetUserById(_user.Id)).Returns(_user);
        _settings.SetupGet(s => s.MaxPageSize).Returns(50);
        _principal = new McpPrincipal(_user.Id, new HashSet<string> { McpScopes.Library });
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private ItemLookup Lookup() => new(_library.Object, _views.Object);

    private SimilarItemsTool Similar() => new(_users.Object, _settings.Object, _userData.Object, Lookup(), _library.Object, _similar.Object);

    private Movie VisibleMovie(string name = "Night Signal")
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = name };
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        _library.Setup(l => l.GetLibraryOptions(movie)).Returns(new LibraryOptions());
        return movie;
    }

    private void SimilarReturns(params BaseItem[] items)
        => _similar.Setup(s => s.GetSimilarItemsAsync(
                It.IsAny<BaseItem>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<User?>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<int?>(),
                It.IsAny<LibraryOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

    [Fact]
    public async Task SimilarItems_PassesUserLimitAndLibraryOptions()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Night Signal" };
        var options = new LibraryOptions();
        _library.Setup(l => l.GetItemById<BaseItem>(movie.Id, _user)).Returns(movie);
        _library.Setup(l => l.GetLibraryOptions(movie)).Returns(options);
        SimilarReturns();

        await Similar().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","limit":10000}"""), _principal, CancellationToken.None);

        _similar.Verify(
            s => s.GetSimilarItemsAsync(movie, It.Is<IReadOnlyList<Guid>>(l => l.Count == 0), _user, It.IsAny<DtoOptions>(), 50, options, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SimilarItems_DefaultLimitIs20()
    {
        var movie = VisibleMovie();
        SimilarReturns();

        await Similar().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        _similar.Verify(
            s => s.GetSimilarItemsAsync(movie, It.IsAny<IReadOnlyList<Guid>>(), _user, It.IsAny<DtoOptions>(), 20, It.IsAny<LibraryOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SimilarItems_ReturnsSummaries()
    {
        var movie = VisibleMovie();
        var other = new Movie { Id = Guid.NewGuid(), Name = "Paper Harbor" };
        SimilarReturns(other);
        var tool = Similar();

        var result = await tool.ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Paper Harbor", item.GetProperty("name").GetString());
        Assert.Equal("movie", item.GetProperty("type").GetString());
        SchemaAssert.Conforms(tool, result);
    }

    [Fact]
    public async Task SimilarItems_HiddenItem_IsNotFound()
    {
        var id = Guid.NewGuid();
        _library.Setup(l => l.GetItemById<BaseItem>(id, _user)).Returns((BaseItem?)null);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Similar().ExecuteAsync(Args($$"""{"itemId":"{{id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Item not found", ex.Message);
        _similar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SimilarItems_Episode_IsErrorPointingToSeries()
    {
        var episode = new Episode { Id = Guid.NewGuid(), Name = "Pilot" };
        _library.Setup(l => l.GetItemById<BaseItem>(episode.Id, _user)).Returns(episode);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Similar().ExecuteAsync(Args($$"""{"itemId":"{{episode.Id}}"}"""), _principal, CancellationToken.None));

        Assert.Contains("use the series id", ex.Message, StringComparison.Ordinal);
        _similar.VerifyNoOtherCalls();
    }

    public static TheoryData<BaseItem> UnsupportedItems() => new()
    {
        new Season { Id = Guid.NewGuid() },
        new Audio { Id = Guid.NewGuid() },
        new Person { Id = Guid.NewGuid() },
        new Folder { Id = Guid.NewGuid() },
    };

    [Theory]
    [MemberData(nameof(UnsupportedItems))]
    public async Task SimilarItems_UnsupportedType_IsError(BaseItem item)
    {
        _library.Setup(l => l.GetItemById<BaseItem>(item.Id, _user)).Returns(item);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Similar().ExecuteAsync(Args($$"""{"itemId":"{{item.Id}}"}"""), _principal, CancellationToken.None));

        Assert.Equal("Similar items are not available for this item type", ex.Message);
    }

    [Fact]
    public async Task SimilarItems_DropsInvisibleGuardedItems()
    {
        var movie = VisibleMovie();
        var other = new Movie { Id = Guid.NewGuid(), Name = "Paper Harbor" };
        SimilarReturns(other, new Fakes.BoxSet { Id = Guid.NewGuid(), Name = "Hidden", Visible = false });

        var result = await Similar().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}"}"""), _principal, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Paper Harbor", Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("{}", "'itemId'")]
    [InlineData("""{"itemId":"nope"}""", "'itemId'")]
    public async Task SimilarItems_MalformedArguments_NameTheArgument(string json, string expected)
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Similar().ExecuteAsync(Args(json), _principal, CancellationToken.None));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SimilarItems_MalformedLimit_NamesTheArgument()
    {
        var movie = VisibleMovie();

        var ex = await Assert.ThrowsAsync<ToolException>(() => Similar().ExecuteAsync(Args($$"""{"itemId":"{{movie.Id}}","limit":"x"}"""), _principal, CancellationToken.None));

        Assert.Contains("'limit'", ex.Message, StringComparison.Ordinal);
    }
}
