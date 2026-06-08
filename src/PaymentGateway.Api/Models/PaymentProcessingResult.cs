
using PaymentGateway.Api.Models.Domain;

namespace PaymentGateway.Api.Models;

public record PaymentProcessingResult(Payment? Payment, bool AlreadyProcessed, IReadOnlyCollection<string> Errors)
{
    public PaymentProcessingResult(Payment payment, bool alreadyProcessed)
        : this(payment, alreadyProcessed, [])
    {
    }
}
