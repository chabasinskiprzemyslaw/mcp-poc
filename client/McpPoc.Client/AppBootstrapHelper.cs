using OpenAI.Chat;

namespace McpPoc.Client;

internal static class AppBootstrapHelper
{
    internal static string ResolveHistoryFilePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("OPENAI_CHAT_HISTORY_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var envPath = DotEnvHelper.FindDotEnvPath();
        var historyRoot = envPath is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(envPath)!;

        return Path.Combine(historyRoot, ".openai-chat-history.json");
    }

    internal static string ResolveRegistryFilePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("MCP_SERVER_REGISTRY_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var envPath = DotEnvHelper.FindDotEnvPath();
        var registryRoot = envPath is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(envPath)!;

        return Path.Combine(registryRoot, ".mcp-server-registry.json");
    }

    internal static string ExtractAssistantText(ChatCompletion completion)
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
}
