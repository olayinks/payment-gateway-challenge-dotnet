using System;
using System.Threading;
using System.Threading.Tasks;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Interfaces;

public interface IPaymentService
{
    Task<Payment> GetPaymentAsync(Guid id);
    Task<PaymentProcessingResult> ProcessAsync(PostPaymentRequest request, string? idempotentKey, CancellationToken cancellationToken);
}
