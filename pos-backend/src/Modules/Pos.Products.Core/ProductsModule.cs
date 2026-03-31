using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Products.Contracts.Dtos;
using Pos.Products.Contracts.Interfaces;
using Pos.Products.Core.Data;
using Pos.Products.Core.Repositories;

namespace Pos.Products.Core;

public static class ProductsModule
{
    public static IServiceCollection AddProductsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ProductsDbContext>(opt =>
            opt.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "products")));

        services.AddScoped<IProductRepository, EfProductRepository>();
        services.AddScoped<ICategoryRepository, EfCategoryRepository>();

        return services;
    }

    public static WebApplication MapProductsEndpoints(this WebApplication app)
    {
        var products = app.MapGroup("/api/products").WithTags("Products");
        var categories = app.MapGroup("/api/categories").WithTags("Categories");

        // Products CRUD
        products.MapGet("/", async (IProductRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAllAsync(ct)));

        products.MapGet("/{id}", async (string id, IProductRepository repo, CancellationToken ct) =>
            await repo.GetByIdAsync(id, ct) is { } product
                ? Results.Ok(product)
                : Results.NotFound());

        products.MapPost("/", async (CreateProductRequest request, IProductRepository repo, CancellationToken ct) =>
        {
            var product = await repo.CreateAsync(request, ct);
            return Results.Created($"/api/products/{product.Id}", product);
        });

        products.MapPut("/{id}", async (string id, UpdateProductRequest request, IProductRepository repo, CancellationToken ct) =>
            await repo.UpdateAsync(id, request, ct) is { } product
                ? Results.Ok(product)
                : Results.NotFound());

        products.MapDelete("/{id}", async (string id, IProductRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(id, ct)
                ? Results.NoContent()
                : Results.NotFound());

        // Categories CRUD
        categories.MapGet("/", async (ICategoryRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAllAsync(ct)));

        categories.MapGet("/{id}", async (string id, ICategoryRepository repo, CancellationToken ct) =>
            await repo.GetByIdAsync(id, ct) is { } cat
                ? Results.Ok(cat)
                : Results.NotFound());

        categories.MapPost("/", async (CreateCategoryRequest request, ICategoryRepository repo, CancellationToken ct) =>
        {
            var cat = await repo.CreateAsync(request, ct);
            return Results.Created($"/api/categories/{cat.Id}", cat);
        });

        categories.MapDelete("/{id}", async (string id, ICategoryRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(id, ct)
                ? Results.NoContent()
                : Results.NotFound());

        return app;
    }
}
