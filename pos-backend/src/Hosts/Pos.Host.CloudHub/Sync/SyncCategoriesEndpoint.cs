using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pos.Products.Contracts.Dtos;
using Pos.Products.Core.Data;
using Pos.Products.Core.Entities;

namespace Pos.Host.CloudHub.Sync;

public static class SyncCategoriesEndpoint
{
    public static WebApplication MapSyncCategoriesEndpoints(this WebApplication app)
    {
        var sync = app.MapGroup("/api/sync").WithTags("Sync");

        sync.MapPost("/categories", async (CategoryDto dto, ProductsDbContext db, CancellationToken ct) =>
        {
            var ulid = Ulid.Parse(dto.Id);
            var existing = await db.Categories.FindAsync([ulid], ct);

            if (existing is not null)
            {
                var updated = existing with { Name = dto.Name, Description = dto.Description };
                db.Entry(existing).CurrentValues.SetValues(updated);
            }
            else
            {
                db.Categories.Add(new Category
                {
                    Id = ulid,
                    Name = dto.Name,
                    Description = dto.Description,
                    CreatedAt = dto.CreatedAt
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Synced = true, Id = dto.Id });
        })
        .RequireAuthorization("SyncEndpoint");

        return app;
    }
}
