using System.Text.Json;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// The <c>prompts/list</c> and <c>prompts/get</c> methods, shared by both protocol eras.
/// </summary>
public sealed partial class JsonRpcDispatcher
{
    private void WritePromptsList(Utf8JsonWriter writer, McpPrincipal principal, bool stateless)
    {
        writer.WriteStartObject();
        if (stateless)
        {
            writer.WriteString("resultType", "complete");
        }

        writer.WriteStartArray("prompts");
        foreach (var prompt in _prompts.ListFor(principal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", prompt.Name);
            writer.WriteString("title", prompt.Title);
            writer.WriteString("description", prompt.Description);
            writer.WriteStartArray("arguments");
            foreach (var argument in prompt.Arguments)
            {
                writer.WriteStartObject();
                writer.WriteString("name", argument.Name);
                writer.WriteString("title", argument.Title);
                writer.WriteString("description", argument.Description);
                writer.WriteBoolean("required", argument.Required);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (stateless)
        {
            WriteCacheHints(writer);
            WriteServerInfoMeta(writer);
        }

        writer.WriteEndObject();
    }

    private byte[] HandlePromptsGet(JsonElement id, JsonElement parameters, McpPrincipal principal, bool stateless)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            return JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidParams, "Invalid params: 'name' is required");
        }

        var name = nameElement.GetString()!;
        var prompt = _prompts.Find(name, principal);
        if (prompt is null)
        {
            return JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidParams, "Unknown prompt: " + name);
        }

        PromptMessage message;
        try
        {
            parameters.TryGetProperty("arguments", out var argumentsElement);
            var arguments = new PromptArguments(argumentsElement);
            foreach (var argument in prompt.Arguments)
            {
                if (argument.Required && !arguments.Has(argument.Name))
                {
                    return JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidParams, "Missing required argument: " + argument.Name);
                }
            }

            message = prompt.Render(arguments, principal);
        }
        catch (PromptException ex)
        {
            return JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidParams, ex.Message);
        }

        return JsonRpcMessages.Success(id, writer =>
        {
            writer.WriteStartObject();
            if (stateless)
            {
                writer.WriteString("resultType", "complete");
            }

            writer.WriteString("description", message.Description);
            writer.WriteStartArray("messages");
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteStartObject("content");
            writer.WriteString("type", "text");
            writer.WriteString("text", message.Text);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndArray();
            if (stateless)
            {
                WriteServerInfoMeta(writer);
            }

            writer.WriteEndObject();
        });
    }
}
