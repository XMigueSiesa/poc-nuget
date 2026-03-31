using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Orders.Contracts.Dtos;
using Pos.Orders.Contracts.Interfaces;
using Pos.Orders.Core.Data;
using Pos.Orders.Core.Repositories;

namespace Pos.Orders.Core;

public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OrdersDbContext>(opt =>
            opt.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "orders"))
               .UseSnakeCaseNamingConvention());

        services.AddScoped<IOrderRepository, EfOrderRepository>();

        return services;
    }

    public static WebApplication MapOrdersEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapGet("/", async (IOrderRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAllAsync(ct)));

        orders.MapGet("/{id}", async (string id, IOrderRepository repo, CancellationToken ct) =>
            await repo.GetByIdAsync(id, ct) is { } order
                ? Results.Ok(order)
                : Results.NotFound());

        orders.MapPost("/", async (CreateOrderRequest request, IOrderRepository repo, CancellationToken ct) =>
        {
            var order = await repo.CreateAsync(request, ct);
            return Results.Created($"/api/orders/{order.Id}", order);
        });

        orders.MapPost("/{id}/lines", async (string id, AddOrderLineRequest request, IOrderRepository repo, CancellationToken ct) =>
            await repo.AddLineAsync(id, request, ct) is { } order
                ? Results.Ok(order)
                : Results.NotFound());

        orders.MapPost("/{id}/close", async (string id, IOrderRepository repo, CancellationToken ct) =>
            await repo.CloseOrderAsync(id, ct)
                ? Results.NoContent()
                : Results.NotFound());

        return app;
    }
}
