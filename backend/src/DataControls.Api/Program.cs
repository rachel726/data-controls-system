using DataControls.Api.ErrorHandling;
using DataControls.Application;
using DataControls.Core.Definitions;
using DataControls.Infrastructure;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

const string DevCorsPolicy = "dev-frontend";

// Requests are small (a control definition is a few KB): anything bigger is refused before it is read.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024 * 1024);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers()
    // One JSON contract for the whole API: the same rules as stored control definitions (camelCase string enums, strict).
    .AddJsonOptions(o => DefinitionSerializer.ApplyContract(o.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = InvalidRequest.Problem);

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    c.ProblemDetails.Extensions.TryAdd("traceId", c.HttpContext.TraceIdentifier);
    // Status codes without an exception (unknown address, wrong method...) get a Hebrew title too.
    if (c.Exception is null && c.ProblemDetails.Status is { } status && InvalidRequest.StatusTitles.TryGetValue(status, out var title))
    {
        c.ProblemDetails.Title = title;
    }
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(DevCorsPolicy);
}

app.MapControllers();

app.Run();

/// <summary>Entry point marker, used by integration tests (WebApplicationFactory).</summary>
public partial class Program;
