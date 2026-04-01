using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Pos.SharedKernel.Observability;

public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing + metrics for ASP.NET Core, HttpClient, EF Core,
    /// and the custom Pos.Sync meter. Exports via OTLP when an endpoint is configured,
    /// otherwise falls back to console (dev-only).
    /// </summary>
    public static IServiceCollection AddPosObservability(
        this IServiceCollection services,
        string serviceName,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        services.AddSingleton<SyncMetrics>();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] =
                        configuration["ASPNETCORE_ENVIRONMENT"] ?? "Development"
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(opt =>
                    {
                        opt.RecordException = true;
                        // Exclude health check noise
                        opt.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource("Pos.Sync");

                if (!string.IsNullOrEmpty(otlpEndpoint))
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                else
                    tracing.AddConsoleExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(SyncMetrics.MeterName);

                if (!string.IsNullOrEmpty(otlpEndpoint))
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                else
                    metrics.AddConsoleExporter();
            });

        return services;
    }
}
