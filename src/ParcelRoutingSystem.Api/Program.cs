using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ParcelRoutingSystem.Api.Configuration;
using ParcelRoutingSystem.Api.Http;
using ParcelRoutingSystem.Api.Security;

var builder = WebApplication.CreateBuilder(args);

string? externalConnection = Environment.GetEnvironmentVariable(
    "PARCEL_ROUTING_DATABASE_CONNECTION");
if (!string.IsNullOrWhiteSpace(externalConnection))
{
    builder.Configuration[
        $"{ParcelRoutingDatabaseOptions.SectionName}:ConnectionString"] =
        externalConnection;
}

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddParcelRoutingServices(builder.Configuration);
builder.Services.AddParcelRoutingSecurity(
    builder.Configuration,
    builder.Environment);
builder.Services.AddParcelRoutingRateLimits(builder.Configuration);
builder.Services.AddParcelRoutingForwardedHeaders(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks(
        "/health/live",
        new HealthCheckOptions
        {
            Predicate = _ => false,
        })
    .AllowAnonymous();
app.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        })
    .AllowAnonymous();

await app.ApplyDevelopmentMigrationsAsync();
app.Run();

/// <summary>
/// Exposes the composed web host to integration tests without changing the
/// production entry point.
/// </summary>
public partial class Program;
