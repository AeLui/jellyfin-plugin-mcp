using System;
using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tools;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools;

public class ItemMapperTests
{
    private static JsonElement Serialize(object value) => JsonDocument.Parse(ToolResult.Json(value).Text).RootElement.Clone();

    [Fact]
    public void ItemMapper_Summary_OmitsNullsAndConvertsTicksToMinutes()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Heat", RunTimeTicks = TimeSpan.FromMinutes(170).Ticks };

        var json = Serialize(ItemMapper.Summary(movie, null));

        Assert.Equal("Heat", json.GetProperty("name").GetString());
        Assert.Equal("movie", json.GetProperty("type").GetString());
        Assert.Equal(170, json.GetProperty("runtimeMinutes").GetDouble());
        foreach (var omitted in new[] { "year", "seriesName", "seasonNumber", "episodeNumber", "communityRating", "officialRating", "played", "favorite", "playbackPositionMinutes", "overview" })
        {
            Assert.False(json.TryGetProperty(omitted, out _), omitted);
        }
    }

    [Fact]
    public void ItemMapper_Summary_IncludesEpisodeAndUserData()
    {
        var episode = new Episode { Id = Guid.NewGuid(), Name = "Pilot", SeriesName = "Lost", ParentIndexNumber = 1, IndexNumber = 2, ProductionYear = 2004 };
        var data = new UserItemData { Key = "k", Played = true, IsFavorite = true, PlaybackPositionTicks = TimeSpan.FromMinutes(12.5).Ticks };

        var json = Serialize(ItemMapper.Summary(episode, data));

        Assert.Equal("episode", json.GetProperty("type").GetString());
        Assert.Equal("Lost", json.GetProperty("seriesName").GetString());
        Assert.Equal(1, json.GetProperty("seasonNumber").GetInt32());
        Assert.Equal(2, json.GetProperty("episodeNumber").GetInt32());
        Assert.Equal(2004, json.GetProperty("year").GetInt32());
        Assert.True(json.GetProperty("played").GetBoolean());
        Assert.True(json.GetProperty("favorite").GetBoolean());
        Assert.Equal(12.5, json.GetProperty("playbackPositionMinutes").GetDouble());
    }

    [Fact]
    public void ItemMapper_Detail_AddsPeopleAndNeverPath()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "Heat",
            Overview = "Cops and robbers",
            Genres = ["Crime"],
            Studios = ["Warner"],
            Tags = ["classic"],
            PremiereDate = new DateTime(1995, 12, 15, 0, 0, 0, DateTimeKind.Utc),
            CriticRating = 87,
            Path = "/secret/heat.mkv",
        };
        var people = new List<PersonInfo> { new() { Name = "Al Pacino", Role = "Hanna", Type = PersonKind.Actor, Id = Guid.NewGuid() } };

        var json = Serialize(ItemMapper.Detail(movie, null, people));

        Assert.Equal("Cops and robbers", json.GetProperty("overview").GetString());
        Assert.Equal("Crime", json.GetProperty("genres")[0].GetString());
        Assert.Equal("Warner", json.GetProperty("studios")[0].GetString());
        Assert.Equal("classic", json.GetProperty("tags")[0].GetString());
        Assert.StartsWith("1995-12-15", json.GetProperty("premiereDate").GetString(), StringComparison.Ordinal);
        Assert.Equal(87, json.GetProperty("criticRating").GetDouble());
        var person = json.GetProperty("people")[0];
        Assert.Equal("Al Pacino", person.GetProperty("name").GetString());
        Assert.Equal("Hanna", person.GetProperty("role").GetString());
        Assert.Equal("Actor", person.GetProperty("type").GetString());
        Assert.True(person.TryGetProperty("id", out _));
        Assert.DoesNotContain("secret", json.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ItemMapper_Detail_WithoutPeopleOrGenres_OmitsThem()
    {
        var json = Serialize(ItemMapper.Detail(new Movie { Id = Guid.NewGuid(), Name = "X" }, null, Array.Empty<PersonInfo>()));

        Assert.False(json.TryGetProperty("people", out _));
        Assert.False(json.TryGetProperty("genres", out _));
    }
}
