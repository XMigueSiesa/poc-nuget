using Microsoft.EntityFrameworkCore;
using Pos.Orders.Core;
using Pos.Payments.Core;
using Pos.Products.Core;
using Pos.SharedKernel.Auth;
using Pos.SharedKernel.Events;
using Pos.SharedKernel.Observability;
using Pos.Infrastructure.Postgres;
using Pos.Orders.Core.Data;
using Pos.Products.Core.Data;
using Pos.Payments.Core.Data;
using Pos.Host.CloudHub.Auth;
using Pos.Host.CloudHub.Sync;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: JSON in production, colored console in dev
builder.Host.ConfigureProductionLogging();

var connectionString = builder.Configuration.GetConnectionString("CloudDb")
    ?? "Host=localhost;Port=5432;Database=pos_cloud;Username=pos;Password=pos";

// All modules in the central cloud hub
builder.Services.AddOrdersModule(connectionString);
builder.Services.AddProductsModule(connectionString);
builder.Services.AddPaymentsModule(connectionString);

builder.Services.AddSingleton<IEventBus, InProcessEventBus>();

// Observability: OpenTelemetry tracing + metrics + structured logging
builder.Services.AddPosObservability("pos-cloudhub", builder.Configuration);

// Health checks: /health/live (liveness) and /health/ready (readiness)
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrdersDbContext>("orders_db", tags: ["ready"])
    .AddDbContextCheck<ProductsDbContext>("products_db", tags: ["ready"])
    .AddDbContextCheck<PaymentsDbContext>("payments_db", tags: ["ready"])
    .AddDbContextCheck<AuthDbContext>("auth_db", tags: ["ready"]);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Auth
builder.Services.AddDbContext<AuthDbContext>(opt =>
    opt.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))
       .UseSnakeCaseNamingConvention());
builder.Services.AddPosAuthentication(builder.Configuration);

// CORS — production origins come from config; dev allows any origin
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();

builder.Services.AddCors(opt =>
{
    if (allowedOrigins is { Length: > 0 })
        opt.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
    else
        opt.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

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
    await MigrationRunner.RunMigrationsAsync<AuthDbContext>(app.Services);
}
else
{
    await MigrationRunner.EnsureCreatedAsync<OrdersDbContext>(app.Services);
    await MigrationRunner.EnsureCreatedAsync<ProductsDbContext>(app.Services);
    await MigrationRunner.EnsureCreatedAsync<PaymentsDbContext>(app.Services);
    await MigrationRunner.EnsureCreatedAsync<AuthDbContext>(app.Services);
}

app.MapOpenApi();
app.MapScalarApiReference();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// Auth endpoints
app.MapTokenEndpoints();
app.MapStoreManagementEndpoints();

// Business endpoints
app.MapOrdersEndpoints();
app.MapProductsEndpoints();
app.MapPaymentsEndpoints();

// Sync receivers (from local POS stores)
app.MapSyncCategoriesEndpoints();
app.MapSyncProductsEndpoints();
app.MapSyncOrdersEndpoints();

// /health/live — just checks the app is running (no DB deps)
app.MapHealthChecks("/health/live");

// /health/ready — checks DB connectivity for all contexts
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
    Service = "POS Cloud Hub",
    Modules = new[] { "Orders", "Products", "Payments" },
    Sync = new[] { "POST /api/sync/categories", "POST /api/sync/products", "POST /api/sync/orders" },
    Docs = "/scalar/v1",
    Status = "Running"
}));

app.Run();
