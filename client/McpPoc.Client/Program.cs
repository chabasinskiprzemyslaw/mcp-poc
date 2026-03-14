using OpenAI.Chat;
using System.ClientModel;
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
var chatClient = new ChatClient(model: model, apiKey: apiKey);

Console.WriteLine("MCP PoC Client (.NET + OpenAI)");
Console.WriteLine($"Model: {model}");
Console.WriteLine($"History: {historyFilePath}");
Console.WriteLine($"Stored messages: {conversationHistory.Count}");
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
