using System.Text;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Converts PascalCase identifiers to the snake_case used on the wire.
/// </summary>
public static class SnakeCase
{
    /// <summary>
    /// Converts a PascalCase name to snake_case.
    /// </summary>
    /// <param name="pascal">The name.</param>
    /// <returns>The snake_case name.</returns>
    public static string From(string pascal)
    {
        var builder = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(pascal[i]));
        }

        return builder.ToString();
    }
}
