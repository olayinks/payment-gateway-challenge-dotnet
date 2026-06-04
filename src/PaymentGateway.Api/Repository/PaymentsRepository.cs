using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Domain;

namespace PaymentGateway.Api.Repository;

public class PaymentsRepository : IPaymentsRepository
{
    private readonly Dictionary<string, IdempotencyRecord> _idempotentRecords = new(StringComparer.Ordinal);
    private readonly List<Payment> _payments = [];

    public void Add(Payment payment) => _payments.Add(payment);

    public Payment? Get(Guid id) => _payments.FirstOrDefault(p => p.Id == id);

    public IdempotencyRecord? GetIdempotencyRecord(string key)
    {
        _idempotentRecords.TryGetValue(key, out var record);
        return record;
    }

    public void AddIdempotencyRecord(IdempotencyRecord record) =>
        _idempotentRecords[record.Key] = record;
}
