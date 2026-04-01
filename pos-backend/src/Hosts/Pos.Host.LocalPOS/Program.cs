using Microsoft.EntityFrameworkCore;
using Pos.Orders.Core;
using Pos.Payments.Core;
using Pos.Products.Core;
using Pos.SharedKernel.Events;
using Pos.SharedKernel.Sync;
using Pos.SharedKernel.Health;
using Pos.SharedKernel.Observability;
using Pos.Infrastructure.Postgres;
using Pos.Orders.Core.Data;
using Pos.Products.Core.Data;
using Pos.Payments.Core.Data;
using Pos.Host.LocalPOS.Auth;
using Pos.Host.LocalPOS.Sync;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Support running as Windows Service or Linux systemd unit
builder.Host.UseWindowsService();
builder.Host.UseSystemd();

// Structured logging: JSON in production, colored console in dev
builder.Host.ConfigureProductionLogging();

var connectionString = builder.Configuration.GetConnectionString("LocalDb")
    ?? "Host=localhost;Port=5432;Database=pos_local;Username=pos;Password=pos";

builder.Services.AddOrdersModule(connectionString);
builder.Services.AddProductsModule(connectionString);
builder.Services.AddPaymentsModule(connectionString);

// Sync outbox infrastructure
builder.Services.AddDbContext<SyncDbContext>(opt =>
    opt.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "sync")));
builder.Services.AddScoped<ISyncOutboxWriter, EfSyncOutboxWriter>();
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("Sync"));
builder.Services.AddHttpClient("SyncClient");
builder.Services.AddHostedService<SyncOutboxWorker>();

// Auth
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<TokenProvider>();

builder.Services.AddSingleton<IEventBus, InProcessEventBus>();

// Observability: OpenTelemetry tracing + metrics + structured logging
builder.Services.AddPosObservability("pos-localpos", builder.Configuration);

// Health checks: /health/live (liveness) and /health/ready (readiness)
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SyncDbContext>("sync_db", tags: ["ready"])
    .AddDbContextCheck<OrdersDbContext>("orders_db", tags: ["ready"])
    .AddDbContextCheck<ProductsDbContext>("products_db", tags: ["ready"])
    .AddDbContextCheck<PaymentsDbContext>("payments_db", tags: ["ready"])
    .AddCheck<OutboxBacklogHealthCheck>("outbox_backlog", tags: ["ready"]);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddCors(opt => opt.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// In dev: EnsureCreated; in production: RunMigrations
var env = app.Environment;
if (env.IsProduction())
{
    await MigrationRunner.RunMigrationsAsync<OrdersDbContext>(app.Services);
    await MigrationRunner.RunMigrationsAsync<ProductsDbContext>(app.Services);
    await MigrationRunner.RunMigrationsAsync<PaymentsDbContext>(app.Services);
    await MigrationRunner.RunMigrationsAsync<SyncDbContext>(app.Services);
}
else
{
    await MigrationRunner.EnsureCreatedAsync<OrdersDbContext>(app.Services);
    await MigrationRunner.EnsureCreatedAsync<ProductsDbContext>(app.Services);
    await MigrationRunner.EnsureCreatedAsync<PaymentsDbContext>(app.Services);
    await MigrationRunner.EnsureCreatedAsync<SyncDbContext>(app.Services);
}

app.MapOpenApi();
app.MapScalarApiReference();

app.UseCors();

app.MapOrdersEndpoints();
app.MapProductsEndpoints();
app.MapPaymentsEndpoints();

// /health/live — just checks the app is running (no DB deps)
app.MapHealthChecks("/health/live");

// /health/ready — checks DB connectivity + outbox backlog
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        });
        await ctx.Response.WriteAsync(result);
    }
});

// Legacy /health for backwards compatibility
app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Ok(new
{
    Service = "POS Local Store",
    Modules = new[] { "Orders", "Products", "Payments", "Sync" },
    Docs = "/scalar/v1",
    Status = "Running"
}));

app.Run();
