// MultiModelProxy - Program.cs
// Created on 2024.11.18
// Last modified at 2024.11.19 13:11

namespace MultiModelProxy;

#region
using Context;
using Controllers;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mistral.SDK;
#endregion

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.Configure<RouteOptions>(options => options.SetParameterPolicy<RegexInlineRouteConstraint>("regex"));
        builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));

        builder.Services.AddDbContextPool<ChatContext>(opt => opt.UseNpgsql(builder.Configuration.GetConnectionString("ChatContext")));
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddHttpClient("PrimaryClient", (serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<Settings>>().Value;
            client.BaseAddress = new Uri(settings.Inference.PrimaryEndpoint);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).SetHandlerLifetime(TimeSpan.FromMinutes(5)).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10), MaxConnectionsPerServer = 100, EnableMultipleHttp2Connections = true
        });

        builder.Services.AddScoped<MistralClient>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<Settings>>().Value;
            return new MistralClient(settings.Inference.MistralAiSettings.ApiKey);
        });

        builder.Services.AddScoped<GenericProxyController>();
        builder.Services.AddScoped<CompletionController>();

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

        // Generic Proxy Endpoints
        app.MapGet("/{*path}", async (HttpContext context, GenericProxyController controller, string path) => await controller.GenericGetAsync(context, path));
        app.MapPost("/{*path}", async (HttpContext context, GenericProxyController controller, string path) => await controller.GenericPostAsync(context, path));
        app.Run();
    }
}
