// MultiModelProxy - GenericProxyController.cs
// Created on 2024.11.18
// Last modified at 2024.11.19 13:11

namespace MultiModelProxy.Controllers;

#region
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Models;
#endregion

public class GenericProxyController(ILogger<GenericProxyController> logger, IOptions<Settings> settings, IHttpClientFactory httpClientFactory)
{
    private readonly Settings _settings = settings.Value;

    public async Task<IResult> GenericGetAsync(HttpContext context, string path)
    {
        var httpClient = httpClientFactory.CreateClient("PrimaryClient");
        var headers = context.Request.Headers;
        logger.LogInformation("GenericGet was called at: {path}", path);

        Utility.SetHeaders(httpClient, headers);

        var isAliveTask = Utility.IsAliveAsync(httpClient);
        if (!await isAliveTask)
        {
            logger.LogError("Primary inference endpoint is offline.");
            return Results.InternalServerError();
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

        Utility.SetHeaders(httpClient, headers);

        var isAliveTask = Utility.IsAliveAsync(httpClient);
        if (!await isAliveTask)
        {
            logger.LogError("Primary inference endpoint is offline.");
            return Results.InternalServerError();
        }

        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, false, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        request.Body.Position = 0;

        var tabbyRequest = JsonSerializer.Deserialize<TabbyCompletionRequest>(body, TabbyCompletionRequestContext.Default.TabbyCompletionRequest);

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
