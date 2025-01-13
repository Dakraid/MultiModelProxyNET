// MultiModelProxy - TabbyCompletionRequest.cs
// Created on 2024.11.18
// Last modified at 2024.12.07 19:12

namespace MultiModelProxy.Models;

#region
using System.Text.Json.Serialization;
#endregion

public class Message
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }
}

public class BaseCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public Message[] Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = true;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 1024;

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public float TopP { get; set; } = 1f;

    [JsonPropertyName("presence_penalty")]
    public float PresencePenalty { get; set; }

    [JsonPropertyName("frequency_penalty")]
    public float FrequencyPenalty { get; set; }
}

public class MistralCompletionRequest : BaseCompletionRequest
{
    [JsonPropertyName("random_seed")]
    public int Seed { get; set; } = Random.Shared.Next();

    [JsonPropertyName("safe_prompt")]
    public bool SafePrompt { get; set; }
}

public class OpenRouterCompletionRequest : BaseCompletionRequest
{
    [JsonPropertyName("seed")]
    public int Seed { get; set; } = Random.Shared.Next();
    
    [JsonPropertyName("min_p")]
    public float? MinP { get; set; } = 0.05f;
}

public class ExtensionSettings
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("cot_prompt")]
    public string? CotPrompt { get; set; }

    [JsonPropertyName("cot_rotation")]
    public int CotRotation { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("handler")]
    public Handler Handler { get; set; }

    [JsonPropertyName("use_fallback")]
    public bool UseFallback { get; set; } = true;

    [JsonPropertyName("fallback_handler")]
    public string? FallbackHandler { get; set; }

    [JsonPropertyName("fallback_model")]
    public string[]? FallbackModel { get; set; }

    [JsonPropertyName("fallback_rotation")]
    public int FallbackRotation { get; set; }
}
