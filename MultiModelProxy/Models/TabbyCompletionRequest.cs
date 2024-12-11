// MultiModelProxy - TabbyCompletionRequest.cs
// Created on 2024.11.18
// Last modified at 2024.12.07 19:12

namespace MultiModelProxy.Models;

#region
using System.Text.Json.Serialization;
#endregion

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultBufferSize = 4096)]
[JsonSerializable(typeof(TabbyCompletionRequest))]
public partial class TabbyCompletionRequestContext : JsonSerializerContext;

public class Message
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class BaseCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public Message[] Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = true;

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.7f;

    [JsonPropertyName("top_p")]
    public float TopP { get; set; } = 1f;

    [JsonPropertyName("presence_penalty")]
    public float PresencePenalty { get; set; } = 0.01f;

    [JsonPropertyName("frequency_penalty")]
    public float FrequencyPenalty { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 1024;
}

public class MistralCompletionRequest : BaseCompletionRequest
{
    [JsonPropertyName("random_seed")]
    public int Seed { get; set; } = Random.Shared.Next();

    [JsonPropertyName("safe_prompt")]
    public bool SafePrompt { get; set; } = false;
}

public class OpenRouterCompletionRequest : BaseCompletionRequest
{
    [JsonPropertyName("min_p")]
    public float? MinP { get; set; } = 0.05f;

    [JsonPropertyName("seed")]
    public int Seed { get; set; } = Random.Shared.Next();
}

public class TabbyCompletionRequest
{
    [JsonPropertyName("messages")]
    public List<Message>? Messages { get; set; }

    [JsonPropertyName("stop")]
    public List<string>? Stop { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("cotPrompt")]
    public string? CotPrompt { get; set; }

    [JsonPropertyName("minMessages")]
    public int? MinMessages { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }

    [JsonPropertyName("min_p")]
    public float? MinP { get; set; }

    [JsonPropertyName("top_p")]
    public float TopP { get; set; } = 1f;

    [JsonPropertyName("presence_penalty")]
    public float PresencePenalty { get; set; } = 0f;

    [JsonPropertyName("frequency_penalty")]
    public float FrequencyPenalty { get; set; } = 0f;
}
