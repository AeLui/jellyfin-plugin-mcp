using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.Mcp.Tests.Tools.Manage.Fakes;

// Named "Movie" on purpose: BaseItem.GetBaseItemKind parses the runtime type name, which a Moq proxy would break.
internal sealed class Movie : MediaBrowser.Controller.Entities.Movies.Movie
{
    public List<(User User, DateTime? Date, bool ResetPosition)> Played { get; } = [];

    public List<User> Unplayed { get; } = [];

    public override void MarkPlayed(User user, DateTime? datePlayed, bool resetPosition)
        => Played.Add((user, datePlayed, resetPosition));

    public override void MarkUnplayed(User user)
        => Unplayed.Add(user);

    public override bool IsVisible(User user, bool skipAllowedTagsCheck = false) => true;
}
