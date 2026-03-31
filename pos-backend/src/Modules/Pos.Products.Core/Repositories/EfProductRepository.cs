using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Products.Contracts.Dtos;
using Pos.Products.Contracts.Interfaces;
using Pos.Products.Core.Data;
using Pos.Products.Core.Entities;
using Pos.SharedKernel.Sync;

namespace Pos.Products.Core.Repositories;

public sealed class EfProductRepository(
    ProductsDbContext db,
    ISyncOutboxWriter? outboxWriter = null) : IProductRepository
{
    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Select(p => ToDto(p))
            .ToListAsync(ct);
    }

    public async Task<ProductDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(id);
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == ulid, ct);

        return product is null ? null : ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            CategoryId = Ulid.Parse(request.CategoryId),
            Price = request.Price
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        var saved = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstAsync(p => p.Id == product.Id, ct);

        var dto = ToDto(saved);
        await WriteToOutboxAsync("Product", dto, ct);

        return dto;
    }

    public async Task<ProductDto?> UpdateAsync(string id, UpdateProductRequest request, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(id);
        var existing = await db.Products.FindAsync([ulid], ct);
        if (existing is null) return null;

        var updated = existing with
        {
            Name = request.Name,
            Description = request.Description,
            CategoryId = Ulid.Parse(request.CategoryId),
            Price = request.Price,
            IsActive = request.IsActive,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Entry(existing).CurrentValues.SetValues(updated);
        await db.SaveChangesAsync(ct);

        var saved = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstAsync(p => p.Id == ulid, ct);

        var dto = ToDto(saved);
        await WriteToOutboxAsync("Product", dto, ct);

        return dto;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(id);
        var rows = await db.Products.Where(p => p.Id == ulid).ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    private async Task WriteToOutboxAsync(string entityType, ProductDto dto, CancellationToken ct)
    {
        if (outboxWriter is not null)
        {
            var payload = JsonSerializer.Serialize(dto);
            await outboxWriter.WriteAsync(entityType, dto.Id, payload, ct);
        }
    }

    private static ProductDto ToDto(Product p) => new(
        Id: p.Id.ToString(),
        Name: p.Name,
        Description: p.Description,
        CategoryId: p.CategoryId.ToString(),
        CategoryName: p.Category?.Name,
        Price: p.Price,
        IsActive: p.IsActive,
        CreatedAt: p.CreatedAt,
        UpdatedAt: p.UpdatedAt);
}
