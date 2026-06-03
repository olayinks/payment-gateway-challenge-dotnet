using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository
{
    private readonly Dictionary<string, IdempotencyRecord> _idempotentRecords = new(StringComparer.Ordinal);
    public List<Payment> Payments = new();

    public void Add(Payment payment)
    {
        Payments.Add(payment);
    }

    public Payment Get(Guid id)
    {
        return Payments.FirstOrDefault(p => p.Id == id);
    }

    public IdempotencyRecord? GetIdempotencyRecord(string key)
    {
        _idempotentRecords.TryGetValue(key, out var record);
        return record;
    }

    public void AddIdempotencyRecord(IdempotencyRecord record)
    {
        _idempotentRecords[record.Key] = record;
    }


}