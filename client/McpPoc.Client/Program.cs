using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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
    model = "o4-mini";
}

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.openai.com/"),
    Timeout = TimeSpan.FromSeconds(120)
};
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

Console.WriteLine("MCP PoC Client (.NET + OpenAI)");
Console.WriteLine($"Model: {model}");
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

    var payload = new
    {
        model,
        input,
        reasoning = new
        {
            effort = "medium"
        }
    };

    using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    HttpResponseMessage response;
    try
    {
        response = await httpClient.PostAsync("v1/responses", content);
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Request error: {ex.Message}");
        continue;
    }
    catch (TaskCanceledException ex)
    {
        Console.Error.WriteLine($"Request timed out: {ex.Message}");
        continue;
    }

    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"OpenAI API error ({(int)response.StatusCode}): {body}");
        continue;
    }

    using var document = JsonDocument.Parse(body);
    var assistantText = ExtractAssistantText(document.RootElement);
    Console.WriteLine();
    Console.WriteLine($"Assistant> {assistantText}");
    Console.WriteLine();
}

static string ExtractAssistantText(JsonElement root)
{
    if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
    {
        var text = outputText.GetString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }
    }

    if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
    {
        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (!contentItem.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = textValue.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(text);
                }
            }
        }

        if (builder.Length > 0)
        {
            return builder.ToString();
        }
    }

    return "No text response was returned.";
}
