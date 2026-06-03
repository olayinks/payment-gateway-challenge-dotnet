
using Microsoft.Extensions.Options;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class BankClient(HttpClient httpClient, ILogger<BankClient> logger, IOptions<BankApiConfig> options) : IBankClient
{

    public async Task<BankPaymentResponse> AuthorizeAsync(BankPaymentRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Sending payment authorization request to bank API");

        using var response = await httpClient.PostAsJsonAsync(options.Value.PaymentEndpoint, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bankResponse = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Bank API returned an empty response");

        logger.LogInformation("Bank API request completed with status code: {StatusCode}", response.StatusCode);
        return bankResponse;
    }
}