using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace McpPoc.Client;

public sealed class McpClient : IDisposable
{
    private readonly string _clientName;
    private readonly string _clientVersion;
    private readonly HttpClient _httpClient = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly HashSet<string> _initializedServers = new(StringComparer.OrdinalIgnoreCase);
    private long _nextId = 1;

    public McpClient(string clientName, string clientVersion)
    {
        _clientName = clientName;
        _clientVersion = clientVersion;
    }

    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(ServerRegistryEntry entry, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(entry, cancellationToken);

        var response = await SendRequestAsync(
            entry,
            "tools/list",
            new Dictionary<string, object?>(),
            cancellationToken
        );

        if (!response.TryGetProperty("result", out var resultElement))
        {
            throw new InvalidOperationException("MCP response for tools/list is missing 'result'.");
        }

        if (!resultElement.TryGetProperty("tools", out var toolsElement) || toolsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tools = new List<McpToolDefinition>();
        foreach (var toolElement in toolsElement.EnumerateArray())
        {
            var name = toolElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = toolElement.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString()
                : null;

            var inputSchema = toolElement.TryGetProperty("inputSchema", out var schemaElement)
                ? schemaElement.Clone()
                : JsonDocument.Parse("{}").RootElement.Clone();

            tools.Add(new McpToolDefinition(name, description, inputSchema));
        }

        return tools;
    }

    public async Task<JsonElement> CallToolAsync(
        ServerRegistryEntry entry,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name must not be empty.", nameof(toolName));
        }

        await EnsureInitializedAsync(entry, cancellationToken);

        var paramsObject = new Dictionary<string, object?>
        {
            ["name"] = toolName,
            ["arguments"] = JsonSerializer.Deserialize<object>(arguments.GetRawText(), _jsonOptions)
        };

        var response = await SendRequestAsync(entry, "tools/call", paramsObject, cancellationToken);
        if (!response.TryGetProperty("result", out var resultElement))
        {
            throw new InvalidOperationException("MCP response for tools/call is missing 'result'.");
        }

        return resultElement.Clone();
    }

    private async Task EnsureInitializedAsync(ServerRegistryEntry entry, CancellationToken cancellationToken)
    {
        if (_initializedServers.Contains(entry.ServerId))
        {
            return;
        }

        var initializeParams = new Dictionary<string, object?>
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new Dictionary<string, object?>(),
            ["clientInfo"] = new Dictionary<string, string>
            {
                ["name"] = _clientName,
                ["version"] = _clientVersion
            }
        };

        await SendRequestAsync(entry, "initialize", initializeParams, cancellationToken);
        await SendNotificationAsync(entry, "notifications/initialized", null, cancellationToken);

        _initializedServers.Add(entry.ServerId);
    }

    private async Task SendNotificationAsync(
        ServerRegistryEntry entry,
        string method,
        Dictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        if (parameters is not null)
        {
            payload["params"] = parameters;
        }

        var endpoint = ResolveMcpEndpoint(entry);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"MCP notification '{method}' failed on '{entry.ServerId}' with status {(int)response.StatusCode}: {responseText}"
            );
        }
    }

    private async Task<JsonElement> SendRequestAsync(
        ServerRegistryEntry entry,
        string method,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _nextId);
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId,
            ["method"] = method,
            ["params"] = parameters
        };

        var endpoint = ResolveMcpEndpoint(entry);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"MCP request '{method}' failed on '{entry.ServerId}' with status {(int)response.StatusCode}: {responseText}"
            );
        }

        using var responseDoc = JsonDocument.Parse(responseText);
        var root = responseDoc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"MCP request '{method}' on '{entry.ServerId}' returned an empty batch response.");
            }

            root = root[0];
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            throw new InvalidOperationException(
                $"MCP request '{method}' failed on '{entry.ServerId}' with protocol error: {errorElement.GetRawText()}"
            );
        }

        return root.Clone();
    }

    private static Uri ResolveMcpEndpoint(ServerRegistryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.BaseUrl))
        {
            throw new InvalidOperationException($"Server '{entry.ServerId}' has no baseUrl.");
        }

        if (!string.Equals(entry.Transport, "mcp-http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Server '{entry.ServerId}' transport '{entry.Transport}' is not supported by this MCP client.");
        }

        if (entry.BaseUrl.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(entry.BaseUrl);
        }

        return new Uri($"{entry.BaseUrl.TrimEnd('/')}/mcp");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public sealed record McpToolDefinition(string Name, string? Description, JsonElement InputSchema);
