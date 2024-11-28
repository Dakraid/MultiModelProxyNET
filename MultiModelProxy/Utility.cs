// MultiModelProxy - Utility.cs
// Created on 2024.11.18
// Last modified at 2024.11.19 13:11

namespace MultiModelProxy;

#region Usings
using System.Net.Http.Headers;
#endregion

public static class Utility
{
    public static async ValueTask<bool> IsAliveAsync(HttpClient httpClient)
    {
        try {
            var result = await httpClient.GetAsync("/health");
            return result.IsSuccessStatusCode;
        }
        catch (TaskCanceledException exception)
        {
            return false;
        }
    }

    public static void SetHeaders(HttpClient httpClient, IHeaderDictionary headers)
    {
        if (headers.TryGetValue("Authorization", out var authHeader))
        {
            var authHeaderValue = authHeader.ToString();
            if (authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeaderValue["Bearer ".Length..].Trim();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", authHeaderValue);
            }
        }

        if (headers.TryGetValue("x-api-key", out var xApiKeyHeader))
        {
            httpClient.DefaultRequestHeaders.Add("x-api-key", xApiKeyHeader.ToString());
        }
    }
}
