using Pos.Orders.Core;
using Pos.Payments.Core;
using Pos.Products.Core;
using Pos.SharedKernel.Events;
using Pos.Infrastructure.Postgres;
using Pos.Orders.Core.Data;
using Pos.Products.Core.Data;
using Pos.Payments.Core.Data;
using Pos.Host.CloudHub.Sync;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("CloudDb")
    ?? "Host=localhost;Port=5432;Database=pos_cloud;Username=pos;Password=pos";

// All modules in the central cloud hub
builder.Services.AddOrdersModule(connectionString);
builder.Services.AddProductsModule(connectionString);
builder.Services.AddPaymentsModule(connectionString);

builder.Services.AddSingleton<IEventBus, InProcessEventBus>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddCors(opt => opt.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Create schemas/tables at startup (POC only)
await MigrationRunner.EnsureCreatedAsync<OrdersDbContext>(app.Services);
await MigrationRunner.EnsureCreatedAsync<ProductsDbContext>(app.Services);
await MigrationRunner.EnsureCreatedAsync<PaymentsDbContext>(app.Services);

app.MapOpenApi();
app.MapScalarApiReference();

app.UseCors();

// Business endpoints
app.MapOrdersEndpoints();
app.MapProductsEndpoints();
app.MapPaymentsEndpoints();

// Sync receivers (from local POS stores)
app.MapSyncCategoriesEndpoints();
app.MapSyncProductsEndpoints();
app.MapSyncOrdersEndpoints();

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
