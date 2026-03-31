using Pos.Orders.Contracts.Dtos;

namespace Pos.Orders.Contracts.Interfaces;

public interface IOrderRepository
{
    Task<IReadOnlyList<OrderDto>> GetAllAsync(CancellationToken ct = default);
    Task<OrderDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task<OrderDto?> AddLineAsync(string orderId, AddOrderLineRequest request, CancellationToken ct = default);
    Task<bool> CloseOrderAsync(string id, CancellationToken ct = default);
}
