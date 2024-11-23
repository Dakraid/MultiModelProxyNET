﻿// MultiModelProxy - TabbyCompletionRequest.cs
// Created on 2024.11.18
// Last modified at 2024.11.19 13:11

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

public class CompletionRequest
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
    public float TopP { get; set; } = 0.95f;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 1024;
}

public class TabbyCompletionRequest
{
    [JsonPropertyName("messages")]
    public List<Message>? Messages { get; set; }

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
}
