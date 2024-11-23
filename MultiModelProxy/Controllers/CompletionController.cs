// MultiModelProxy - CompletionController.cs
// Created on 2024.11.18
// Last modified at 2024.11.19 13:11

namespace MultiModelProxy.Controllers;

#region
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Context;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using Mistral.SDK;
using Mistral.SDK.DTOs;
using Models;
using ChatMessage = Mistral.SDK.DTOs.ChatMessage;
using DbChatMessage = Models.ChatMessage;
#endregion

public class CompletionController(
    ILogger<CompletionController> logger,
    IOptions<Settings> settings,
    IHttpClientFactory httpClientFactory,
    MistralClient mistralClient,
    ChatContext chatContext
)
{
    private readonly Settings _settings = settings.Value;
    private static readonly RecyclableMemoryStreamManager _streamManager = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, DefaultBufferSize = 4096 };
    
    public async Task<IResult> CompletionAsync(HttpContext context)
    {
        var httpClient = httpClientFactory.CreateClient("PrimaryClient");
        var headers = context.Request.Headers;
        var request = context.Request;
        logger.LogInformation("CompletionAsync was called.");

        Utility.SetHeaders(httpClient, headers);

        if (!await Utility.IsAliveAsync(httpClient).ConfigureAwait(false))
        {
            logger.LogError("Primary inference endpoint is offline.");
            return Results.InternalServerError();
        }

        request.EnableBuffering();

        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8, false, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            var tabbyRequest = JsonSerializer.Deserialize<TabbyCompletionRequest>(body, _jsonOptions);

            if (tabbyRequest?.Messages == null)
            {
                logger.LogError("Failed to deserialize request body or messages are null.");
                return Results.InternalServerError();
            }

            var lastUserMessage = tabbyRequest.Messages.LastOrDefault(m => m.Role.Equals("User", StringComparison.OrdinalIgnoreCase));
            if (lastUserMessage == null)
            {
                return Results.InternalServerError();
            }

            var results = await Task.WhenAll([
                File.ReadAllTextAsync("lastStoredUserMessage.txt").ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : string.Empty),
                File.ReadAllTextAsync("lastStoredCoTMessage.txt").ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : string.Empty)
            ]);

            var lastStoredUserMessage = results[0];
            var lastStoredCoTMessage = results[1];

            var extendedMessages = new List<Message>(tabbyRequest.Messages.Count + 3);
            extendedMessages.AddRange(tabbyRequest.Messages);
            
            if (!lastStoredUserMessage.Equals(lastUserMessage.Content, StringComparison.OrdinalIgnoreCase))
            {
                var cotPrompt = _settings.Prompt.Replace("{character}", tabbyRequest.Character ?? "Character").Replace("{username}", tabbyRequest.Character ?? "user");
                var cotMessages = new List<Message>(tabbyRequest.Messages) { new() { Content = cotPrompt, Role = "User" } };

                var chatMessages = cotMessages.Select(m => new ChatMessage(m.Role.ToLowerInvariant() switch
                {
                    "system" => ChatMessage.RoleEnum.System,
                    "user" => ChatMessage.RoleEnum.User,
                    "assistant" => ChatMessage.RoleEnum.Assistant,
                    _ => throw new ArgumentException($"Invalid role: {m.Role}")
                }, m.Content)).ToList();

                var cotRequest = new ChatCompletionRequest(ModelDefinitions.MistralLarge, chatMessages);
                var cotResponse = await mistralClient.Completions.GetCompletionAsync(cotRequest);

                lastStoredCoTMessage = $"<chain_of_thought>{cotResponse.Choices.First().Message.Content}</chain_of_thought>";
                await Task.WhenAll([
                    File.WriteAllTextAsync("lastStoredUserMessage.txt", lastUserMessage.Content),
                    File.WriteAllTextAsync("lastStoredCoTMessage.txt", lastStoredCoTMessage)
                ]);
            }

            if (!extendedMessages.Last().Role.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                extendedMessages.Add(new Message { Content = _settings.Prefill, Role = "user" });
            }

            extendedMessages.Add(new Message { Content = lastStoredCoTMessage, Role = "assistant" });
            extendedMessages.Add(new Message { Content = _settings.Postfill, Role = "user" });
            
            if (_settings.Logging is { SaveFull: true, SaveCoT: true })
            {
                chatContext.ChainOfThoughts.Add(new ChainOfThought { Content = lastStoredCoTMessage });
                chatContext.Chats.Add(new Chat { ChatMessages = extendedMessages.Select(m => new DbChatMessage { Content = m.Content, Role = m.Role }).ToList() });
                await chatContext.SaveChangesAsync();
            } else if (_settings.Logging.SaveCoT)
            {
                chatContext.ChainOfThoughts.Add(new ChainOfThought { Content = lastStoredCoTMessage });
                await chatContext.SaveChangesAsync();
            } else if (_settings.Logging.SaveFull)
            {
                chatContext.Chats.Add(new Chat { ChatMessages = extendedMessages.Select(m => new DbChatMessage { Content = m.Content, Role = m.Role }).ToList() });
                await chatContext.SaveChangesAsync();
            }

            var jsonContent = JsonNode.Parse(body);
            if (jsonContent == null)
            {
                return Results.InternalServerError();
            }

            jsonContent["messages"] = JsonNode.Parse(JsonSerializer.Serialize(extendedMessages));
            using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
            proxyRequest.Content = new StringContent(jsonContent.ToJsonString(), Encoding.UTF8, "application/json");

            if (tabbyRequest.Stream)
            {
                var response = await httpClient.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    return Results.StatusCode((int) response.StatusCode);
                }

                var responseContent = await response.Content.ReadAsByteArrayAsync();
                var cancellationToken = context.RequestAborted;
                var forceAbortToken = new CancellationTokenSource();
                var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, forceAbortToken.Token).Token;

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
                        await memoryStream.WriteAsync(responseContent, combinedToken);
                        memoryStream.Position = 0;
                        await memoryStream.CopyToAsync(stream, combinedToken);
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogInformation("Stream was forcefully aborted");
                    }
                });
            }
            else
            {
                var response = await httpClient.SendAsync(proxyRequest);

                if (!response.IsSuccessStatusCode)
                {
                    return Results.StatusCode((int) response.StatusCode);
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                return Results.Text(responseContent, "application/json");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
