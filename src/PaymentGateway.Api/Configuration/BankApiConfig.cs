
namespace PaymentGateway.Api.Services;

public class BankApiConfig
{
    public required Uri BaseUrl { get; set; }
    public string PaymentEndpoint { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}