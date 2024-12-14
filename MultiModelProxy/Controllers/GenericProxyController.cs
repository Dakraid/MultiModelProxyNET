// MultiModelProxy - GenericProxyController.cs
// Created on 2024.11.18
// Last modified at 2024.12.07 19:12

namespace MultiModelProxy.Controllers;

#region
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Models;
#endregion

public class GenericProxyController(ILogger<GenericProxyController> logger, IOptions<Settings> settings, IHttpClientFactory httpClientFactory)
{
    private readonly Settings _settings = settings.Value;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, DefaultBufferSize = 4096 };

    private async Task<bool> IsAliveAsync()
    {
        using var httpClient = httpClientFactory.CreateClient("PrimaryClient");
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        return await Utility.IsAliveAsync(httpClient).ConfigureAwait(false);
    }

    public async Task<IResult> GenericGetAsync(HttpContext context, string path)
    {
        var httpClient = httpClientFactory.CreateClient("PrimaryClient");
        var headers = context.Request.Headers;
        logger.LogInformation("GenericGet was called at: {path}", path);
        
        var isAlive = await IsAliveAsync();
        if (!isAlive && _settings.Inference.UseFallback)
        {
            logger.LogInformation("Primary inference endpoint offline and fallback enabled, switching to fallback endpoint.");
            switch (_settings.Inference.CotHandler)
            {
            case Handler.MistralAi:
                logger.LogInformation("Using Mistral AI as fallback.");
                httpClient.BaseAddress = new Uri("https://api.mistral.ai/");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.MistralAiSettings!.ApiKey);
                httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.MistralAiSettings!.ApiKey);
                break;

            case Handler.OpenRouter:
                logger.LogInformation("Using OpenRouter as fallback.");
                httpClient.BaseAddress = new Uri("https://openrouter.ai/api/");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.OpenRouterSettings!.ApiKey);
                httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.OpenRouterSettings!.ApiKey);
                break;

            case Handler.TabbyApi:
                throw new NotImplementedException("TabbyAPI fallback handler is not implemented yet.");
            }
        }
        else
        {
            Utility.SetHeaders(httpClient, headers);
        }

        var response = await httpClient.GetAsync(path);

        var content = await response.Content.ReadAsStringAsync();
        return Results.Text(content, response.Content.Headers.ContentType?.ToString(), statusCode: (int) response.StatusCode);
    }

    public async Task<IResult> GenericPostAsync(HttpContext context, string path)
    {
        var httpClient = httpClientFactory.CreateClient("PrimaryClient");
        var headers = context.Request.Headers;
        var request = context.Request;
        logger.LogInformation("GenericPost was called at: {path}", path);

        var isAlive = await IsAliveAsync();
        if (!isAlive && _settings.Inference.UseFallback)
        {
            logger.LogInformation("Primary inference endpoint offline and fallback enabled, switching to fallback endpoint.");
            switch (_settings.Inference.CotHandler)
            {
            case Handler.MistralAi:
                logger.LogInformation("Using Mistral AI as fallback.");
                httpClient.BaseAddress = new Uri("https://api.mistral.ai/");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.MistralAiSettings!.ApiKey);
                httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.MistralAiSettings!.ApiKey);
                break;

            case Handler.OpenRouter:
                logger.LogInformation("Using OpenRouter as fallback.");
                httpClient.BaseAddress = new Uri("https://openrouter.ai/api/");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Inference.OpenRouterSettings!.ApiKey);
                httpClient.DefaultRequestHeaders.Add("ApiKey", _settings.Inference.OpenRouterSettings!.ApiKey);
                break;

            case Handler.TabbyApi:
                throw new NotImplementedException("TabbyAPI fallback handler is not implemented yet.");
            }
        }
        else
        {
            Utility.SetHeaders(httpClient, headers);
        }

        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, false, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        request.Body.Position = 0;

        var tabbyRequest = JsonSerializer.Deserialize<BaseCompletionRequest>(body, _jsonOptions);

        if (tabbyRequest == null)
        {
            logger.LogError("Failed to deserialize request body.");
            return Results.InternalServerError();
        }

        if (tabbyRequest.Stream)
        {
            using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, path);
            proxyRequest.Content = new StreamContent(request.Body);

            var response = await httpClient.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                return Results.StatusCode((int) response.StatusCode);
            }

            return Results.Stream(async stream =>
            {
                await using var responseStream = await response.Content.ReadAsStreamAsync();
                await responseStream.CopyToAsync(stream);
            });
        }
        else
        {
            using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, path);
            proxyRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(proxyRequest);

            if (!response.IsSuccessStatusCode)
            {
                return Results.StatusCode((int) response.StatusCode);
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return Results.Text(responseContent, "application/json");
        }
    }
}
