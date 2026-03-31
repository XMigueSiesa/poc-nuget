using Microsoft.EntityFrameworkCore;
using Pos.Products.Contracts.Dtos;
using Pos.Products.Contracts.Interfaces;
using Pos.Products.Core.Data;
using Pos.Products.Core.Entities;

namespace Pos.Products.Core.Repositories;

public sealed class EfCategoryRepository(ProductsDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Categories
            .AsNoTracking()
            .Select(c => ToDto(c))
            .ToListAsync(ct);
    }

    public async Task<CategoryDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(id);
        var category = await db.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == ulid, ct);

        return category is null ? null : ToDto(category);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        var category = new Category
        {
            Name = request.Name,
            Description = request.Description
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        return ToDto(category);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(id);
        var rows = await db.Categories.Where(c => c.Id == ulid).ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    private static CategoryDto ToDto(Category c) => new(
        Id: c.Id.ToString(),
        Name: c.Name,
        Description: c.Description,
        CreatedAt: c.CreatedAt);
}
