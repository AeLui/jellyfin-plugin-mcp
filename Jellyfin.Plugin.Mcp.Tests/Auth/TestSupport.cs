using System;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Auth;

namespace Jellyfin.Plugin.Mcp.Tests.Auth;

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow += by;
}

internal static class TestUsers
{
    public static User Create(bool admin = false, bool disabled = false)
    {
        var user = new User("tester", "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider", "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider");
        user.SetPermission(PermissionKind.IsAdministrator, admin);
        user.SetPermission(PermissionKind.IsDisabled, disabled);
        return user;
    }
}
