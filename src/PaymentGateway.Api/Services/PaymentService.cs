using System.Net;

using AutoMapper;

using FluentValidation;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class PaymentService(
    IPaymentsRepository repository,
    IIdempotencyService idempotencyService,
    ILogger<PaymentService> logger,
    IMapper mapper,
    IBankClient bankClient,
    IValidator<PostPaymentRequest> validator) : IPaymentService
{
    public Task<Payment?> GetPaymentAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(repository.Get(id));
    }

    public async Task<PaymentProcessingResult> ProcessAsync(PostPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToArray();
            logger.LogInformation("Rejected invalid payment request with {ValidationErrorCount} validation errors.", errors.Length);
            return new PaymentProcessingResult(Payment: null, AlreadyProcessed: false, Errors: errors);
        }

        var existingPayment = idempotencyService.Check(idempotencyKey, request);
        if (existingPayment != null) return existingPayment;

        logger.LogInformation("Processing new payment request for card ending with {CardLastFour}", request.CardNumber[^4..]);
        var payment = await HandleBankSimulationCall(request, cancellationToken);
        repository.Add(payment);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            idempotencyService.Record(idempotencyKey, request, payment.Id);

        logger.LogInformation(
            "Payment processing completed with {PaymentStatus} for payment {PaymentId}, amount {Amount} {Currency}, card ending with {CardLastFour}",
            payment.Status,
            payment.Id,
            payment.Amount,
            payment.Currency,
            payment.CardNumberLastFour);

        return new PaymentProcessingResult(payment, alreadyProcessed: false);
    }

    private async Task<Payment> HandleBankSimulationCall(PostPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Initiating bank authorization for card ending with {CardLastFour}", request.CardNumber[^4..]);
            var bankRequest = mapper.Map<BankPaymentRequest>(request);
            var bankResponse = await bankClient.AuthorizeAsync(bankRequest, cancellationToken);
            var payment = mapper.Map<Payment>(request);
            payment.Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
            payment.ErrorMessage = bankResponse.ErrorMessage;
            return payment;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Error communicating with bank");
            var payment = mapper.Map<Payment>(request);
            payment.Status = PaymentStatus.Declined;
            payment.ErrorMessage = "Bank service unavailable";
            return payment;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during payment processing");
            var payment = mapper.Map<Payment>(request);
            payment.Status = PaymentStatus.Declined;
            payment.ErrorMessage = "An unexpected error occurred during payment processing";
            return payment;
        }
    }
}
