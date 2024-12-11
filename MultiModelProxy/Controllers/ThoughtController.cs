// MultiModelProxy - ThoughtController.cs
// Created on 2024.11.24
// Last modified at 2024.12.07 19:12

namespace MultiModelProxy.Controllers;

#region
using Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
#endregion

public class ThoughtController(ILogger<ThoughtController> logger, IOptions<Settings> settings, ChatContext chatContext)
{
    private readonly Settings _settings = settings.Value;

    public async Task<IResult> GetLastThoughtAsync(HttpContext context)
    {
        if (!_settings.Logging.SaveCoT)
        {
            return Results.BadRequest("Saving CoT is disabled in settings.");
        }

        var lastThought = await chatContext.ChainOfThoughts.OrderBy(x => x.Timestamp).LastAsync();
        var response = new { content = lastThought.Content };
        return Results.Json(response);
    }
}
