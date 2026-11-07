using System;
using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Prompts;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Prompts;

public class PromptsTests
{
    private static readonly McpPrincipal Library = new(Guid.NewGuid(), new HashSet<string> { McpScopes.Library });

    private static readonly McpPrincipal LibraryAndPlayback = new(Guid.NewGuid(), new HashSet<string> { McpScopes.Library, McpScopes.Playback });

    private static readonly McpPrincipal Admin = new(Guid.NewGuid(), new HashSet<string> { McpScopes.Admin, McpScopes.Playback });

    // FakeClock starts at 2026-11-02T12:00:00Z.
    private static readonly FakeClock Clock = new();

    private static PromptArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    public static TheoryData<string> LibraryPrompts => new() { "movie_night", "catch_me_up", "whats_new" };

    private static IMcpPrompt Create(string name) => name switch
    {
        "movie_night" => new MovieNightPrompt(),
        "catch_me_up" => new CatchMeUpPrompt(),
        "whats_new" => new WhatsNewPrompt(Clock),
        "library_health" => new LibraryHealthPrompt(Clock),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Fact]
    public void Metadata_MatchesSpec()
    {
        var expected = new (string Name, string Title, string Scope)[]
        {
            ("movie_night", "Movie night", McpScopes.Library),
            ("catch_me_up", "Catch me up", McpScopes.Library),
            ("whats_new", "What's new", McpScopes.Library),
            ("library_health", "Library health", McpScopes.Admin),
        };
        foreach (var (name, title, scope) in expected)
        {
            var prompt = Create(name);
            Assert.Equal(name, prompt.Name);
            Assert.Equal(title, prompt.Title);
            Assert.Equal(scope, prompt.RequiredScope);
            Assert.False(string.IsNullOrWhiteSpace(prompt.Description));
            Assert.All(prompt.Arguments, a => Assert.False(a.Required));
        }

        Assert.Equal(new[] { "genre", "maxMinutes", "mood" }, ArgumentNames(new MovieNightPrompt()));
        Assert.Empty(new CatchMeUpPrompt().Arguments);
        Assert.Equal(new[] { "days", "type" }, ArgumentNames(new WhatsNewPrompt(Clock)));
        Assert.Equal(new[] { "days" }, ArgumentNames(new LibraryHealthPrompt(Clock)));
    }

    [Fact]
    public void MovieNight_NoArguments()
    {
        var message = new MovieNightPrompt().Render(PromptArguments.Empty, Library);

        Assert.Equal("Movie night", message.Description);
        foreach (var expected in new[] { "jellyfin_movie_recommendations", "jellyfin_search", "jellyfin_browse", "jellyfin_list_libraries", "jellyfin_get_item", "unplayed", "three" })
        {
            Assert.Contains(expected, message.Text, StringComparison.Ordinal);
        }

        Assert.EndsWith("Reply in the user's language.", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MovieNight_AllArguments()
    {
        var message = new MovieNightPrompt().Render(Args("""{"genre":"Drama","maxMinutes":"120","mood":"something light"}"""), Library);

        Assert.Equal("Movie night: Drama, under 120 minutes, mood \"something light\"", message.Description);
        Assert.Contains("\"Drama\"", message.Text, StringComparison.Ordinal);
        Assert.Contains("120 minutes", message.Text, StringComparison.Ordinal);
        Assert.Contains("\"something light\"", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MovieNight_AllArgumentsEmpty_RendersDefaultText()
    {
        var empty = new MovieNightPrompt().Render(Args("""{"genre":"","maxMinutes":"","mood":""}"""), Library);

        Assert.Equal(new MovieNightPrompt().Render(PromptArguments.Empty, Library), empty);
    }

    [Fact]
    public void MovieNight_MaxMinutesOutOfRange_Throws()
    {
        var ex = Assert.Throws<PromptException>(() => new MovieNightPrompt().Render(Args("""{"maxMinutes":"10"}"""), Library));

        Assert.Equal("Invalid argument 'maxMinutes': must be an integer between 30 and 600", ex.Message);
    }

    [Theory]
    [MemberData(nameof(LibraryPrompts))]
    public void PlaybackOffer_OnlyWithPlaybackScope(string name)
    {
        Assert.DoesNotContain("jellyfin_play", Create(name).Render(PromptArguments.Empty, Library).Text, StringComparison.Ordinal);
        Assert.Contains(PromptText.PlaybackOffer, Create(name).Render(PromptArguments.Empty, LibraryAndPlayback).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CatchMeUp_Text()
    {
        var message = new CatchMeUpPrompt().Render(PromptArguments.Empty, Library);

        Assert.Equal("Catch up on what you are watching", message.Description);
        Assert.Contains("jellyfin_continue_watching", message.Text, StringComparison.Ordinal);
        Assert.Contains("jellyfin_next_up", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsNew_Defaults()
    {
        var message = new WhatsNewPrompt(Clock).Render(PromptArguments.Empty, Library);

        Assert.Equal("What's new in the last 7 days", message.Description);
        Assert.Contains("2026-10-26T12:00:00Z", message.Text, StringComparison.Ordinal);
        Assert.Contains("jellyfin_recently_added", message.Text, StringComparison.Ordinal);
        Assert.Contains("dateAdded", message.Text, StringComparison.Ordinal);
        Assert.Contains("jellyfin_watch_history", message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("types", message.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("movies", "[\"movie\"]")]
    [InlineData("shows", "[\"episode\"]")]
    [InlineData("music", "[\"audio\"]")]
    public void WhatsNew_TypeMapping(string type, string types)
    {
        var message = new WhatsNewPrompt(Clock).Render(Args("{\"type\":\"" + type + "\"}"), Library);

        Assert.Contains("types " + types, message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsNew_OneDayOfShows_Description()
    {
        var message = new WhatsNewPrompt(Clock).Render(Args("""{"days":"1","type":"Shows"}"""), Library);

        Assert.Equal("What's new in the last 1 day (shows)", message.Description);
        Assert.Contains("2026-11-01T12:00:00Z", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsNew_InvalidArguments_Throw()
    {
        Assert.Throws<PromptException>(() => new WhatsNewPrompt(Clock).Render(Args("""{"days":"91"}"""), Library));
        Assert.Throws<PromptException>(() => new WhatsNewPrompt(Clock).Render(Args("""{"type":"films"}"""), Library));
    }

    [Fact]
    public void LibraryHealth_Since()
    {
        var message = new LibraryHealthPrompt(Clock).Render(Args("""{"days":"2"}"""), Admin);

        Assert.Equal("Server health for the last 2 days", message.Description);
        Assert.Contains("since: \"2026-10-31T12:00:00Z\"", message.Text, StringComparison.Ordinal);
        foreach (var expected in new[] { "jellyfin_server_status", "jellyfin_list_tasks", "jellyfin_activity_log", "jellyfin_list_all_sessions", "IP" })
        {
            Assert.Contains(expected, message.Text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("jellyfin_play", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryHealth_DefaultIsOneDay()
    {
        var message = new LibraryHealthPrompt(Clock).Render(PromptArguments.Empty, Admin);

        Assert.Equal("Server health for the last 1 day", message.Description);
        Assert.Contains("since: \"2026-11-01T12:00:00Z\"", message.Text, StringComparison.Ordinal);
        Assert.Throws<PromptException>(() => new LibraryHealthPrompt(Clock).Render(Args("""{"days":"31"}"""), Admin));
    }

    [Theory]
    [InlineData("movie_night")]
    [InlineData("catch_me_up")]
    [InlineData("whats_new")]
    [InlineData("library_health")]
    public void AllPrompts_EndWithReplyInUserLanguage(string name)
    {
        Assert.EndsWith(" Reply in the user's language.", Create(name).Render(PromptArguments.Empty, Admin).Text, StringComparison.Ordinal);
    }

    private static IEnumerable<string> ArgumentNames(IMcpPrompt prompt)
    {
        foreach (var argument in prompt.Arguments)
        {
            yield return argument.Name;
        }
    }
}
