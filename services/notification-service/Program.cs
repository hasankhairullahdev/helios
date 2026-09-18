using NotificationService.Messaging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

// Bootstrap Serilog sebelum builder — tangkap log dari proses startup
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, config) =>
    config.ReadFrom.Configuration(ctx.Configuration)
          .Enrich.FromLogContext()
          .WriteTo.Console(new CompactJsonFormatter())
);

// ============================================================
// OPENTELEMETRY
// ============================================================
var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("notification-service"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrEmpty(otelEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrEmpty(otelEndpoint))
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
        metrics.AddPrometheusExporter();
    });

// ============================================================
// BACKGROUND SERVICE — RabbitMQ Consumer
// ============================================================
// AddHostedService mendaftarkan RabbitMqConsumer sebagai background service.
// .NET akan otomatis start/stop dia bersamaan dengan lifecycle aplikasi.
builder.Services.AddHostedService<RabbitMqConsumer>();

var app = builder.Build();

app.MapPrometheusScrapingEndpoint();

// /healthz — Liveness: selalu healthy kalau proses masih jalan
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

// /readyz — Readiness: untuk notification-service, kita anggap ready
// kalau proses sudah jalan. Consumer punya retry loop sendiri untuk RabbitMQ.
// Di production lebih baik expose status koneksi RabbitMQ di sini.
app.MapGet("/readyz", () => Results.Ok(new { status = "ready" }));

var gitSha = Environment.GetEnvironmentVariable("GIT_SHA") ?? "local";
app.MapGet("/version", () => Results.Ok(new { version = gitSha }));

app.Run();
