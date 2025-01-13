// MultiModelProxy - Program.cs
// Created on 2024.11.18
// Last modified at 2024.12.07 19:12

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
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
using Services;
#endregion

public class Program
{
    private static bool IsValidConfiguration(Settings? settings)
    {
        if (settings == null)
        {
            return false;
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Prompt))
        {
            return false;
        }

        // Validate LoggingSettings
        if (settings.Logging == null)
        {
            return false;
        }

        // Validate InferenceSettings
        if (settings.Inference == null)
        {
            return false;
        }

        // Validate PrimaryEndpoint
        if (string.IsNullOrWhiteSpace(settings.Inference.PrimaryEndpoint))
        {
            return false;
        }
        
        // Validate CotRotation
        if (settings.Inference.CoTRotation < 0)
        {
            return false;
        }

        // Validate Handler-specific settings
        switch (settings.Inference.CotHandler)
        {
            case Handler.MistralAi:
                if (settings.Inference.MistralAiSettings == null ||
                    string.IsNullOrWhiteSpace(settings.Inference.MistralAiSettings.ApiKey) ||
                    string.IsNullOrWhiteSpace(settings.Inference.MistralAiSettings.Model))
                {
                    return false;
                }
                break;

            case Handler.TabbyApi:
                if (settings.Inference.TabbyApiSettings == null ||
                    string.IsNullOrWhiteSpace(settings.Inference.TabbyApiSettings.BaseUri) ||
                    string.IsNullOrWhiteSpace(settings.Inference.TabbyApiSettings.ApiKey) ||
                    string.IsNullOrWhiteSpace(settings.Inference.TabbyApiSettings.Model))
                {
                    return false;
                }
                break;

            case Handler.OpenRouter:
                if (settings.Inference.OpenRouterSettings == null ||
                    string.IsNullOrWhiteSpace(settings.Inference.OpenRouterSettings.ApiKey) ||
                    string.IsNullOrWhiteSpace(settings.Inference.OpenRouterSettings.Model))
                {
                    return false;
                }
                break;

            default:
                return false;
        }

        // Validate FallbackModel
        return settings.Inference.FallbackModel != null && settings.Inference.FallbackModel.Length != 0;
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

        builder.Services.AddSingleton<ITrackerService, TrackerService>();
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
