using System.Text.Json;

namespace McpPoc.Client;

internal sealed record LocalToolDefinition(string Name, string Description, JsonElement InputSchema);

internal static class LocalToolRegistry
{
    private static readonly JsonElement EmptyObject = ParseJsonClone("{}");

    internal static IReadOnlyList<LocalToolDefinition> ListTools()
    {
        return
        [
            new LocalToolDefinition(
                "current_date",
                "Returns the current local and UTC date/time for the host machine.",
                ParseJsonClone("""
                {
                  "type": "object",
                  "properties": {},
                  "additionalProperties": false
                }
                """))
        ];
    }

    internal static Task<JsonElement> CallToolAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(toolName, "current_date", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown local tool '{toolName}'.");
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Local tool arguments must be a JSON object.");
        }

        var nowLocal = DateTimeOffset.Now;
        var nowUtc = DateTimeOffset.UtcNow;

        var payload = JsonSerializer.Serialize(new
        {
            localNowIso = nowLocal.ToString("O"),
            utcNowIso = nowUtc.ToString("O"),
            localDate = nowLocal.ToString("yyyy-MM-dd"),
            utcDate = nowUtc.ToString("yyyy-MM-dd"),
            dayOfWeek = nowLocal.DayOfWeek.ToString()
        });

        return Task.FromResult(ParseJsonClone(payload));
    }

    internal static JsonElement EmptyArguments() => EmptyObject.Clone();

    private static JsonElement ParseJsonClone(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
