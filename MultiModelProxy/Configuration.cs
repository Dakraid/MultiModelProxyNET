// MultiModelProxy - Configuration.cs
// Created on 2024.11.18
// Last modified at 2024.11.19 13:11

namespace MultiModelProxy;

public enum Handler { TabbyApi, MistralAi, OpenRouter }

public class Settings
{
    public string ApiKey { get; set; } = string.Empty;
    public bool SillyTavernExtension { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string Prefill { get; set; } = "[Continue.]";
    public string Postfill { get; set; } = "[Write the next reply as instructed, taking the thoughts in the chain_of_thought block into account.]";
    public LoggingSettings Logging { get; set; } = new();
    public RegexSettings Regex { get; set; } = new();
    public InferenceSettings Inference { get; set; } = new();
}

public class LoggingSettings
{
    public bool SaveCoT { get; set; }
    public bool SaveFull { get; set; }
}

public class RegexSettings
{
    public string TextToChat { get; set; }
        = @"(?P<System>\[INST](.*)\[/INST] Understood\.</s>)|(?P<User>(?<=\[INST])\s([\w|\s]*:)\s(.*?)(?=\[/INST]))|(?P<Assistant>(?<=\[/INST])\s([\w|\s]*:)\s(.*?)(?=</s>))";

    public string Username { get; set; } = @"\[INST]\s*([^\s:]+):\s*[^\[\]]*\[/INST](?!</s>)";
}

public class InferenceSettings
{
    public string PrimaryEndpoint { get; set; } = string.Empty;
    public int MinCoTTokens { get; set; } = 200;
    public bool UseFallback { get; set; } = true;
    public Handler CotHandler { get; set; } = Handler.MistralAi;
    public EndpointSettings TabbyApiSettings { get; set; } = new();
    public EndpointSettings MistralAiSettings { get; set; } = new();
    public EndpointSettings OpenRouterSettings { get; set; } = new();
}

public class EndpointSettings
{
    public string? BaseUri { get; set; } = null;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
