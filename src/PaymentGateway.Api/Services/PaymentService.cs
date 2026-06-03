
using AutoMapper;

using FluentValidation;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class PaymentService(PaymentsRepository repository, ILogger<PaymentService> logger, IMapper mapper, IBankClient bankClient, IValidator<PostPaymentRequest> validator) : IPaymentService
{

    public Task<Payment> GetPaymentAsync(Guid id)
    {
        return Task.FromResult(repository.Get(id));
    }

    public async Task<PaymentProcessingResult> ProcessAsync(PostPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(error => error.ErrorMessage).ToArray();
            logger.LogInformation("Rejected invalid payment request with {ValidationErrorCount} validation errors.", errors.Length);
            return new PaymentProcessingResult(Payment: null, AlreadyProcessed: false, Errors: errors);
        }

        var requestHash = Hash(request);
        var normalizedIdempotentKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (normalizedIdempotentKey != null)
        {
            var existingRecord = repository.GetIdempotencyRecord(normalizedIdempotentKey);
            if (existingRecord != null)
            {
                if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new IdempotencyConflictException(normalizedIdempotentKey);
                }
                var existingPayment = repository.Get(existingRecord.PaymentId)
                    ?? throw new InvalidOperationException("Inconsistent state: Idempotency record exists without corresponding payment");

                logger.LogInformation("Idempotent request with key {Key} already processed, returning existing payment", normalizedIdempotentKey);
                return new PaymentProcessingResult(existingPayment, AlreadyProcessed: true);
            }
        }
        logger.LogInformation("Processing new payment request");
        var payment = await HandleBankSimulationCall(request, cancellationToken);
        repository.Add(payment);
        if (normalizedIdempotentKey != null)
        {
            repository.AddIdempotencyRecord(new IdempotencyRecord
            {
                Key = normalizedIdempotentKey,
                RequestHash = requestHash,
                PaymentId = payment.Id
            });
        }
        return new PaymentProcessingResult(payment, AlreadyProcessed: false);


    }

    private async Task<Payment> HandleBankSimulationCall(PostPaymentRequest request, CancellationToken cancellationToken)
    {
        Payment payment;
        try
        {
            var bankRequest = mapper.Map<BankPaymentRequest>(request);
            var bankResponse = await bankClient.AuthorizeAsync(bankRequest, cancellationToken);
            payment = mapper.Map<Payment>(request);
            payment.Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Error communicating with bank");
            payment = mapper.Map<Payment>(request);
            payment.Status = PaymentStatus.Declined;
            payment.ErrorMessage = "Bank simulator is unavailable";

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during payment processing");
            payment = mapper.Map<Payment>(request);
            payment.Status = PaymentStatus.Declined;
            payment.ErrorMessage = "An unexpected error occurred during payment processing";
        }
        return payment;
    }
    private static string Hash(PostPaymentRequest request)
    {
        var input = $"{request.Amount}:{request.Currency}:{request.CardNumber}:{request.ExpiryMonth}:{request.ExpiryYear}:{request.Cvv}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes);
    }
}