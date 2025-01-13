// MultiModelProxy - CompletionController.cs
// Created on 2024.11.18
// Last modified at 2025.01.13 12:01

// ReSharper disable InconsistentNaming

namespace MultiModelProxy.Controllers;

#region
using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Context;
using Microsoft.Extensions.Options;
using Models;
using OpenAI.Chat;
using Services;
using ChatMessage = OpenAI.Chat.ChatMessage;
using DbChatMessage = Models.ChatMessage;
#endregion

public class CompletionController(
    ILogger<CompletionController> logger,
    IOptions<Settings> settings,
    IHttpClientFactory httpClientFactory,
    ChatClient chatClient,
    ChatContext chatContext,
    ITrackerService trackerService
)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, DefaultBufferSize = 4096 };
    private readonly Settings _settings = settings.Value;
    private CancellationToken _combinedToken = CancellationToken.None;
    private BaseCompletionRequest? _completionRequest;
    private Message[] _extendedMessages = [];
    private ExtensionSettings? _extensionSettings;
    private HttpClient _httpClient = httpClientFactory.CreateClient("PrimaryClient");
    private bool _isAlive;
    private Message? _lastUserMessage;

    private async ValueTask<bool> IsAliveAsync()
    {
        using var httpClient = httpClientFactory.CreateClient("PrimaryClient");
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        return await Utility.IsAliveAsync(httpClient).ConfigureAwait(false);
    }

    private bool IsValidRequest()
    {
        if (_completionRequest != null && _extensionSettings != null && _completionRequest.Messages.Length != 0)
        {
            return true;
        }

        logger.LogError("Failed to deserialize request body or messages are null.");
        return false;
    }

    private async ValueTask ExecuteLoggingAsync()
    {
        if (_settings.Logging is { SaveFull: true, SaveCoT: true })
        {
            chatContext.ChainOfThoughts.Add(new ChainOfThought { Content = trackerService.GetLastCotMessage() });
            chatContext.Chats.Add(new Chat { ChatMessages = _extendedMessages.Select(m => new DbChatMessage { Content = m.Content, Role = m.Role }).ToList() });
        }
        else if (_settings.Logging.SaveCoT)
        {
            chatContext.ChainOfThoughts.Add(new ChainOfThought { Content = trackerService.GetLastCotMessage() });
        }
        else if (_settings.Logging.SaveFull)
        {
            chatContext.Chats.Add(new Chat { ChatMessages = _extendedMessages.Select(m => new DbChatMessage { Content = m.Content, Role = m.Role }).ToList() });
        }

        await chatContext.SaveChangesAsync(_combinedToken);
    }

    private void OverwriteSettings()
    {
        if (_extensionSettings == null)
        {
            return;
        }

        if (_extensionSettings.ForceCoT.HasValue)
        {
            _settings.Inference.ForceCoT = _extensionSettings.ForceCoT.Value;
        }

        if (_extensionSettings.CotRotation.HasValue)
        {
            _settings.Inference.CoTRotation = _extensionSettings.CotRotation.Value;
        }

        if (!string.IsNullOrWhiteSpace(_extensionSettings.CotPrompt))
        {
            _settings.Prompt = _extensionSettings.CotPrompt;
        }

        if (_extensionSettings.FallbackHandler.HasValue)
        {
            _settings.Inference.FallbackHandler = _extensionSettings.FallbackHandler.Value;
        }

        if (_extensionSettings.FallbackModel is { Length: > 0 })
        {
            _settings.Inference.FallbackModel = _extensionSettings.FallbackModel;
        }
    }

    private async ValueTask<bool> GenerateChainOfThoughtAsync()
    {
        logger.LogInformation("Starting Chain of Thought generation.");
        var watch = Stopwatch.StartNew();

        var forceCoT = _settings.Inference.ForceCoT;
        var newUserMessage = !trackerService.GetLastUserMessage().Equals(_lastUserMessage!.Content, StringComparison.OrdinalIgnoreCase);
        var emptyCoT = string.IsNullOrWhiteSpace(trackerService.GetLastCotMessage());
        var cotRounds = _settings.Inference.CoTRotation;
        var newRound = cotRounds == 0 || trackerService.GetCoTRound() >= cotRounds;
        
        if (newUserMessage)
        {
            if (newRound)
            {
                logger.LogInformation("New CoT round, resetting counter.");
                trackerService.ResetCoTRound();
            }
            else
            {
                trackerService.IncrementCoTRound();
            }
        }

        logger.LogInformation("CoT rotation currently: {curr}/{limit}", trackerService.GetCoTRound(), cotRounds);

        if ((newRound && newUserMessage) || emptyCoT || forceCoT)
        {
            var chatMessages = _completionRequest!.Messages.Select<Message, ChatMessage>(message => message.Role.ToLowerInvariant() switch
            {
                "system" => ChatMessage.CreateSystemMessage(message.Content),
                "user" => ChatMessage.CreateUserMessage(message.Content),
                "assistant" => ChatMessage.CreateAssistantMessage(message.Content),
                _ => throw new ArgumentException($"Invalid role: {message.Role}")
            }).ToList();

            var templatePrompt = _settings.Prompt;
            var cotPrompt = templatePrompt.Replace("{character}", _extensionSettings!.Character ?? "Character").Replace("{username}", _extensionSettings!.Username ?? "User");
            chatMessages.Add(ChatMessage.CreateUserMessage(cotPrompt));

            ChatCompletion cotResponse = await chatClient.CompleteChatAsync(chatMessages, null, _combinedToken);

            if (cotResponse.Content.Count <= 0 || cotResponse.Content[0] == null)
            {
                if (!string.IsNullOrWhiteSpace(cotResponse.Refusal))
                {
                    logger.LogError("Received refusal from the chat completion. Refusal Message: {message}", cotResponse.Refusal);
                }
                else
                {
                    logger.LogError("Received no content from the chat completion. Content Object: {content}", cotResponse.Content);
                }
                watch.Stop();
                return false;
            }

            trackerService.SetLastCotMessage($"<chain_of_thought>{cotResponse.Content[0].Text}</chain_of_thought>");
            trackerService.SetLastUserMessage(_lastUserMessage.Content);
        }

        var extendedMessages = new List<Message>(_completionRequest!.Messages);
        if (!extendedMessages.Last().Role.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            extendedMessages.Add(new Message { Content = _settings.Prefill, Role = "user" });
        }

        extendedMessages.Add(new Message { Content = trackerService.GetLastCotMessage(), Role = "assistant" });
        extendedMessages.Add(new Message { Content = _settings.Postfill, Role = "user" });

        _extendedMessages = extendedMessages.ToArray();

        watch.Stop();
        logger.LogInformation("Finished Chain of Thought generation. Generation took {timeMs} ms (or {timeSec} s).", watch.ElapsedMilliseconds, watch.ElapsedMilliseconds / 1000);

        return true;
    }

    public async Task<IResult> CompletionAsync(HttpContext context)
    {
        logger.LogInformation("CompletionAsync was called.");

        var headers = context.Request.Headers;
        Utility.SetHeaders(_httpClient, headers);

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

            _completionRequest = _settings.Inference.CotHandler switch
            {
                Handler.MistralAi => JsonSerializer.Deserialize<MistralCompletionRequest>(body, _jsonOptions),
                Handler.OpenRouter => JsonSerializer.Deserialize<OpenRouterCompletionRequest>(body, _jsonOptions),
                Handler.TabbyApi => throw new NotImplementedException("TabbyAPI fallback handler is not implemented yet."),
                _ => JsonSerializer.Deserialize<BaseCompletionRequest>(body, _jsonOptions)
            };
            _extensionSettings = JsonSerializer.Deserialize<ExtensionSettings>(body, _jsonOptions);

            if (!IsValidRequest())
            {
                return Results.InternalServerError();
            }

            _lastUserMessage = _completionRequest!.Messages.LastOrDefault(m => m.Role.Equals("User", StringComparison.OrdinalIgnoreCase));
            if (_lastUserMessage == null)
            {
                return Results.InternalServerError();
            }

            OverwriteSettings();

            _isAlive = await IsAliveAsync();
            var generatedThought = await GenerateChainOfThoughtAsync();
            if (!generatedThought)
            {
                return Results.InternalServerError();
            }
            
            await ExecuteLoggingAsync();

            _completionRequest.Messages = _extendedMessages;

            using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");

            _httpClient = httpClientFactory.CreateClient("PrimaryClient");
            if (!_isAlive && _settings.Inference.UseFallback)
            {
                logger.LogInformation("Primary inference endpoint offline and fallback enabled, switching to fallback endpoint.");

                var round = trackerService.GetResponseRound();
                var model = _settings.Inference.FallbackModel[round];
                logger.LogInformation("Selected model: {model}", model);
                var limit = _settings.Inference.FallbackModel.Length - 1;
                if (round >= limit)
                {
                    trackerService.ResetResponseRound();
                    logger.LogInformation("Reset response rotation.");
                }
                else
                {
                    trackerService.IncrementResponseRound();
                }

                logger.LogInformation("Response rotation currently: {curr}/{limit}", trackerService.GetResponseRound(), limit);

                _completionRequest.Model = model;

                switch (_settings.Inference.FallbackHandler)
                {
                    case Handler.MistralAi:
                        logger.LogInformation("Using Mistral AI as fallback.");
                        _httpClient.BaseAddress = new Uri("https://api.mistral.ai/");
                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.MistralAiSettings!.ApiKey);
                        _httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.MistralAiSettings!.ApiKey);
                        break;

                    case Handler.OpenRouter:
                        logger.LogInformation("Using OpenRouter as fallback.");
                        proxyRequest.RequestUri = new Uri("/api/v1/chat/completions", UriKind.Relative);
                        _httpClient.BaseAddress = new Uri("https://openrouter.ai/");
                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.OpenRouterSettings!.ApiKey);
                        _httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.OpenRouterSettings!.ApiKey);
                        break;

                    case Handler.TabbyApi:
                        throw new NotImplementedException("TabbyAPI fallback handler is not implemented yet.");
                    
                    default:
                        throw new ArgumentException("Unknown fallback handler provided.");
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
                    logger.LogWarning("Received non-success status code {statusCode} with message \"{reason}\"", (int)response.StatusCode, response.ReasonPhrase);
                    return Results.StatusCode((int)response.StatusCode);
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
                    return Results.StatusCode((int)response.StatusCode);
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
