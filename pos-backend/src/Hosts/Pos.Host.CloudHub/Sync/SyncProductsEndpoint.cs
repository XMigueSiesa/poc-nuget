using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pos.Products.Contracts.Dtos;
using Pos.Products.Core.Data;
using Pos.Products.Core.Entities;

namespace Pos.Host.CloudHub.Sync;

public static class SyncProductsEndpoint
{
    public static WebApplication MapSyncProductsEndpoints(this WebApplication app)
    {
        var sync = app.MapGroup("/api/sync").WithTags("Sync");

        sync.MapPost("/products", async (ProductDto dto, ProductsDbContext db, CancellationToken ct) =>
        {
            // Upsert category first to satisfy FK constraint
            var categoryUlid = Ulid.Parse(dto.CategoryId);
            var existingCategory = await db.Categories.FindAsync([categoryUlid], ct);
            if (existingCategory is null)
            {
                db.Categories.Add(new Category
                {
                    Id = categoryUlid,
                    Name = dto.CategoryName ?? dto.CategoryId
                });
                await db.SaveChangesAsync(ct);
            }

            // Upsert product
            var productUlid = Ulid.Parse(dto.Id);
            var existing = await db.Products.FindAsync([productUlid], ct);

            if (existing is not null)
            {
                var updated = existing with
                {
                    Name = dto.Name,
                    Description = dto.Description,
                    CategoryId = categoryUlid,
                    Price = dto.Price,
                    IsActive = dto.IsActive,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                db.Entry(existing).CurrentValues.SetValues(updated);
            }
            else
            {
                db.Products.Add(new Product
                {
                    Id = productUlid,
                    Name = dto.Name,
                    Description = dto.Description,
                    CategoryId = categoryUlid,
                    Price = dto.Price,
                    IsActive = dto.IsActive,
                    CreatedAt = dto.CreatedAt,
                    UpdatedAt = dto.UpdatedAt
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Synced = true, Id = dto.Id });
        });

        return app;
    }
}
