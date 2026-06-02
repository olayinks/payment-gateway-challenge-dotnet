
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class PaymentService(PaymentsRepository repository) : IPaymentService
{
    private readonly PaymentsRepository _repository = repository;

    public Task<PaymentProcessingResult> ProcessAsync(PostPaymentRequest request, string? idempotentKey, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }


}