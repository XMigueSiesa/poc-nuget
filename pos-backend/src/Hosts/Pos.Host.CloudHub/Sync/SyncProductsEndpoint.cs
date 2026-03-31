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
            var ulid = Ulid.Parse(dto.Id);
            var existing = await db.Products.FindAsync([ulid], ct);

            if (existing is not null)
            {
                var updated = existing with
                {
                    Name = dto.Name,
                    Description = dto.Description,
                    CategoryId = Ulid.Parse(dto.CategoryId),
                    Price = dto.Price,
                    IsActive = dto.IsActive,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                db.Entry(existing).CurrentValues.SetValues(updated);
            }
            else
            {
                var product = new Product
                {
                    Id = ulid,
                    Name = dto.Name,
                    Description = dto.Description,
                    CategoryId = Ulid.Parse(dto.CategoryId),
                    Price = dto.Price,
                    IsActive = dto.IsActive,
                    CreatedAt = dto.CreatedAt,
                    UpdatedAt = dto.UpdatedAt
                };

                db.Products.Add(product);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Synced = true, Id = dto.Id });
        });

        return app;
    }
}
