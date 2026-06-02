using System;
using System.Threading;
using System.Threading.Tasks;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Interfaces;

public interface IPaymentService
{
    Task<PaymentProcessingResult> ProcessAsync(PostPaymentRequest request, string? idempotentKey, CancellationToken cancellationToken);
}
