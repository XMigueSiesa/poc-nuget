using Pos.Payments.Contracts.Dtos;

namespace Pos.Payments.Contracts.Interfaces;

public interface IPaymentRepository
{
    Task<IReadOnlyList<PaymentDto>> GetByOrderIdAsync(string orderId, CancellationToken ct = default);
    Task<PaymentDto> CreateAsync(CreatePaymentRequest request, CancellationToken ct = default);
}
