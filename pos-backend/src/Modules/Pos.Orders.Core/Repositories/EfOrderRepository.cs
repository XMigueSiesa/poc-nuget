using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Orders.Contracts.Dtos;
using Pos.Orders.Contracts.Interfaces;
using Pos.Orders.Core.Data;
using Pos.Orders.Core.Entities;
using Pos.SharedKernel.Sync;

namespace Pos.Orders.Core.Repositories;

public sealed class EfOrderRepository(
    OrdersDbContext db,
    ISyncOutboxWriter? outboxWriter = null) : IOrderRepository
{
    public async Task<IReadOnlyList<OrderDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .Select(o => ToDto(o))
            .ToListAsync(ct);
    }

    public async Task<OrderDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(id);
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == ulid, ct);

        return order is null ? null : ToDto(order);
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var orderId = Ulid.NewUlid();
        var lines = request.Lines.Select(l => new OrderLine
        {
            OrderId = orderId,
            ProductId = l.ProductId,
            ProductName = l.ProductName,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice
        }).ToList();

        var order = new Order
        {
            Id = orderId,
            Status = "Open",
            TableNumber = request.TableNumber,
            Lines = lines,
            Total = lines.Sum(l => l.Quantity * l.UnitPrice)
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        var dto = ToDto(order);
        await WriteToOutboxAsync(dto, ct);

        return dto;
    }

    public async Task<OrderDto?> AddLineAsync(string orderId, AddOrderLineRequest request, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(orderId);
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == ulid, ct);
        if (order is null || order.Status != "Open") return null;

        var newLine = new OrderLine
        {
            OrderId = ulid,
            ProductId = request.ProductId,
            ProductName = request.ProductName,
            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice
        };

        db.OrderLines.Add(newLine);

        var allLines = order.Lines.Append(newLine).ToList();
        var updated = order with
        {
            Total = allLines.Sum(l => l.Quantity * l.UnitPrice),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Entry(order).CurrentValues.SetValues(updated);

        await db.SaveChangesAsync(ct);

        return ToDto(updated with { Lines = allLines });
    }

    public async Task<bool> CloseOrderAsync(string id, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(id);
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == ulid, ct);
        if (order is null || order.Status != "Open") return false;

        var closed = order with { Status = "Closed", UpdatedAt = DateTimeOffset.UtcNow };
        db.Entry(order).CurrentValues.SetValues(closed);
        await db.SaveChangesAsync(ct);

        await WriteToOutboxAsync(ToDto(closed), ct);

        return true;
    }

    private async Task WriteToOutboxAsync(OrderDto dto, CancellationToken ct)
    {
        if (outboxWriter is not null)
        {
            var payload = JsonSerializer.Serialize(dto);
            await outboxWriter.WriteAsync("Order", dto.Id, payload, ct);
        }
    }

    private static OrderDto ToDto(Order o) => new(
        Id: o.Id.ToString(),
        Status: o.Status,
        Lines: o.Lines.Select(l => new OrderLineDto(
            Id: l.Id.ToString(),
            ProductId: l.ProductId,
            ProductName: l.ProductName,
            Quantity: l.Quantity,
            UnitPrice: l.UnitPrice,
            LineTotal: l.Quantity * l.UnitPrice)).ToList(),
        Total: o.Total,
        TableNumber: o.TableNumber,
        CreatedAt: o.CreatedAt,
        UpdatedAt: o.UpdatedAt);
}
