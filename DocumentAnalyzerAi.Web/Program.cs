using DocumentAnalyzer.Core.Abstractions;
using DocumentAnalyzer.Core.Services;
using DocumentAnalyzerAi.Web.Requests;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAnalyzerAi.Web
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<IDocumentsAgent, DocumentsAgent>();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.SetIsOriginAllowed(origin => true)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                });
            });
            var app = builder.Build();
            app.UseStaticFiles();

            app.MapGet("/", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync("wwwroot/index.html");
            });

            app.MapPost("/send", async ([FromBody]MessageRequest request, 
                [FromServices]IDocumentsAgent agentService,
                CancellationToken token) =>
            {
                try
                {
                if (request is null)
                    return Results.BadRequest("Сообщение не должно быть пустым");
                    string? result = await agentService.SendMessageAsync(request.Message, token);
                    return Results.Text(result ?? "", "text/plain; charset=utf-8");
                }
                catch
                {
                    return Results.InternalServerError();
                }
            });

            app.Run();
        }
    }
}
