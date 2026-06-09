
using System.Net;

using Microsoft.Extensions.Options;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class BankClient(HttpClient httpClient, ILogger<BankClient> logger, IOptions<BankApiConfig> options) : IBankClient
{
    public async Task<BankPaymentResponse> AuthorizeAsync(BankPaymentRequest request, CancellationToken cancellationToken)
    {
        var lastFourDigits = request.CardNumber[^4..];
        logger.LogInformation(
            "Sending payment authorization request to bank API for card ending with {CardLastFour}, amount {Amount} {Currency}",
            lastFourDigits,
            request.Amount,
            request.Currency);

        using var response = await httpClient.PostAsJsonAsync(options.Value.PaymentEndpoint, request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Bank API rejected the payment authorization request with status code {StatusCode} for card ending with {CardLastFour}, amount {Amount} {Currency}",
                response.StatusCode,
                lastFourDigits,
                request.Amount,
                request.Currency);

            throw new HttpRequestException(errorBody);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Bank API returned unexpected status code {StatusCode} for card ending with {CardLastFour}", response.StatusCode, lastFourDigits);
            response.EnsureSuccessStatusCode();
        }

        var bankResponse = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Bank API returned an empty response");

        logger.LogInformation("Bank API authorization completed for card ending with {CardLastFour}, status {StatusCode}", lastFourDigits, response.StatusCode);
        return bankResponse;
    }
}
