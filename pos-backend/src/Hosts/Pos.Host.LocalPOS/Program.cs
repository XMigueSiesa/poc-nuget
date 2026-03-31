using Microsoft.EntityFrameworkCore;
using Pos.Orders.Core;
using Pos.Payments.Core;
using Pos.Products.Core;
using Pos.SharedKernel.Events;
using Pos.SharedKernel.Sync;
using Pos.Infrastructure.Postgres;
using Pos.Orders.Core.Data;
using Pos.Products.Core.Data;
using Pos.Payments.Core.Data;
using Pos.Host.LocalPOS.Sync;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSingleton<IEventBus, InProcessEventBus>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddCors(opt => opt.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Siempre mostrar detalles de error en el POC
app.UseExceptionHandler();
app.UseStatusCodePages();

// Siempre crear tablas al arrancar (POC — en produccion usar migraciones)
await MigrationRunner.EnsureCreatedAsync<OrdersDbContext>(app.Services);
await MigrationRunner.EnsureCreatedAsync<ProductsDbContext>(app.Services);
await MigrationRunner.EnsureCreatedAsync<PaymentsDbContext>(app.Services);
await MigrationRunner.EnsureCreatedAsync<SyncDbContext>(app.Services);

app.MapOpenApi();
app.MapScalarApiReference();

app.UseCors();

app.MapOrdersEndpoints();
app.MapProductsEndpoints();
app.MapPaymentsEndpoints();

app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Ok(new
{
    Service = "POS Local Store",
    Modules = new[] { "Orders", "Products", "Payments", "Sync" },
    Docs = "/scalar/v1",
    Status = "Running"
}));

app.Run();
