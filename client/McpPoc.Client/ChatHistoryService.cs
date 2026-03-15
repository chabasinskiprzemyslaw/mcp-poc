using OpenAI.Chat;
using System.Text.Json;

namespace McpPoc.Client;

internal static class ChatHistoryService
{
    internal static List<PersistedChatMessage> LoadConversationHistory(string historyFilePath)
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

    internal static void SaveConversationHistory(string historyFilePath, List<PersistedChatMessage> conversationHistory)
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

    internal static List<ChatMessage> BuildRequestMessages(List<PersistedChatMessage> conversationHistory, string userInput)
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
}

internal sealed record PersistedChatMessage(string Role, string Content);
