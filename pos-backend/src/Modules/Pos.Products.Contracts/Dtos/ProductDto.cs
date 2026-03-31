namespace Pos.Products.Contracts.Dtos;

public sealed record ProductDto(
    string Id,
    string Name,
    string? Description,
    string CategoryId,
    string? CategoryName,
    decimal Price,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateProductRequest(
    string Name,
    string? Description,
    string CategoryId,
    decimal Price);

public sealed record UpdateProductRequest(
    string Name,
    string? Description,
    string CategoryId,
    decimal Price,
    bool IsActive);

public sealed record CategoryDto(
    string Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt);

public sealed record CreateCategoryRequest(
    string Name,
    string? Description);
