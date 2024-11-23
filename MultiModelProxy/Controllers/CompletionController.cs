// MultiModelProxy - CompletionController.cs
// Created on 2024.11.18
// Last modified at 2024.11.19 13:11

namespace MultiModelProxy.Controllers;

#region
using System.Buffers;
using System.ClientModel;
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
    private readonly Settings _settings = settings.Value;
    private static readonly RecyclableMemoryStreamManager _streamManager = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, DefaultBufferSize = 4096 };
    private bool _isAlive;
    private HttpClient _httpClient = httpClientFactory.CreateClient("PrimaryClient");
    private CancellationToken _combinedToken = CancellationToken.None;
    private TabbyCompletionRequest? _tabbyRequest = new();
    private Message? _lastUserMessage = new();
    private string _lastStoredUserMessage = string.Empty;
    private string _lastStoredCoTMessage = string.Empty;
    private List<Message> _extendedMessages = [];

    private async Task IsAliveAsync() => _isAlive = await Utility.IsAliveAsync(_httpClient).ConfigureAwait(false);

    private async Task GenerateChainOfThoughtAsync()
    {
        if (_tabbyRequest?.Messages == null || _lastUserMessage == null)
        {
            throw new ArgumentNullException();
        }
        
        _extendedMessages = new List<Message>(_tabbyRequest.Messages.Count + 3);
        _extendedMessages.AddRange(_tabbyRequest.Messages);
        
        if (!_lastStoredUserMessage.Equals(_lastUserMessage.Content, StringComparison.OrdinalIgnoreCase))
        {
            var cotPrompt = _settings.Prompt!.Replace("{character}", _tabbyRequest.Character ?? "Character").Replace("{username}", _tabbyRequest.Character ?? "user");
            var cotMessages = new List<Message>(_tabbyRequest.Messages) { new() { Content = cotPrompt, Role = "User" } };

            var chatMessages = cotMessages.Select<Message, ChatMessage>(message => message.Role.ToLowerInvariant() switch
            {
                "system" => ChatMessage.CreateSystemMessage(message.Content),
                "user" => ChatMessage.CreateUserMessage(message.Content),
                "assistant" => ChatMessage.CreateAssistantMessage(message.Content),
                _ => throw new ArgumentException($"Invalid role: {message.Role}")
            }).ToList();

            ChatCompletion cotResponse = await chatClient.CompleteChatAsync(chatMessages, null, _combinedToken);

            _lastStoredCoTMessage = $"<chain_of_thought>{cotResponse.Content[0].Text}</chain_of_thought>";
            await Task.WhenAll([
                File.WriteAllTextAsync("lastStoredUserMessage.txt", _lastUserMessage.Content, _combinedToken),
                File.WriteAllTextAsync("lastStoredCoTMessage.txt", _lastStoredCoTMessage, _combinedToken)
            ]);
        }

        if (!_extendedMessages.Last().Role.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            _extendedMessages.Add(new Message { Content = _settings.Prefill, Role = "user" });
        }

        _extendedMessages.Add(new Message { Content = _lastStoredCoTMessage, Role = "assistant" });
        _extendedMessages.Add(new Message { Content = _settings.Postfill, Role = "user" });
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

            _tabbyRequest = JsonSerializer.Deserialize<TabbyCompletionRequest>(body, _jsonOptions);
            if (_tabbyRequest?.Messages == null)
            {
                logger.LogError("Failed to deserialize request body or messages are null.");
                return Results.InternalServerError();
            }

            _lastUserMessage = _tabbyRequest.Messages.LastOrDefault(m => m.Role.Equals("User", StringComparison.OrdinalIgnoreCase));
            if (_lastUserMessage == null)
            {
                return Results.InternalServerError();
            }

            var results = await Task.WhenAll([
                File.ReadAllTextAsync("lastStoredUserMessage.txt", _combinedToken).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : string.Empty, _combinedToken),
                File.ReadAllTextAsync("lastStoredCoTMessage.txt", _combinedToken).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : string.Empty, _combinedToken)
            ]);

            _lastStoredUserMessage = results[0];
            _lastStoredCoTMessage = results[1];
            
            await Task.WhenAll([
                IsAliveAsync(),
                GenerateChainOfThoughtAsync()
            ]);
            
            if (_settings.Logging is { SaveFull: true, SaveCoT: true })
            {
                chatContext.ChainOfThoughts.Add(new ChainOfThought { Content = _lastStoredCoTMessage });
                chatContext.Chats.Add(new Chat { ChatMessages = _extendedMessages.Select(m => new DbChatMessage { Content = m.Content, Role = m.Role }).ToList() });
                await chatContext.SaveChangesAsync(_combinedToken);
            } else if (_settings.Logging.SaveCoT)
            {
                chatContext.ChainOfThoughts.Add(new ChainOfThought { Content = _lastStoredCoTMessage });
                await chatContext.SaveChangesAsync(_combinedToken);
            } else if (_settings.Logging.SaveFull)
            {
                chatContext.Chats.Add(new Chat { ChatMessages = _extendedMessages.Select(m => new DbChatMessage { Content = m.Content, Role = m.Role }).ToList() });
                await chatContext.SaveChangesAsync(_combinedToken);
            }

            var jsonContent = JsonNode.Parse(body);
            if (jsonContent == null)
            {
                return Results.InternalServerError();
            }

            jsonContent["messages"] = JsonNode.Parse(JsonSerializer.Serialize(_extendedMessages));
            using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
            proxyRequest.Content = new StringContent(jsonContent.ToJsonString(), Encoding.UTF8, "application/json");

            _httpClient = httpClientFactory.CreateClient("PrimaryClient");
            if (!_isAlive && _settings.Inference.UseFallback)
            {
                switch (_settings.Inference.CotHandler)
                {
                case Handler.MistralAi:
                    _httpClient.BaseAddress = new Uri("https://api.mistral.ai/v1");
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.MistralAiSettings!.ApiKey);
                    _httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.MistralAiSettings!.ApiKey);
                    var mistralAiCompletionRequest = new CompletionRequest
                    {
                        Model = _settings.Inference.MistralAiSettings!.Model, Stream = _tabbyRequest.Stream, Messages = _extendedMessages.ToArray()
                    };

                    proxyRequest.Content = new StringContent(JsonSerializer.Serialize(mistralAiCompletionRequest), Encoding.UTF8, "application/json");
                    break;

                case Handler.OpenRouter:
                    _httpClient.BaseAddress = new Uri("https://openrouter.ai/api/v1");
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.OpenRouterSettings!.ApiKey);
                    _httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.OpenRouterSettings!.ApiKey);
                    var openRouterCompletionRequest = new CompletionRequest
                    {
                        Model = _settings.Inference.OpenRouterSettings!.Model, Stream = _tabbyRequest.Stream, Messages = _extendedMessages.ToArray()
                    };

                    proxyRequest.Content = new StringContent(JsonSerializer.Serialize(openRouterCompletionRequest), Encoding.UTF8, "application/json");
                    break;

                case Handler.TabbyApi:
                    _httpClient.BaseAddress = new Uri(_settings.Inference.TabbyApiSettings!.BaseUri!);
                    break;
                }
            }
            
            if (_tabbyRequest.Stream)
            {
                var response = await _httpClient.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead, _combinedToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Received non-success status code {statusCode} with message \"{reason}\"", (int) response.StatusCode,  response.ReasonPhrase);
                    return Results.StatusCode((int) response.StatusCode);
                }

                var responseContent = await response.Content.ReadAsByteArrayAsync(_combinedToken);

                context.Response.OnCompleted(() =>
                {
                    forceAbortToken.Cancel();
                    return Task.CompletedTask;
                });

                return Results.Stream(async stream =>
                {
                    try
                    {
                        await using var memoryStream = _streamManager.GetStream();
                        await memoryStream.WriteAsync(responseContent, _combinedToken);
                        memoryStream.Position = 0;
                        await memoryStream.CopyToAsync(stream, _combinedToken);
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogInformation("Stream was forcefully aborted");
                    }
                });
            }
            else
            {
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
