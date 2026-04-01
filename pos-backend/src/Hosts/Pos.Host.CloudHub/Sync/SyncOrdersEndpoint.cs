using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pos.Orders.Contracts.Dtos;
using Pos.Orders.Core.Data;
using Pos.Orders.Core.Entities;

namespace Pos.Host.CloudHub.Sync;

public static class SyncOrdersEndpoint
{
    public static WebApplication MapSyncOrdersEndpoints(this WebApplication app)
    {
        var sync = app.MapGroup("/api/sync").WithTags("Sync");

        sync.MapPost("/orders", async (OrderDto dto, OrdersDbContext db, CancellationToken ct) =>
        {
            var ulid = Ulid.Parse(dto.Id);
            var existing = await db.Orders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == ulid, ct);

            if (existing is not null)
            {
                var updated = existing with
                {
                    Status = dto.Status,
                    Total = dto.Total,
                    TableNumber = dto.TableNumber,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                db.Entry(existing).CurrentValues.SetValues(updated);

                // Sync lines: remove existing and re-add
                db.OrderLines.RemoveRange(existing.Lines);
                foreach (var lineDto in dto.Lines)
                {
                    db.OrderLines.Add(new OrderLine
                    {
                        Id = Ulid.Parse(lineDto.Id),
                        OrderId = ulid,
                        ProductId = lineDto.ProductId,
                        ProductName = lineDto.ProductName,
                        Quantity = lineDto.Quantity,
                        UnitPrice = lineDto.UnitPrice
                    });
                }
            }
            else
            {
                var order = new Order
                {
                    Id = ulid,
                    Status = dto.Status,
                    TableNumber = dto.TableNumber,
                    Total = dto.Total,
                    CreatedAt = dto.CreatedAt,
                    UpdatedAt = dto.UpdatedAt,
                    Lines = dto.Lines.Select(l => new OrderLine
                    {
                        Id = Ulid.Parse(l.Id),
                        OrderId = ulid,
                        ProductId = l.ProductId,
                        ProductName = l.ProductName,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice
                    }).ToList()
                };

                db.Orders.Add(order);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Synced = true, Id = dto.Id });
        })
        .RequireAuthorization("SyncEndpoint");

        return app;
    }
}
