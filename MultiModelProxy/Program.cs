// MultiModelProxy - Program.cs
// Created on 2024.11.18
// Last modified at 2024.12.07 19:12

namespace MultiModelProxy;

#region
using System.ClientModel;
using Context;
using Controllers;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
#endregion

public class Program
{
    private static bool IsValidConfiguration(Settings? settings)
    {
        if (settings == null)
        {
            Console.WriteLine("Settings section not found in appsettings.json");
            return false;
        }

        if (settings.ApiKey == null || settings.Prompt == null)
        {
            Console.WriteLine("ApiKey or Prompt is not set in appsettings.json");
            return false;
        }

        if (settings.Inference.PrimaryEndpoint == null)
        {
            Console.WriteLine("PrimaryEndpoint is not set in appsettings.json");
            return false;
        }

        switch (settings.Inference.CotHandler)
        {
        case Handler.TabbyApi:
            if (settings.Inference.TabbyApiSettings == null)
            {
                Console.WriteLine("TabbyApiSettings is not set in appsettings.json");
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.Inference.TabbyApiSettings.BaseUri)
                || string.IsNullOrWhiteSpace(settings.Inference.TabbyApiSettings.ApiKey)
                || string.IsNullOrWhiteSpace(settings.Inference.TabbyApiSettings.Model))
            {
                Console.WriteLine("BaseUri, ApiKey or Model is not set in TabbyApiSettings in appsettings.json");
                return false;
            }

            break;

        case Handler.MistralAi:
            if (settings.Inference.MistralAiSettings == null)
            {
                Console.WriteLine("MistralAiSettings is not set in appsettings.json");
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.Inference.MistralAiSettings.ApiKey) || string.IsNullOrWhiteSpace(settings.Inference.MistralAiSettings.Model))
            {
                Console.WriteLine("ApiKey or Model is not set in MistralAiSettings in appsettings.json");
                return false;
            }

            break;

        case Handler.OpenRouter:
            if (settings.Inference.OpenRouterSettings == null)
            {
                Console.WriteLine("OpenRouterSettings is not set in appsettings.json");
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.Inference.OpenRouterSettings.ApiKey) || string.IsNullOrWhiteSpace(settings.Inference.OpenRouterSettings.Model))
            {
                Console.WriteLine("ApiKey or Model is not set in OpenRouterSettings in appsettings.json");
                return false;
            }

            break;

        default:
            return false;
        }

        return true;
    }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.Configure<RouteOptions>(options => options.SetParameterPolicy<RegexInlineRouteConstraint>("regex"));
        builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));

        var settings = builder.Configuration.GetSection("Settings").Get<Settings>();
        if (!IsValidConfiguration(settings))
        {
            throw new InvalidOperationException("Invalid configuration, please update your appsettings.json!");
        }

        builder.Services.AddDbContextPool<ChatContext>(opt => opt.UseNpgsql(builder.Configuration.GetConnectionString("ChatContext")));
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddHttpClient("PrimaryClient", (_, client) =>
        {
            client.BaseAddress = new Uri(settings!.Inference.PrimaryEndpoint!);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).SetHandlerLifetime(TimeSpan.FromMinutes(5)).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10), MaxConnectionsPerServer = 100, EnableMultipleHttp2Connections = true
        });

        switch (settings!.Inference.CotHandler)
        {
        case Handler.MistralAi:
            builder.Services.AddScoped<ChatClient>(_ => new ChatClient(model: settings.Inference.MistralAiSettings!.Model, credential: new ApiKeyCredential(
                settings.Inference.MistralAiSettings!.ApiKey), options: new OpenAIClientOptions { Endpoint = new Uri("https://api.mistral.ai/v1") }));
            break;

        case Handler.OpenRouter:
            builder.Services.AddScoped<ChatClient>(_ => new ChatClient(model: settings.Inference.OpenRouterSettings!.Model,
                credential: new ApiKeyCredential(settings.Inference.OpenRouterSettings!.ApiKey),
                options: new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") }));
            break;
        }

        builder.Services.AddScoped<GenericProxyController>();
        builder.Services.AddScoped<CompletionController>();
        builder.Services.AddScoped<ThoughtController>();

        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.AddMemoryCache();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseDeveloperExceptionPage();
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // app.UseHsts();
        }

        // Completion Proxy Endpoints
        app.MapPost("/v1/chat/completions", async (HttpContext context, CompletionController controller) => await controller.CompletionAsync(context));

        // Thoughts
        app.MapGet("/v1/thought", async (HttpContext context, ThoughtController controller) => await controller.GetLastThoughtAsync(context));

        // Generic Proxy Endpoints
        app.MapGet("/{*path}", async (HttpContext context, GenericProxyController controller, string path) => await controller.GenericGetAsync(context, path));
        app.MapPost("/{*path}", async (HttpContext context, GenericProxyController controller, string path) => await controller.GenericPostAsync(context, path));
        app.Run();
    }
}
