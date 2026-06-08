using System.Security.Cryptography;
using System.Text;

using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class IdempotencyService(IPaymentsRepository repository, ILogger<IdempotencyService> logger) : IIdempotencyService
{
    public PaymentProcessingResult? Check(string? key, PostPaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var normalizedKey = key.Trim();
        var existingRecord = repository.GetIdempotencyRecord(normalizedKey);
        if (existingRecord == null) return null;

        if (!string.Equals(existingRecord.RequestHash, Hash(request), StringComparison.Ordinal))
            throw new IdempotencyConflictException(normalizedKey);

        var existingPayment = repository.Get(existingRecord.PaymentId)
            ?? throw new InvalidOperationException("Inconsistent state: idempotency record exists without corresponding payment");

        logger.LogInformation("Idempotent request with key {Key} already processed, returning existing payment", normalizedKey);
        return new PaymentProcessingResult(existingPayment, alreadyProcessed: true);
    }

    public void Record(string key, PostPaymentRequest request, Guid paymentId) =>
        repository.AddIdempotencyRecord(new IdempotencyRecord
        {
            Key = key.Trim(),
            RequestHash = Hash(request),
            PaymentId = paymentId
        });

    private static string Hash(PostPaymentRequest request)
    {
        var cardLastFourDigits = request.CardNumber[^4..];
        var input = $"{request.Amount}:{request.Currency}:{cardLastFourDigits}:{request.ExpiryMonth}:{request.ExpiryYear}";
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
