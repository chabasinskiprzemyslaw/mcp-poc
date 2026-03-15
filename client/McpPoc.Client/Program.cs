using OpenAI.Chat;
using McpPoc.Client;
using System.ClientModel;
using System.IO;
using System.Text;
using System.Text.Json;

DotEnvHelper.LoadIfPresent();

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Missing OPENAI_API_KEY. Set it and rerun.");
    Environment.ExitCode = 1;
    return;
}

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
if (string.IsNullOrWhiteSpace(model))
{
    model = "gpt-4.1-mini";
}

var historyFilePath = AppBootstrapHelper.ResolveHistoryFilePath();
var conversationHistory = ChatHistoryService.LoadConversationHistory(historyFilePath);
var registryFilePath = AppBootstrapHelper.ResolveRegistryFilePath();
using var serverRegistry = ServerRegistry.LoadFromFile(registryFilePath, enableHotReload: true);
using var mcpClient = new McpClient("mcp-poc-client", "1.0.0");
var localCommandDispatcher = new LocalCommandDispatcher(serverRegistry, mcpClient);
var chatClient = new ChatClient(model: model, apiKey: apiKey);
var toolContextTtl = ResolveToolContextTtl();
var cachedToolContext = string.Empty;
var toolContextExpiresAtUtc = DateTimeOffset.MinValue;
var localTools = LocalToolRegistry.ListTools();

Console.WriteLine("MCP PoC Client (.NET + OpenAI)");
Console.WriteLine($"Model: {model}");
Console.WriteLine($"History: {historyFilePath}");
Console.WriteLine($"Stored messages: {conversationHistory.Count}");
Console.WriteLine($"Server registry: {registryFilePath}");
Console.WriteLine($"Registered servers: {serverRegistry.Entries.Count}");
Console.WriteLine($"MCP tool context cache TTL: {toolContextTtl.TotalSeconds:F0}s");
Console.WriteLine("Commands: /servers..., /mcp show, /mcp tools <serverId>, /mcp call <serverId> <tool> \"<jsonArgs>\", /mcp route-call <tool> \"<jsonArgs>\" <tag...>");
Console.WriteLine("Type a prompt and press Enter. Type 'exit' to quit.");
Console.WriteLine();

while (true)
{
    Console.Write("You> ");
    var input = Console.ReadLine();

    if (input is null || string.Equals(input.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (await localCommandDispatcher.TryHandleAsync(input.Trim()))
    {
        continue;
    }

    try
    {
        var messages = ChatHistoryService.BuildRequestMessages(conversationHistory, input);
        if (DateTimeOffset.UtcNow >= toolContextExpiresAtUtc || string.IsNullOrWhiteSpace(cachedToolContext))
        {
            cachedToolContext = await BuildToolContextAsync(serverRegistry, mcpClient, localTools, CancellationToken.None);
            toolContextExpiresAtUtc = DateTimeOffset.UtcNow.Add(toolContextTtl);
        }

        var toolContext = cachedToolContext;
        messages.Insert(0, new SystemChatMessage(toolContext));
        var assistantText = await CompleteWithToolsAsync(
            messages,
            chatClient,
            serverRegistry,
            mcpClient,
            localTools,
            CancellationToken.None);
        Console.WriteLine();
        Console.WriteLine($"Assistant> {assistantText}");
        Console.WriteLine();

        conversationHistory.Add(new PersistedChatMessage("user", input));
        conversationHistory.Add(new PersistedChatMessage("assistant", assistantText));
        ChatHistoryService.SaveConversationHistory(historyFilePath, conversationHistory);
    }
    catch (ClientResultException ex)
    {
        Console.Error.WriteLine($"OpenAI API error ({ex.Status}): {ex.Message}");
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Request error: {ex.Message}");
    }
    catch (TaskCanceledException ex)
    {
        Console.Error.WriteLine($"Request timed out: {ex.Message}");
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"History I/O error: {ex.Message}");
    }
    catch (UnauthorizedAccessException ex)
    {
        Console.Error.WriteLine($"History access error: {ex.Message}");
    }
}

static TimeSpan ResolveToolContextTtl()
{
    var raw = Environment.GetEnvironmentVariable("MCP_TOOL_CONTEXT_TTL_SECONDS");
    if (!int.TryParse(raw, out var ttlSeconds) || ttlSeconds < 0)
    {
        ttlSeconds = 30;
    }

    return TimeSpan.FromSeconds(ttlSeconds);
}

static async Task<string> CompleteWithToolsAsync(
    List<ChatMessage> messages,
    ChatClient chatClient,
    ServerRegistry serverRegistry,
    McpClient mcpClient,
    IReadOnlyList<LocalToolDefinition> localTools,
    CancellationToken cancellationToken)
{
    const int maxToolRounds = 4;

    for (var round = 0; round < maxToolRounds; round++)
    {
        var completion = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        var assistantText = AppBootstrapHelper.ExtractAssistantText(completion);

        if (!TryParseToolCall(assistantText, out var toolCall, out var parseError))
        {
            return assistantText;
        }

        if (toolCall is null)
        {
            messages.Add(new AssistantChatMessage(assistantText));
            messages.Add(new UserChatMessage($"Tool request parsing failed: {parseError}. Please either return a valid toolCall JSON object or answer directly."));
            continue;
        }

        var toolResult = await ExecuteToolCallAsync(toolCall, serverRegistry, mcpClient, localTools, cancellationToken);
        messages.Add(new AssistantChatMessage(assistantText));
        messages.Add(new UserChatMessage($"Tool result ({toolCall.Source}/{toolCall.Name}):\n{toolResult}\nNow answer the user request."));
    }

    return "I reached the tool-call limit for this turn. Please try again with a narrower request.";
}

static bool TryParseToolCall(string assistantText, out ToolCallRequest? toolCall, out string? error)
{
    toolCall = null;
    error = null;

    var trimmed = assistantText.Trim();
    if (!trimmed.StartsWith("{", StringComparison.Ordinal))
    {
        return false;
    }

    try
    {
        using var document = JsonDocument.Parse(trimmed);
        var root = document.RootElement;
        if (!root.TryGetProperty("toolCall", out var toolCallElement) || toolCallElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var source = toolCallElement.TryGetProperty("source", out var sourceElement)
            ? sourceElement.GetString()
            : null;
        var name = toolCallElement.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        var serverId = toolCallElement.TryGetProperty("serverId", out var serverIdElement)
            ? serverIdElement.GetString()
            : null;

        var arguments = toolCallElement.TryGetProperty("arguments", out var argsElement)
            ? argsElement.Clone()
            : LocalToolRegistry.EmptyArguments();

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(name))
        {
            error = "toolCall.source and toolCall.name are required.";
            return true;
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            error = "toolCall.arguments must be a JSON object.";
            return true;
        }

        toolCall = new ToolCallRequest(source, serverId, name, arguments);
        return true;
    }
    catch (JsonException ex)
    {
        error = ex.Message;
        return true;
    }
}

static async Task<string> ExecuteToolCallAsync(
    ToolCallRequest toolCall,
    ServerRegistry serverRegistry,
    McpClient mcpClient,
    IReadOnlyList<LocalToolDefinition> localTools,
    CancellationToken cancellationToken)
{
    try
    {
        if (string.Equals(toolCall.Source, "local", StringComparison.OrdinalIgnoreCase))
        {
            if (!localTools.Any(tool => string.Equals(tool.Name, toolCall.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return JsonSerializer.Serialize(new { ok = false, error = $"Local tool '{toolCall.Name}' is not available." });
            }

            var result = await LocalToolRegistry.CallToolAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        if (string.Equals(toolCall.Source, "mcp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(toolCall.ServerId))
            {
                return JsonSerializer.Serialize(new { ok = false, error = "For MCP tool calls, serverId is required." });
            }

            var entry = serverRegistry.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.ServerId, toolCall.ServerId, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                return JsonSerializer.Serialize(new { ok = false, error = $"Server '{toolCall.ServerId}' was not found." });
            }

            var result = await mcpClient.CallToolAsync(entry, toolCall.Name, toolCall.Arguments, cancellationToken);
            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        return JsonSerializer.Serialize(new { ok = false, error = $"Unknown tool source '{toolCall.Source}'. Use 'local' or 'mcp'." });
    }
    catch (Exception ex)
    {
        return JsonSerializer.Serialize(new { ok = false, error = ex.Message });
    }
}

static async Task<string> BuildToolContextAsync(
    ServerRegistry serverRegistry,
    McpClient mcpClient,
    IReadOnlyList<LocalToolDefinition> localTools,
    CancellationToken cancellationToken)
{
    var entries = serverRegistry.Entries
        .Where(entry =>
            (string.Equals(entry.Transport, "mcp-http", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(entry.BaseUrl))
        .OrderByDescending(entry => entry.Priority)
        .ThenBy(entry => entry.ServerId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var builder = new StringBuilder();
    builder.AppendLine("You are in a tool-enabled console environment with local and MCP tools.");
    builder.AppendLine("When a tool is needed, respond with JSON only using this shape:");
    builder.AppendLine("{\"toolCall\":{\"source\":\"local|mcp\",\"serverId\":\"<required-for-mcp>\",\"name\":\"<tool-name>\",\"arguments\":{}}}");
    builder.AppendLine("If no tool is needed, answer normally.");
    builder.AppendLine();
    builder.AppendLine("Local tools:");

    if (localTools.Count == 0)
    {
        builder.AppendLine("- (no local tools registered)");
    }
    else
    {
        foreach (var localTool in localTools.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase))
        {
            var schema = localTool.InputSchema.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : localTool.InputSchema.GetRawText();
            builder.AppendLine($"- {localTool.Name}: {localTool.Description}");
            builder.AppendLine($"  inputSchema: {schema}");
        }
    }

    builder.AppendLine();
    builder.AppendLine("MCP tools:");

    if (entries.Length == 0)
    {
        builder.AppendLine("No MCP-capable servers are currently registered.");
        return builder.ToString();
    }

    foreach (var entry in entries)
    {
        builder.AppendLine($"Server: {entry.ServerId} ({entry.Name})");

        try
        {
            var tools = await mcpClient.ListToolsAsync(entry, cancellationToken);
            if (tools.Count == 0)
            {
                builder.AppendLine("- (no tools returned)");
                continue;
            }

            foreach (var tool in tools.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase))
            {
                var description = string.IsNullOrWhiteSpace(tool.Description) ? "(no description)" : tool.Description;
                var schema = tool.InputSchema.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : tool.InputSchema.GetRawText();

                builder.AppendLine($"- {tool.Name}: {description}");
                builder.AppendLine($"  inputSchema: {schema}");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"- (tool discovery failed: {ex.Message})");
        }
    }

    return builder.ToString();
}

internal sealed record ToolCallRequest(string Source, string? ServerId, string Name, JsonElement Arguments);

