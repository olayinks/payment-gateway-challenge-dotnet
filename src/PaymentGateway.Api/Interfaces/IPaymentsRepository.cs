using PaymentGateway.Api.Models.Domain;

namespace PaymentGateway.Api.Interfaces;

public interface IPaymentsRepository
{
    void Add(Payment payment);
    Payment? Get(Guid id);
    IdempotencyRecord? GetIdempotencyRecord(string key);
    void AddIdempotencyRecord(IdempotencyRecord record);
}
