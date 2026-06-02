using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests;

internal static class TestData
{
    internal static PostPaymentRequest PaymentRequest(string cardNumber = "4111111111111111")
    {
        return new PostPaymentRequest
        {
            CardNumber = cardNumber,
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1250,
            Cvv = "123",
        };
    }
}
