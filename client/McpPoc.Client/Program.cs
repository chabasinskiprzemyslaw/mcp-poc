using OpenAI.Chat;
using McpPoc.Client;
using System.CommandLine;
using System.ClientModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

LoadDotEnvIfPresent();

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

var historyFilePath = ResolveHistoryFilePath();
var conversationHistory = LoadConversationHistory(historyFilePath);
var registryFilePath = ResolveRegistryFilePath();
using var serverRegistry = ServerRegistry.LoadFromFile(registryFilePath, enableHotReload: true);
using var mcpClient = new McpClient("mcp-poc-client", "1.0.0");
var chatClient = new ChatClient(model: model, apiKey: apiKey);

Console.WriteLine("MCP PoC Client (.NET + OpenAI)");
Console.WriteLine($"Model: {model}");
Console.WriteLine($"History: {historyFilePath}");
Console.WriteLine($"Stored messages: {conversationHistory.Count}");
Console.WriteLine($"Server registry: {registryFilePath}");
Console.WriteLine($"Registered servers: {serverRegistry.Entries.Count}");
Console.WriteLine("Commands: /servers..., /mcp tools <serverId>, /mcp call <serverId> <tool> \"<jsonArgs>\", /mcp route-call <tool> \"<jsonArgs>\" <tag...>");
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

    if (await HandleLocalCommandAsync(input.Trim(), serverRegistry, mcpClient))
    {
        continue;
    }

    try
    {
        var messages = BuildRequestMessages(conversationHistory, input);
        var completion = await chatClient.CompleteChatAsync(messages);
        var assistantText = ExtractAssistantText(completion);
        Console.WriteLine();
        Console.WriteLine($"Assistant> {assistantText}");
        Console.WriteLine();

        conversationHistory.Add(new PersistedChatMessage("user", input));
        conversationHistory.Add(new PersistedChatMessage("assistant", assistantText));
        SaveConversationHistory(historyFilePath, conversationHistory);
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

static string ResolveHistoryFilePath()
{
    var configuredPath = Environment.GetEnvironmentVariable("OPENAI_CHAT_HISTORY_PATH");
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        return Path.GetFullPath(configuredPath);
    }

    var envPath = FindDotEnvPath();
    var historyRoot = envPath is null
        ? Directory.GetCurrentDirectory()
        : Path.GetDirectoryName(envPath)!;

    return Path.Combine(historyRoot, ".openai-chat-history.json");
}

static string ResolveRegistryFilePath()
{
    var configuredPath = Environment.GetEnvironmentVariable("MCP_SERVER_REGISTRY_PATH");
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        return Path.GetFullPath(configuredPath);
    }

    var envPath = FindDotEnvPath();
    var registryRoot = envPath is null
        ? Directory.GetCurrentDirectory()
        : Path.GetDirectoryName(envPath)!;

    return Path.Combine(registryRoot, ".mcp-server-registry.json");
}

static List<PersistedChatMessage> LoadConversationHistory(string historyFilePath)
{
    if (!File.Exists(historyFilePath))
    {
        return [];
    }

    try
    {
        var json = File.ReadAllText(historyFilePath);
        return JsonSerializer.Deserialize<List<PersistedChatMessage>>(json) ?? [];
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"History file is invalid JSON. Starting fresh: {ex.Message}");
        return [];
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Unable to read history file. Starting fresh: {ex.Message}");
        return [];
    }
    catch (UnauthorizedAccessException ex)
    {
        Console.Error.WriteLine($"No permission to read history file. Starting fresh: {ex.Message}");
        return [];
    }
}

static void SaveConversationHistory(string historyFilePath, List<PersistedChatMessage> conversationHistory)
{
    var directory = Path.GetDirectoryName(historyFilePath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var json = JsonSerializer.Serialize(conversationHistory, new JsonSerializerOptions
    {
        WriteIndented = true
    });
    File.WriteAllText(historyFilePath, json);
}

static List<ChatMessage> BuildRequestMessages(List<PersistedChatMessage> conversationHistory, string userInput)
{
    var messages = new List<ChatMessage>(conversationHistory.Count + 1);

    foreach (var message in conversationHistory)
    {
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            continue;
        }

        if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(new AssistantChatMessage(message.Content));
        }
        else
        {
            messages.Add(new UserChatMessage(message.Content));
        }
    }

    messages.Add(new UserChatMessage(userInput));
    return messages;
}

static async Task<bool> HandleLocalCommandAsync(string input, ServerRegistry registry, McpClient mcpClient)
{
    if (!input.StartsWith("/", StringComparison.Ordinal))
    {
        return false;
    }

    var tokens = TokenizeLocalCommandInput(input);
    if (tokens.Length == 0)
    {
        PrintLocalCommandUsage();
        return true;
    }

    var rootCommand = BuildLocalRootCommand(registry, mcpClient);
    var parseResult = rootCommand.Parse(tokens);

    if (parseResult.Errors.Count > 0)
    {
        foreach (var parseError in parseResult.Errors)
        {
            Console.WriteLine(parseError.Message);
        }

        PrintLocalCommandUsage();
        return true;
    }

    await parseResult.InvokeAsync();
    return true;
}

static string[] TokenizeLocalCommandInput(string input)
{
    var tokens = new List<string>();
    var current = new System.Text.StringBuilder();
    var inQuotes = false;

    foreach (var ch in input)
    {
        if (ch == '"')
        {
            inQuotes = !inQuotes;
            continue;
        }

        if (!inQuotes && char.IsWhiteSpace(ch))
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }

            continue;
        }

        current.Append(ch);
    }

    if (current.Length > 0)
    {
        tokens.Add(current.ToString());
    }

    if (tokens.Count == 0)
    {
        return [];
    }

    tokens[0] = tokens[0].TrimStart('/');
    if (string.IsNullOrWhiteSpace(tokens[0]))
    {
        return [];
    }

    tokens[0] = tokens[0].ToLowerInvariant();
    if (tokens.Count > 1)
    {
        tokens[1] = tokens[1].ToLowerInvariant();
    }

    return [.. tokens];
}

static RootCommand BuildLocalRootCommand(ServerRegistry registry, McpClient mcpClient)
{
    var rootCommand = new RootCommand
    {
        Description = "Local slash commands"
    };

    var serversCommand = new Command("servers", "Inspect registered MCP servers");
    serversCommand.SetAction(_ =>
    {
        PrintServers(registry);
        return 0;
    });

    var healthCommand = new Command("health", "Run health checks for all registered servers");
    healthCommand.SetAction(async (_, _) =>
    {
        await PrintHealthAsync(registry);
        return 0;
    });

    var metricsCommand = new Command("metrics", "Show per-server fallback metrics");
    metricsCommand.SetAction(_ =>
    {
        PrintMetrics(registry);
        return 0;
    });

    var tagsArgument = new Argument<string[]>("tags")
    {
        Arity = ArgumentArity.OneOrMore,
        Description = "One or more tags to match"
    };

    var findCommand = new Command("find", "Find candidate servers by tags")
    {
        tagsArgument
    };
    findCommand.SetAction(parseResult =>
    {
        var requiredTags = parseResult.GetValue(tagsArgument) ?? [];
        PrintCandidatesByTags(registry, requiredTags);
        return 0;
    });

    serversCommand.Subcommands.Add(healthCommand);
    serversCommand.Subcommands.Add(metricsCommand);
    serversCommand.Subcommands.Add(findCommand);
    rootCommand.Subcommands.Add(serversCommand);

    var mcpCommand = new Command("mcp", "Inspect and invoke MCP servers");

    var serverIdArgument = new Argument<string>("serverId")
    {
        Description = "Registry serverId"
    };
    var toolNameArgument = new Argument<string>("toolName")
    {
        Description = "Tool name exposed by MCP server"
    };
    var argsJsonArgument = new Argument<string?>("argsJson")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "JSON object with tool arguments"
    };

    var mcpToolsCommand = new Command("tools", "List tools from MCP server")
    {
        serverIdArgument
    };
    mcpToolsCommand.SetAction(async parseResult =>
    {
        var serverId = parseResult.GetValue(serverIdArgument) ?? "";
        await PrintMcpToolsAsync(registry, mcpClient, serverId);
        return 0;
    });

    var mcpCallCommand = new Command("call", "Call a tool on MCP server")
    {
        serverIdArgument,
        toolNameArgument,
        argsJsonArgument
    };
    mcpCallCommand.SetAction(async parseResult =>
    {
        var serverId = parseResult.GetValue(serverIdArgument) ?? "";
        var toolName = parseResult.GetValue(toolNameArgument) ?? "";
        var argsJson = parseResult.GetValue(argsJsonArgument) ?? "{}";
        await CallMcpToolOnServerAsync(registry, mcpClient, serverId, toolName, argsJson);
        return 0;
    });

    var routeTagsArgument = new Argument<string[]>("tags")
    {
        Arity = ArgumentArity.OneOrMore,
        Description = "Routing tags/capabilities (e.g. tools:sql domain:data)"
    };

    var mcpRouteCallCommand = new Command("route-call", "Route tool call by tags with fallback")
    {
        toolNameArgument,
        argsJsonArgument,
        routeTagsArgument
    };
    mcpRouteCallCommand.SetAction(async parseResult =>
    {
        var toolName = parseResult.GetValue(toolNameArgument) ?? "";
        var argsJson = parseResult.GetValue(argsJsonArgument) ?? "{}";
        var tags = parseResult.GetValue(routeTagsArgument) ?? [];
        await RouteMcpToolCallAsync(registry, mcpClient, toolName, argsJson, tags);
        return 0;
    });

    mcpCommand.Subcommands.Add(mcpToolsCommand);
    mcpCommand.Subcommands.Add(mcpCallCommand);
    mcpCommand.Subcommands.Add(mcpRouteCallCommand);
    rootCommand.Subcommands.Add(mcpCommand);

    return rootCommand;
}

static void PrintLocalCommandUsage()
{
    Console.WriteLine("Unknown local command. Use: /servers ... | /mcp tools <serverId> | /mcp call <serverId> <tool> \"<jsonArgs>\" | /mcp route-call <tool> \"<jsonArgs>\" <tag...>");
}

static void PrintServers(ServerRegistry registry)
{
    var entries = registry.Entries;
    if (entries.Count == 0)
    {
        Console.WriteLine("No servers registered.");
        return;
    }

    Console.WriteLine("Registered servers:");
    foreach (var entry in entries.OrderByDescending(x => x.Priority))
    {
        var endpoint = string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase)
            ? entry.BaseUrl
            : entry.Command;
        Console.WriteLine($"- {entry.ServerId} | {entry.Name} | transport={entry.Transport} | priority={entry.Priority} | endpoint={endpoint}");
    }
}

static async Task PrintHealthAsync(ServerRegistry registry)
{
    var entries = registry.Entries;
    if (entries.Count == 0)
    {
        Console.WriteLine("No servers registered.");
        return;
    }

    Console.WriteLine("Health check results:");
    foreach (var entry in entries.OrderByDescending(x => x.Priority))
    {
        var result = await registry.CheckHealthAsync(entry, CancellationToken.None);
        var healthText = result.IsHealthy is null ? "n/a" : (result.IsHealthy.Value ? "healthy" : "unhealthy");
        var latency = result.LatencyMs.HasValue ? $"{result.LatencyMs.Value:F0}ms" : "-";
        Console.WriteLine($"- {entry.ServerId} => {healthText}, attempts={result.Attempts}, latency={latency}, error={result.Error ?? "-"}");
    }
}

static void PrintMetrics(ServerRegistry registry)
{
    var snapshots = registry.GetMetricsSnapshots();
    if (snapshots.Count == 0)
    {
        Console.WriteLine("No metrics collected yet.");
        return;
    }

    Console.WriteLine("Per-server metrics (for smart fallback):");
    foreach (var snapshot in snapshots.OrderBy(x => x.ErrorRate).ThenBy(x => x.AverageLatencyMs))
    {
        Console.WriteLine($"- {snapshot.ServerId}: calls={snapshot.TotalCalls}, errors={snapshot.ErrorCalls}, errorRate={snapshot.ErrorRate:P1}, avgLatencyMs={snapshot.AverageLatencyMs:F1}");
    }
}

static void PrintCandidatesByTags(ServerRegistry registry, string[] tags)
{
    if (tags.Length == 0)
    {
        Console.WriteLine("Usage: /servers find <tag1> <tag2> ...");
        return;
    }

    var candidates = registry.FindByTags(tags);
    if (candidates.Count == 0)
    {
        Console.WriteLine($"No servers matched tags: {string.Join(", ", tags)}");
        return;
    }

    Console.WriteLine($"Candidates for tags [{string.Join(", ", tags)}]:");
    foreach (var entry in candidates)
    {
        Console.WriteLine($"- {entry.ServerId} ({entry.Name}) priority={entry.Priority} tags=[{string.Join(", ", entry.Tags)}] capabilities=[{string.Join(", ", entry.Capabilities)}]");
    }
}

static async Task PrintMcpToolsAsync(ServerRegistry registry, McpClient mcpClient, string serverId)
{
    var entry = ResolveServerEntry(registry, serverId);
    if (entry is null)
    {
        Console.WriteLine($"Server '{serverId}' not found in registry.");
        return;
    }

    try
    {
        var tools = await mcpClient.ListToolsAsync(entry, CancellationToken.None);
        Console.WriteLine($"MCP tools from {entry.ServerId}:");
        if (tools.Count == 0)
        {
            Console.WriteLine("- (no tools returned)");
            return;
        }

        foreach (var tool in tools)
        {
            Console.WriteLine($"- {tool.Name}: {tool.Description ?? "(no description)"}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to list tools from '{entry.ServerId}': {ex.Message}");
    }
}

static async Task CallMcpToolOnServerAsync(ServerRegistry registry, McpClient mcpClient, string serverId, string toolName, string argsJson)
{
    var entry = ResolveServerEntry(registry, serverId);
    if (entry is null)
    {
        Console.WriteLine($"Server '{serverId}' not found in registry.");
        return;
    }

    await CallMcpToolInternalAsync(registry, mcpClient, entry, toolName, argsJson);
}

static async Task RouteMcpToolCallAsync(ServerRegistry registry, McpClient mcpClient, string toolName, string argsJson, string[] tags)
{
    var candidates = registry
        .FindByTags(tags)
        .Where(entry => string.Equals(entry.Transport, "mcp-http", StringComparison.OrdinalIgnoreCase) || string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    if (candidates.Length == 0)
    {
        Console.WriteLine($"No MCP-capable servers matched tags: {string.Join(", ", tags)}");
        return;
    }

    foreach (var candidate in candidates)
    {
        var success = await CallMcpToolInternalAsync(registry, mcpClient, candidate, toolName, argsJson, throwOnError: false);
        if (success)
        {
            return;
        }
    }

    Console.WriteLine("All candidate servers failed for route-call.");
}

static async Task<bool> CallMcpToolInternalAsync(
    ServerRegistry registry,
    McpClient mcpClient,
    ServerRegistryEntry entry,
    string toolName,
    string argsJson,
    bool throwOnError = true)
{
    JsonElement arguments;
    try
    {
        arguments = ParseJsonObject(argsJson);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Invalid argsJson: {ex.Message}");
        if (throwOnError)
        {
            throw;
        }

        return false;
    }

    var stopwatch = Stopwatch.StartNew();
    try
    {
        var result = await mcpClient.CallToolAsync(entry, toolName, arguments, CancellationToken.None);
        stopwatch.Stop();
        registry.RecordCall(entry.ServerId, success: true, latencyMs: stopwatch.Elapsed.TotalMilliseconds);

        var formatted = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Tool result from {entry.ServerId}/{toolName}:");
        Console.WriteLine(formatted);
        return true;
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        registry.RecordCall(entry.ServerId, success: false, latencyMs: null);
        Console.Error.WriteLine($"Tool call failed on '{entry.ServerId}' for '{toolName}': {ex.Message}");
        if (throwOnError)
        {
            throw;
        }

        return false;
    }
}

static ServerRegistryEntry? ResolveServerEntry(ServerRegistry registry, string serverId)
{
    return registry.Entries.FirstOrDefault(entry =>
        string.Equals(entry.ServerId, serverId, StringComparison.OrdinalIgnoreCase));
}

static JsonElement ParseJsonObject(string json)
{
    using var document = JsonDocument.Parse(json);
    if (document.RootElement.ValueKind != JsonValueKind.Object)
    {
        throw new InvalidDataException("argsJson must be a JSON object.");
    }

    return document.RootElement.Clone();
}

static string ExtractAssistantText(ChatCompletion completion)
{
    foreach (var part in completion.Content)
    {
        var text = part.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }
    }

    return "No text response was returned.";
}

static void LoadDotEnvIfPresent()
{
    var envPath = FindDotEnvPath();
    if (envPath is null)
    {
        return;
    }

    foreach (var rawLine in File.ReadAllLines(envPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        if (key.Length == 0)
        {
            continue;
        }

        var value = line[(separatorIndex + 1)..].Trim();
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value[1..^1];
        }

        Environment.SetEnvironmentVariable(key, value);
    }
}

static string? FindDotEnvPath()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, ".env");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}

sealed record PersistedChatMessage(string Role, string Content);
