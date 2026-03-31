namespace Pos.Payments.Contracts.Dtos;

public sealed record PaymentDto(
    string Id,
    string OrderId,
    string Method,
    decimal Amount,
    string Status,
    string? TransactionId,
    DateTimeOffset CreatedAt);

public sealed record CreatePaymentRequest(
    string OrderId,
    string Method,
    decimal Amount,
    string? TransactionId);
