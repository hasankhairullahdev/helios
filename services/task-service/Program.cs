using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Exporter.Prometheus;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using TaskService.Data;
using TaskService.Messaging;
using TaskService.Models;

// ============================================================
// SERILOG — Structured JSON Logging
// ============================================================
// Kenapa setup Serilog di sini sebelum builder.Build()?
// Supaya log dari proses startup juga tertangkap (error koneksi DB, dll).
// WriteTo.Console(CompactJsonFormatter) = output JSON satu baris per log,
// mudah di-scrape oleh log aggregator di cluster nanti.
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// Ganti default Microsoft logger dengan Serilog
builder.Host.UseSerilog((ctx, services, config) =>
    config.ReadFrom.Configuration(ctx.Configuration)
          .Enrich.FromLogContext()
          .WriteTo.Console(new CompactJsonFormatter())
);

// ============================================================
// DATABASE — EF Core + Postgres
// ============================================================
// Connection string dibaca dari environment variable (12-factor app principle).
// Format: "Host=postgres;Database=tasks;Username=app;Password=secret"
// JANGAN hardcode di sini — Helm/docker-compose yang inject nilainya.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["DATABASE_URL"]
    ?? "Host=localhost;Database=tasks;Username=app;Password=secret";

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// ============================================================
// OPENTELEMETRY — Traces + Metrics
// ============================================================
// OTEL_EXPORTER_OTLP_ENDPOINT dibaca dari env var — sesuai architecture.md.
// Kalau env var tidak ada (local dev), OTel tetap collect tapi tidak export
// ke mana-mana (tidak crash).
var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("task-service"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()   // auto-trace semua HTTP requests
            .AddEntityFrameworkCoreInstrumentation(); // auto-trace semua DB queries

        if (!string.IsNullOrEmpty(otelEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()   // http_server_request_duration (dipakai AnalysisTemplate nanti!)
            .AddRuntimeInstrumentation();     // CPU, memory, GC metrics

        if (!string.IsNullOrEmpty(otelEndpoint))
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));

        // Prometheus scrape endpoint — ServiceMonitor di Helm chart akan scrape ini
        metrics.AddPrometheusExporter();
    });

var app = builder.Build();

// Expose /metrics endpoint untuk Prometheus scraping
app.MapPrometheusScrapingEndpoint();

// ============================================================
// AUTO MIGRATE — jalankan migrasi DB saat startup
// ============================================================
// Wrapped dalam try/catch supaya service tidak crash kalau DB belum siap.
// Readiness probe (/readyz) yang akan signal ke Kubernetes bahwa pod belum
// siap terima traffic selama DB tidak bisa diakses — lebih elegant dari crash.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        Log.Information("Database migration completed successfully.");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Database migration failed — service will start but /readyz will return 503 until DB is reachable.");
    }
}

// ============================================================
// CHAOS INJECTION — Phase 8 demo
// ============================================================
// Setiap request ke-3 ke POST /tasks akan return 500.
// Error rate ~33% — jauh di atas threshold 1% di AnalysisTemplate.
// ⚠️  HAPUS BLOK INI setelah chaos demo selesai direkam.
var chaosCounter = 0;

// ============================================================
// RABBITMQ PUBLISHER
// ============================================================
// Dibuat setelah app.Build() karena butuh config yang sudah siap.
// RABBITMQ_HOST dari env var — default "localhost" untuk local dev.
var rabbitHost = builder.Configuration["RABBITMQ_HOST"] ?? "localhost";
ITaskEventPublisher? publisher = null;
try
{
    publisher = await RabbitMqTaskEventPublisher.CreateAsync(rabbitHost);
    Log.Information("Connected to RabbitMQ at {Host}", rabbitHost);
}
catch (Exception ex)
{
    // Kalau RabbitMQ belum siap, service tetap jalan — hanya event publish
    // yang akan gagal. Ini acceptable untuk local dev tanpa docker-compose.
    Log.Warning(ex, "Could not connect to RabbitMQ at {Host}. Events will not be published.", rabbitHost);
}

// ============================================================
// HEALTH PROBES
// ============================================================
// /healthz — Liveness: cukup return 200, tidak cek dependency eksternal.
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

// /readyz — Readiness: cek koneksi DB. Kalau DB tidak bisa diakses,
// pod ini tidak boleh dapat traffic.
app.MapGet("/readyz", async (AppDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ready" });
    }
    catch
    {
        return Results.Problem(
            title: "not ready",
            detail: "database unavailable",
            statusCode: StatusCodes.Status503ServiceUnavailable
        );
    }
});

// /version — Git SHA di-inject saat docker build via ARG GIT_SHA
var gitSha = Environment.GetEnvironmentVariable("GIT_SHA") ?? "local";
app.MapGet("/version", () => Results.Ok(new { version = gitSha }));

// ============================================================
// CRUD ENDPOINTS
// ============================================================

// GET /tasks — list semua tasks
app.MapGet("/tasks", async (AppDbContext db) =>
    await db.Tasks.OrderByDescending(t => t.CreatedAt).ToListAsync());

// GET /tasks/{id} — ambil satu task
app.MapGet("/tasks/{id}", async (int id, AppDbContext db) =>
    await db.Tasks.FindAsync(id) is TaskItem task
        ? Results.Ok(task)
        : Results.NotFound());

// POST /tasks — buat task baru + publish event TaskCreated
// ⚠️  CHAOS: setiap request ke-3 return 500 (Phase 8 demo — hapus setelah demo)
app.MapPost("/tasks", async (CreateTaskRequest req, AppDbContext db) =>
{
    var count = Interlocked.Increment(ref chaosCounter);
    if (count % 3 == 0)
    {
        Log.Warning("CHAOS: injecting 500 error on POST /tasks (request #{Count})", count);
        return Results.Problem(
            title: "chaos injection",
            detail: "Simulated failure for canary analysis demo",
            statusCode: StatusCodes.Status500InternalServerError
        );
    }

    var task = new TaskItem { Title = req.Title };
    db.Tasks.Add(task);
    await db.SaveChangesAsync();

    // Publish event ke RabbitMQ setelah task berhasil disimpan ke DB.
    // Kalau publish gagal, task tetap tersimpan — kita tidak rollback DB
    // hanya karena messaging gagal (eventual consistency).
    if (publisher is not null)
    {
        try
        {
            await publisher.PublishTaskCreatedAsync(task.Id, task.Title);
            Log.Information("Published TaskCreated event for task {TaskId}", task.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to publish TaskCreated event for task {TaskId}", task.Id);
        }
    }

    return Results.Created($"/tasks/{task.Id}", task);
});

// PATCH /tasks/{id} — update task (title dan/atau completion status)
app.MapPatch("/tasks/{id}", async (int id, UpdateTaskRequest req, AppDbContext db) =>
{
    var task = await db.Tasks.FindAsync(id);
    if (task is null) return Results.NotFound();

    if (req.Title is not null) task.Title = req.Title;
    if (req.IsCompleted is not null) task.IsCompleted = req.IsCompleted.Value;

    await db.SaveChangesAsync();
    return Results.Ok(task);
});

// DELETE /tasks/{id}
app.MapDelete("/tasks/{id}", async (int id, AppDbContext db) =>
{
    var task = await db.Tasks.FindAsync(id);
    if (task is null) return Results.NotFound();

    db.Tasks.Remove(task);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

// ============================================================
// REQUEST/RESPONSE RECORDS
// ============================================================
// Minimal record untuk input validation — .NET otomatis deserialize JSON body
record CreateTaskRequest(string Title);
record UpdateTaskRequest(string? Title, bool? IsCompleted);
