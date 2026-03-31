using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Payments.Contracts.Dtos;
using Pos.Payments.Contracts.Interfaces;
using Pos.Payments.Core.Data;
using Pos.Payments.Core.Repositories;

namespace Pos.Payments.Core;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<PaymentsDbContext>(opt =>
            opt.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "payments")));

        services.AddScoped<IPaymentRepository, EfPaymentRepository>();

        return services;
    }

    public static WebApplication MapPaymentsEndpoints(this WebApplication app)
    {
        var payments = app.MapGroup("/api/payments").WithTags("Payments");

        payments.MapGet("/by-order/{orderId}", async (string orderId, IPaymentRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetByOrderIdAsync(orderId, ct)));

        payments.MapPost("/", async (CreatePaymentRequest request, IPaymentRepository repo, CancellationToken ct) =>
        {
            var payment = await repo.CreateAsync(request, ct);
            return Results.Created($"/api/payments/{payment.Id}", payment);
        });

        return app;
    }
}
