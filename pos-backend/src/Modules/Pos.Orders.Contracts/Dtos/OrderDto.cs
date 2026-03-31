namespace Pos.Orders.Contracts.Dtos;

public sealed record OrderDto(
    string Id,
    string Status,
    IReadOnlyList<OrderLineDto> Lines,
    decimal Total,
    string? TableNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record OrderLineDto(
    string Id,
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record CreateOrderRequest(
    string? TableNumber,
    IReadOnlyList<CreateOrderLineRequest> Lines);

public sealed record CreateOrderLineRequest(
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

public sealed record AddOrderLineRequest(
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
