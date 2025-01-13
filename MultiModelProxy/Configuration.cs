// MultiModelProxy - Configuration.cs
// Created on 2024.11.18
// Last modified at 2024.12.07 19:12

namespace MultiModelProxy;

public enum Handler { TabbyApi, MistralAi, OpenRouter }

public class Settings
{
    public required string ApiKey { get; set; }
    public required string Prompt { get; set; }
    public string Prefill { get; set; } = "[Continue.]";
    public string Postfill { get; set; } = "[Write the next reply as instructed, taking the thoughts in the chain_of_thought block into account.]";
    public LoggingSettings Logging { get; set; } = new();
    public InferenceSettings Inference { get; set; } = new();
}

public class LoggingSettings
{
    public bool SaveCoT { get; set; }
    public bool SaveFull { get; set; }
}

public class InferenceSettings
{
    public string? PrimaryEndpoint { get; set; }
    public Handler CotHandler { get; set; } = Handler.MistralAi;
    public int CoTRotation { get; set; } = 0;
    public EndpointSettings? TabbyApiSettings { get; set; }
    public EndpointSettings? MistralAiSettings { get; set; }
    public EndpointSettings? OpenRouterSettings { get; set; }
    public bool UseFallback { get; set; } = true;
    public Handler FallbackHandler { get; set; } = Handler.MistralAi;
    public string[] FallbackModel { get; set; } = [];
}

public class EndpointSettings
{
    public string? BaseUri { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
