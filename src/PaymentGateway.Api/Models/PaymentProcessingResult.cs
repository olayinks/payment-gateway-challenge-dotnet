
using PaymentGateway.Api.Models.Domain;

namespace PaymentGateway.Api.Models;

public record PaymentProcessingResult(Payment Payment, bool AlreadyProcessed);
