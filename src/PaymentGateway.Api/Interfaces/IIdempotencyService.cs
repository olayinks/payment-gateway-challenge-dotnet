using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Interfaces;

public interface IIdempotencyService
{
    PaymentProcessingResult? Check(string? key, PostPaymentRequest request);
    void Record(string key, PostPaymentRequest request, Guid paymentId);
}
