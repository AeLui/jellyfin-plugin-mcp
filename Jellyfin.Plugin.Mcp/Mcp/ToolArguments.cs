using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Typed access to the <c>arguments</c> object of a tool call.
/// </summary>
public sealed class ToolArguments
{
    private const int DefaultLimit = 20;

    private readonly JsonElement _arguments;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolArguments"/> class.
    /// </summary>
    /// <param name="arguments">The arguments element; a missing or non-object value is treated as empty.</param>
    public ToolArguments(JsonElement arguments)
    {
        _arguments = arguments.ValueKind == JsonValueKind.Object ? arguments : default;
    }

    /// <summary>
    /// Gets a required string argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The non-empty value.</returns>
    public string GetRequiredString(string name)
    {
        var value = GetString(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid(name, "is required");
        }

        return value;
    }

    /// <summary>
    /// Gets an optional string argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <c>null</c> when absent.</returns>
    public string? GetString(string name)
    {
        if (!TryGet(name, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : throw Invalid(name, "must be a string");
    }

    /// <summary>
    /// Gets a required GUID argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value.</returns>
    public Guid GetRequiredGuid(string name)
        => GetGuid(name) ?? throw Invalid(name, "is required");

    /// <summary>
    /// Gets an optional GUID argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <c>null</c> when absent.</returns>
    public Guid? GetGuid(string name)
    {
        if (!TryGet(name, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid(name, "must be a GUID string");
        }

        return ParseGuid(name, element.GetString());
    }

    /// <summary>
    /// Gets an optional ISO 8601 date-time argument as UTC; a value without an offset is taken as UTC.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value in UTC, or <c>null</c> when absent.</returns>
    public DateTime? GetUtcDateTime(string name)
    {
        if (!TryGet(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
        {
            return value.UtcDateTime;
        }

        throw Invalid(name, "must be an ISO 8601 date-time");
    }

    /// <summary>
    /// Gets an optional integer argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <c>null</c> when absent.</returns>
    public int? GetInt(string name)
    {
        if (!TryGet(name, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Invalid(name, "must be an integer");
        }

        return value;
    }

    /// <summary>
    /// Gets an optional number argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <c>null</c> when absent.</returns>
    public double? GetDouble(string name)
    {
        if (!TryGet(name, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value))
        {
            throw Invalid(name, "must be a number");
        }

        return value;
    }

    /// <summary>
    /// Gets an optional boolean argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <c>null</c> when absent.</returns>
    public bool? GetBool(string name)
    {
        if (!TryGet(name, out var element))
        {
            return null;
        }

        return element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : throw Invalid(name, "must be a boolean");
    }

    /// <summary>
    /// Gets an optional array of strings.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The values; empty when absent.</returns>
    public IReadOnlyList<string> GetStringArray(string name)
    {
        var result = new List<string>();
        if (!TryGet(name, out var element))
        {
            return result;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(name, "must be an array of strings");
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid(name, "must be an array of strings");
            }

            result.Add(item.GetString()!);
        }

        return result;
    }

    /// <summary>
    /// Gets an optional array of GUIDs.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The values; empty when absent.</returns>
    public IReadOnlyList<Guid> GetGuidArray(string name)
    {
        var result = new List<Guid>();
        foreach (var text in GetStringArray(name))
        {
            result.Add(ParseGuid(name, text));
        }

        return result;
    }

    /// <summary>
    /// Gets an optional enum argument, matched case-insensitively; snake_case is accepted.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <c>null</c> when absent.</returns>
    public T? GetEnum<T>(string name)
        where T : struct, Enum
    {
        var text = GetString(name);
        return text is null ? null : ParseEnum<T>(name, text);
    }

    /// <summary>
    /// Gets an optional array of enum values, matched case-insensitively; snake_case is accepted.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="name">The argument name.</param>
    /// <returns>The values; empty when absent.</returns>
    public IReadOnlyList<T> GetEnumArray<T>(string name)
        where T : struct, Enum
    {
        var result = new List<T>();
        foreach (var text in GetStringArray(name))
        {
            result.Add(ParseEnum<T>(name, text));
        }

        return result;
    }

    /// <summary>
    /// Gets the paging arguments.
    /// </summary>
    /// <param name="maxPageSize">The largest allowed page size.</param>
    /// <returns>The start index (at least 0) and limit (1 to <paramref name="maxPageSize"/>, default 20).</returns>
    public (int StartIndex, int Limit) GetPaging(int maxPageSize)
    {
        var limit = Math.Clamp(GetInt("limit") ?? DefaultLimit, 1, Math.Max(1, maxPageSize));
        var startIndex = Math.Max(0, GetInt("startIndex") ?? 0);
        return (startIndex, limit);
    }

    private static T ParseEnum<T>(string name, string text)
        where T : struct, Enum
    {
        var normalized = text.Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        var allowed = string.Join(", ", Enum.GetNames<T>().Select(SnakeCase.From));
        throw Invalid(name, "must be one of: " + allowed);
    }

    private static ToolException Invalid(string name, string reason)
        => new(string.Create(CultureInfo.InvariantCulture, $"Invalid argument '{name}': {reason}"));

    private static Guid ParseGuid(string name, string? text)
        => Guid.TryParse(text, out var guid) ? guid : throw Invalid(name, "must be a valid GUID");

    private bool TryGet(string name, out JsonElement element)
    {
        if (_arguments.ValueKind == JsonValueKind.Object
            && _arguments.TryGetProperty(name, out element)
            && element.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        element = default;
        return false;
    }
}
