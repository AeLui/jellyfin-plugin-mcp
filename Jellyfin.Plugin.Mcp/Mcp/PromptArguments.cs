using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Reads the string-valued arguments of a <c>prompts/get</c> request. Empty or whitespace-only values count as absent,
/// and unknown argument names are ignored.
/// </summary>
public sealed class PromptArguments
{
    private readonly JsonElement _arguments;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptArguments"/> class.
    /// </summary>
    /// <param name="arguments">The <c>arguments</c> element; undefined or <c>null</c> means no arguments.</param>
    /// <exception cref="PromptException">The element is neither an object, <c>null</c> nor missing.</exception>
    public PromptArguments(JsonElement arguments)
    {
        if (arguments.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object))
        {
            throw new PromptException("Invalid params: 'arguments' must be an object");
        }

        _arguments = arguments;
    }

    /// <summary>
    /// Gets an instance without arguments.
    /// </summary>
    public static PromptArguments Empty { get; } = new(default);

    /// <summary>
    /// Returns whether the argument is present with a non-blank value.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns><c>true</c> when the argument has a value.</returns>
    public bool Has(string name) => Raw(name) is not null;

    /// <summary>
    /// Gets a free-text argument as a single line: control characters become spaces, the value is trimmed and truncated.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="maxLength">The maximum length.</param>
    /// <returns>The text, or <c>null</c> when absent.</returns>
    public string? GetText(string name, int maxLength)
    {
        var raw = Raw(name);
        if (raw is null)
        {
            return null;
        }

        var text = new string(raw.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return text.Length > maxLength ? text[..maxLength] : text;
    }

    /// <summary>
    /// Gets an integer argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="min">The smallest allowed value.</param>
    /// <param name="max">The largest allowed value.</param>
    /// <param name="defaultValue">The value when the argument is absent.</param>
    /// <returns>The value.</returns>
    /// <exception cref="PromptException">The value is not an integer in range.</exception>
    public int GetInt(string name, int min, int max, int defaultValue)
    {
        var raw = Raw(name);
        if (raw is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            throw new PromptException(string.Create(CultureInfo.InvariantCulture, $"Invalid argument '{name}': must be an integer between {min} and {max}"));
        }

        return value;
    }

    /// <summary>
    /// Gets an argument that must be one of a fixed set of values (case-insensitive).
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="allowed">The allowed values, as returned.</param>
    /// <param name="defaultValue">The value when the argument is absent.</param>
    /// <returns>The matching entry of <paramref name="allowed"/>.</returns>
    /// <exception cref="PromptException">The value is not allowed.</exception>
    public string GetEnum(string name, IReadOnlyList<string> allowed, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var raw = Raw(name);
        if (raw is null)
        {
            return defaultValue;
        }

        return allowed.FirstOrDefault(a => string.Equals(a, raw.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new PromptException("Invalid argument '" + name + "': must be one of " + string.Join(", ", allowed));
    }

    private string? Raw(string name)
    {
        if (_arguments.ValueKind != JsonValueKind.Object || !_arguments.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new PromptException("Invalid argument '" + name + "': must be a string");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
