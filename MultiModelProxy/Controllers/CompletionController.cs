// MultiModelProxy - CompletionController.cs
// Created on 2024.11.18
// Last modified at 2024.12.07 19:12

// ReSharper disable InconsistentNaming
namespace MultiModelProxy.Controllers;

#region
using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Context;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using Models;
using OpenAI.Chat;
using ChatMessage = OpenAI.Chat.ChatMessage;
using DbChatMessage = Models.ChatMessage;
#endregion

public class CompletionController(
    ILogger<CompletionController> logger,
    IOptions<Settings> settings,
    IHttpClientFactory httpClientFactory,
    ChatClient chatClient,
    ChatContext chatContext
)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, DefaultBufferSize = 4096 };
    private readonly Settings _settings = settings.Value;
    private CancellationToken _combinedToken = CancellationToken.None;
    private Message[] _extendedMessages = [];
    private HttpClient _httpClient = httpClientFactory.CreateClient("PrimaryClient");
    private bool _isAlive;
    private string _lastStoredCoTMessage = string.Empty;
    private string _lastStoredUserMessage = string.Empty;
    private Message? _lastUserMessage;
    private BaseCompletionRequest? _completionRequest;
    private ExtensionSettings? _extensionSettings;

    private async Task IsAliveAsync()
    {
        using var httpClient = httpClientFactory.CreateClient("PrimaryClient");
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        _isAlive = await Utility.IsAliveAsync(httpClient).ConfigureAwait(false);
    }

    private async Task GenerateChainOfThoughtAsync()
    {
        logger.LogInformation("Starting Chain of Thought generation.");
        var watch = Stopwatch.StartNew();

        if (!_lastStoredUserMessage.Equals(_lastUserMessage!.Content, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_lastStoredCoTMessage))
        {
            var chatMessages = _completionRequest!.Messages.Select<Message, ChatMessage>(message => message.Role.ToLowerInvariant() switch
            {
                "system" => ChatMessage.CreateSystemMessage(message.Content),
                "user" => ChatMessage.CreateUserMessage(message.Content),
                "assistant" => ChatMessage.CreateAssistantMessage(message.Content),
                _ => throw new ArgumentException($"Invalid role: {message.Role}")
            }).ToList();

            var templatePrompt = string.IsNullOrWhiteSpace(_extensionSettings!.CotPrompt) ? _settings.Prompt : _extensionSettings.CotPrompt!;
            var cotPrompt = templatePrompt.Replace("{character}", _extensionSettings!.Character ?? "Character").Replace("{username}", _extensionSettings!.Username ?? "User");
            chatMessages.Add(ChatMessage.CreateUserMessage(cotPrompt));

            ChatCompletion cotResponse = await chatClient.CompleteChatAsync(chatMessages, null, _combinedToken);

            _lastStoredCoTMessage = $"<chain_of_thought>{cotResponse.Content[0].Text}</chain_of_thought>";
            await Task.WhenAll(File.WriteAllTextAsync("lastStoredUserMessage.txt", _lastUserMessage.Content, _combinedToken), File.WriteAllTextAsync("lastStoredCoTMessage.txt", _lastStoredCoTMessage, _combinedToken));
        }

        var extendedMessages = new List<Message>(_completionRequest!.Messages);
        if (!extendedMessages.Last().Role.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            extendedMessages.Add(new Message { Content = _settings.Prefill, Role = "user" });
        }

        extendedMessages.Add(new Message { Content = _lastStoredCoTMessage, Role = "assistant" });
        extendedMessages.Add(new Message { Content = _settings.Postfill, Role = "user" });
        
        _extendedMessages = extendedMessages.ToArray();
        
        watch.Stop();
        logger.LogInformation("Finished Chain of Thought generation. Generation took {timeMs} ms (or {timeSec} s).", watch.ElapsedMilliseconds, watch.ElapsedMilliseconds / 1000);
    }

    public async Task<IResult> CompletionAsync(HttpContext context)
    {
        var headers = context.Request.Headers;
        Utility.SetHeaders(_httpClient, headers);
        logger.LogInformation("CompletionAsync was called.");

        var cancellationToken = context.RequestAborted;
        var forceAbortToken = new CancellationTokenSource();
        _combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, forceAbortToken.Token).Token;

        var request = context.Request;
        request.EnableBuffering();

        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8, false, leaveOpen: true);
            var body = await reader.ReadToEndAsync(_combinedToken);
            request.Body.Position = 0;

            switch (_settings.Inference.CotHandler)
            {
            case Handler.MistralAi:
                _completionRequest = JsonSerializer.Deserialize<MistralCompletionRequest>(body, _jsonOptions);
                break;

            case Handler.OpenRouter:
                _completionRequest = JsonSerializer.Deserialize<OpenRouterCompletionRequest>(body, _jsonOptions);
                break;

            case Handler.TabbyApi:
                throw new NotImplementedException("TabbyAPI fallback handler is not implemented yet.");
            
            default:
                _completionRequest = JsonSerializer.Deserialize<BaseCompletionRequest>(body, _jsonOptions);
                break;
            }

            _extensionSettings = JsonSerializer.Deserialize<ExtensionSettings>(body, _jsonOptions);
            
            if (_completionRequest == null || _extensionSettings == null || _completionRequest.Messages.Length <= 0)
            {
                logger.LogError("Failed to deserialize request body or messages are null.");
                return Results.InternalServerError();
            }

            _lastUserMessage = _completionRequest.Messages.LastOrDefault(m => m.Role.Equals("User", StringComparison.OrdinalIgnoreCase));
            if (_lastUserMessage == null)
            {
                return Results.InternalServerError();
            }

            var results = await Task.WhenAll(File.ReadAllTextAsync("lastStoredUserMessage.txt", _combinedToken).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : string.Empty, _combinedToken), File.ReadAllTextAsync("lastStoredCoTMessage.txt", _combinedToken).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : string.Empty, _combinedToken));

            _lastStoredUserMessage = results[0];
            _lastStoredCoTMessage = results[1];

            await Task.WhenAll(IsAliveAsync(), GenerateChainOfThoughtAsync());

            if (_settings.Logging is { SaveFull: true, SaveCoT: true })
            {
                chatContext.ChainOfThoughts.Add(new ChainOfThought { Content = _lastStoredCoTMessage });
                chatContext.Chats.Add(new Chat { ChatMessages = _extendedMessages.Select(m => new DbChatMessage { Content = m.Content, Role = m.Role }).ToList() });
                await chatContext.SaveChangesAsync(_combinedToken);
            }
            else if (_settings.Logging.SaveCoT)
            {
                chatContext.ChainOfThoughts.Add(new ChainOfThought { Content = _lastStoredCoTMessage });
                await chatContext.SaveChangesAsync(_combinedToken);
            }
            else if (_settings.Logging.SaveFull)
            {
                chatContext.Chats.Add(new Chat { ChatMessages = _extendedMessages.Select(m => new DbChatMessage { Content = m.Content, Role = m.Role }).ToList() });
                await chatContext.SaveChangesAsync(_combinedToken);
            }

            _completionRequest.Messages = _extendedMessages;
            
            
            using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");

            _httpClient = httpClientFactory.CreateClient("PrimaryClient");
            if (!_isAlive && _settings.Inference.UseFallback)
            {
                logger.LogInformation("Primary inference endpoint offline and fallback enabled, switching to fallback endpoint.");
                switch (_settings.Inference.CotHandler)
                {
                case Handler.MistralAi:
                    logger.LogInformation("Using Mistral AI as fallback.");
                    _httpClient.BaseAddress = new Uri("https://api.mistral.ai/");
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.MistralAiSettings!.ApiKey);
                    _httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.MistralAiSettings!.ApiKey);
                    _completionRequest.Model = _extensionSettings.Model ?? _settings.Inference.MistralAiSettings!.Model;
                    break;

                case Handler.OpenRouter:
                    logger.LogInformation("Using OpenRouter as fallback.");
                    proxyRequest.RequestUri = new Uri("/api/v1/chat/completions", UriKind.Relative);
                    _httpClient.BaseAddress = new Uri("https://openrouter.ai/");
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.OpenRouterSettings!.ApiKey);
                    _httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.OpenRouterSettings!.ApiKey);
                    _completionRequest.Model = _extensionSettings.Model ?? _settings.Inference.OpenRouterSettings!.Model;
                    break;

                case Handler.TabbyApi:
                    throw new NotImplementedException("TabbyAPI fallback handler is not implemented yet.");
                }
            }
            else
            {
                Utility.SetHeaders(_httpClient, headers);
            }

            proxyRequest.Content = new StringContent(JsonSerializer.Serialize(_completionRequest), Encoding.UTF8, "application/json");

            if (_completionRequest.Stream)
            {
                logger.LogInformation("Generating final streaming response.");
                var response = await _httpClient.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead, _combinedToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Received non-success status code {statusCode} with message \"{reason}\"", (int) response.StatusCode, response.ReasonPhrase);
                    return Results.StatusCode((int) response.StatusCode);
                }

                var stream = await response.Content.ReadAsStreamAsync(_combinedToken);

                context.Response.OnCompleted(() =>
                {
                    forceAbortToken.Cancel();
                    return Task.CompletedTask;
                });

                return Results.Stream(stream);
            }
            else
            {
                logger.LogInformation("Generating final non-streaming response.");
                var response = await _httpClient.SendAsync(proxyRequest, _combinedToken);

                if (!response.IsSuccessStatusCode)
                {
                    return Results.StatusCode((int) response.StatusCode);
                }

                var responseContent = await response.Content.ReadAsStringAsync(_combinedToken);
                return Results.Text(responseContent, "application/json");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
