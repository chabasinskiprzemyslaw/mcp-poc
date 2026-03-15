using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace McpPoc.Client;

internal sealed class LocalCommandDispatcher
{
    private readonly ServerRegistry _registry;
    private readonly McpClient _mcpClient;

    public LocalCommandDispatcher(ServerRegistry registry, McpClient mcpClient)
    {
        _registry = registry;
        _mcpClient = mcpClient;
    }

    public async Task<bool> TryHandleAsync(string input)
    {
        if (!input.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var tokens = TokenizeInput(input);
        if (tokens.Length == 0)
        {
            PrintUsage();
            return true;
        }

        var rootCommand = BuildRootCommand();
        var parseResult = rootCommand.Parse(tokens);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                Console.WriteLine(parseError.Message);
            }

            PrintUsage();
            return true;
        }

        await parseResult.InvokeAsync();
        return true;
    }

    private static string[] TokenizeInput(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
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

    private RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand
        {
            Description = "Local slash commands"
        };

        var serversCommand = new Command("servers", "Inspect registered MCP servers");
        serversCommand.SetAction(_ =>
        {
            PrintServers();
            return 0;
        });

        var healthCommand = new Command("health", "Run health checks for all registered servers");
        healthCommand.SetAction(async (_, _) =>
        {
            await PrintHealthAsync();
            return 0;
        });

        var metricsCommand = new Command("metrics", "Show per-server fallback metrics");
        metricsCommand.SetAction(_ =>
        {
            PrintMetrics();
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
            PrintCandidatesByTags(requiredTags);
            return 0;
        });

        serversCommand.Subcommands.Add(healthCommand);
        serversCommand.Subcommands.Add(metricsCommand);
        serversCommand.Subcommands.Add(findCommand);
        rootCommand.Subcommands.Add(serversCommand);

        var mcpCommand = new Command("mcp", "Inspect and invoke MCP servers");

        var mcpShowCommand = new Command("show", "List available MCP-capable servers");
        mcpShowCommand.SetAction(_ =>
        {
            PrintAvailableMcpServers();
            return 0;
        });

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
            await PrintMcpToolsAsync(serverId);
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
            await CallMcpToolOnServerAsync(serverId, toolName, argsJson);
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
            await RouteMcpToolCallAsync(toolName, argsJson, tags);
            return 0;
        });

        mcpCommand.Subcommands.Add(mcpShowCommand);
        mcpCommand.Subcommands.Add(mcpToolsCommand);
        mcpCommand.Subcommands.Add(mcpCallCommand);
        mcpCommand.Subcommands.Add(mcpRouteCallCommand);
        rootCommand.Subcommands.Add(mcpCommand);

        return rootCommand;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Unknown local command. Use: /servers ... | /mcp show | /mcp tools <serverId> | /mcp call <serverId> <tool> \"<jsonArgs>\" | /mcp route-call <tool> \"<jsonArgs>\" <tag...>");
    }

    private void PrintServers()
    {
        var entries = _registry.Entries;
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

    private async Task PrintHealthAsync()
    {
        var entries = _registry.Entries;
        if (entries.Count == 0)
        {
            Console.WriteLine("No servers registered.");
            return;
        }

        Console.WriteLine("Health check results:");
        foreach (var entry in entries.OrderByDescending(x => x.Priority))
        {
            var result = await _registry.CheckHealthAsync(entry, CancellationToken.None);
            var healthText = result.IsHealthy is null ? "n/a" : (result.IsHealthy.Value ? "healthy" : "unhealthy");
            var latency = result.LatencyMs.HasValue ? $"{result.LatencyMs.Value:F0}ms" : "-";
            Console.WriteLine($"- {entry.ServerId} => {healthText}, attempts={result.Attempts}, latency={latency}, error={result.Error ?? "-"}");
        }
    }

    private void PrintMetrics()
    {
        var snapshots = _registry.GetMetricsSnapshots();
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

    private void PrintCandidatesByTags(string[] tags)
    {
        if (tags.Length == 0)
        {
            Console.WriteLine("Usage: /servers find <tag1> <tag2> ...");
            return;
        }

        var candidates = _registry.FindByTags(tags);
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

    private void PrintAvailableMcpServers()
    {
        var entries = _registry.Entries
            .Where(entry => string.Equals(entry.Transport, "mcp-http", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.ServerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (entries.Length == 0)
        {
            Console.WriteLine("No MCP-capable servers are currently registered.");
            return;
        }

        Console.WriteLine("Available MCP servers:");
        foreach (var entry in entries)
        {
            var endpoint = string.IsNullOrWhiteSpace(entry.BaseUrl)
                ? "(missing baseUrl)"
                : (entry.BaseUrl.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase)
                    ? entry.BaseUrl
                    : $"{entry.BaseUrl.TrimEnd('/')}/mcp");

            Console.WriteLine($"- {entry.ServerId} ({entry.Name}) transport={entry.Transport} priority={entry.Priority} endpoint={endpoint}");
        }
    }

    private async Task PrintMcpToolsAsync(string serverId)
    {
        var entry = ResolveServerEntry(serverId);
        if (entry is null)
        {
            Console.WriteLine($"Server '{serverId}' not found in registry.");
            return;
        }

        try
        {
            var tools = await _mcpClient.ListToolsAsync(entry, CancellationToken.None);
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

    private async Task CallMcpToolOnServerAsync(string serverId, string toolName, string argsJson)
    {
        var entry = ResolveServerEntry(serverId);
        if (entry is null)
        {
            Console.WriteLine($"Server '{serverId}' not found in registry.");
            return;
        }

        await CallMcpToolInternalAsync(entry, toolName, argsJson);
    }

    private async Task RouteMcpToolCallAsync(string toolName, string argsJson, string[] tags)
    {
        var candidates = _registry
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
            var success = await CallMcpToolInternalAsync(candidate, toolName, argsJson, throwOnError: false);
            if (success)
            {
                return;
            }
        }

        Console.WriteLine("All candidate servers failed for route-call.");
    }

    private async Task<bool> CallMcpToolInternalAsync(
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
            var result = await _mcpClient.CallToolAsync(entry, toolName, arguments, CancellationToken.None);
            stopwatch.Stop();
            _registry.RecordCall(entry.ServerId, success: true, latencyMs: stopwatch.Elapsed.TotalMilliseconds);

            var formatted = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"Tool result from {entry.ServerId}/{toolName}:");
            Console.WriteLine(formatted);
            return true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _registry.RecordCall(entry.ServerId, success: false, latencyMs: null);
            Console.Error.WriteLine($"Tool call failed on '{entry.ServerId}' for '{toolName}': {ex.Message}");
            if (throwOnError)
            {
                throw;
            }

            return false;
        }
    }

    private ServerRegistryEntry? ResolveServerEntry(string serverId)
    {
        return _registry.Entries.FirstOrDefault(entry =>
            string.Equals(entry.ServerId, serverId, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement ParseJsonObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("argsJson must be a JSON object.");
        }

        return document.RootElement.Clone();
    }
}
