using Microsoft.EntityFrameworkCore;
using Pos.Payments.Contracts.Dtos;
using Pos.Payments.Contracts.Interfaces;
using Pos.Payments.Core.Data;
using Pos.Payments.Core.Entities;

namespace Pos.Payments.Core.Repositories;

internal sealed class EfPaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public async Task<IReadOnlyList<PaymentDto>> GetByOrderIdAsync(string orderId, CancellationToken ct = default)
    {
        var ulid = Ulid.Parse(orderId);
        return await db.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == ulid)
            .Select(p => ToDto(p))
            .ToListAsync(ct);
    }

    public async Task<PaymentDto> CreateAsync(CreatePaymentRequest request, CancellationToken ct = default)
    {
        var payment = new Payment
        {
            OrderId = Ulid.Parse(request.OrderId),
            Method = request.Method,
            Amount = request.Amount,
            Status = "Completed",
            TransactionId = request.TransactionId
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        return ToDto(payment);
    }

    private static PaymentDto ToDto(Payment p) => new(
        Id: p.Id.ToString(),
        OrderId: p.OrderId.ToString(),
        Method: p.Method,
        Amount: p.Amount,
        Status: p.Status,
        TransactionId: p.TransactionId,
        CreatedAt: p.CreatedAt);
}
