namespace PaymentGateway.Api.Exceptions;

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string idempotencyKey)
        : base($"Idempotency-Key '{idempotencyKey}' has already been used with a different request.")
    {
    }
}