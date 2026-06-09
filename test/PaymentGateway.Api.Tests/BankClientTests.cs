using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

using Shouldly;

namespace PaymentGateway.Api.Tests;

public class BankClientTests
{
    [Fact]
    public async Task AuthorizeAsync_sends_request_to_configured_bank_endpoint_and_parses_response()
    {
        // Arrange
        var expectedResponse = new BankPaymentResponse
        {
            Authorized = true,
            AuthorizationCode = "auth-789"
        };

        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegatingHandlerStub(async request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expectedResponse)
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:8080")
        };
        var options = Options.Create(new BankApiConfig
        {
            BaseUrl = new Uri("https://localhost:8080"),
            PaymentEndpoint = "/payments",
            TimeoutSeconds = 10
        });

        var client = new BankClient(httpClient, MockLogger<BankClient>(), options);
        var requestModel = new BankPaymentRequest
        {
            Amount = 1000,
            CardNumber = "4111111111111111",
            Currency = "GBP",
            ExpiryDate = "12/2026",
            Cvv = "123"
        };

        // Act
        var result = await client.AuthorizeAsync(requestModel, CancellationToken.None);

        // Assert
        result.Authorized.ShouldBeTrue();
        result.AuthorizationCode.ShouldBe("auth-789");
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.Method.ShouldBe(HttpMethod.Post);
        capturedRequest.RequestUri.ShouldBe(new Uri("https://localhost:8080/payments"));

        var content = await capturedRequest.Content!.ReadAsStringAsync();
        var parsedRequest = JsonSerializer.Deserialize<BankPaymentRequest>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        parsedRequest.ShouldNotBeNull();
        parsedRequest!.CardNumber.ShouldBe(requestModel.CardNumber);
        parsedRequest.ExpiryDate.ShouldBe(requestModel.ExpiryDate);
        parsedRequest.Currency.ShouldBe(requestModel.Currency);
        parsedRequest.Amount.ShouldBe(requestModel.Amount);
        parsedRequest.Cvv.ShouldBe(requestModel.Cvv);
    }

    [Fact]
    public async Task AuthorizeAsync_logs_card_amount_and_currency_when_sending_request()
    {
        var logger = new Mock<ILogger<BankClient>>();
        var handler = new DelegatingHandlerStub(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-1" })
            }));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8080") };
        var options = Options.Create(new BankApiConfig
        {
            BaseUrl = new Uri("https://localhost:8080"),
            PaymentEndpoint = "/payments",
            TimeoutSeconds = 10
        });
        var client = new BankClient(httpClient, logger.Object, options);
        var request = new BankPaymentRequest { CardNumber = "4111111111111111", Amount = 1250, Currency = "GBP", ExpiryDate = "12/2027", Cvv = "123" };

        await client.AuthorizeAsync(request, CancellationToken.None);

        logger.VerifyLog(LogLevel.Information, "amount 1250 GBP", Times.Once());
    }

    [Fact]
    public async Task AuthorizeAsync_logs_card_context_when_bank_returns_400()
    {
        var logger = new Mock<ILogger<BankClient>>();
        var handler = new DelegatingHandlerStub(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request", System.Text.Encoding.UTF8, "application/json")
            }));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8080") };
        var options = Options.Create(new BankApiConfig
        {
            BaseUrl = new Uri("https://localhost:8080"),
            PaymentEndpoint = "/payments",
            TimeoutSeconds = 10
        });
        var client = new BankClient(httpClient, logger.Object, options);
        var request = new BankPaymentRequest { CardNumber = "4111111111111111", Amount = 500, Currency = "USD", ExpiryDate = "12/2027", Cvv = "123" };

        await Should.ThrowAsync<HttpRequestException>(() => client.AuthorizeAsync(request, CancellationToken.None));

        logger.VerifyLog(LogLevel.Warning, "1111", Times.Once());
        logger.VerifyLog(LogLevel.Warning, "500", Times.Once());
        logger.VerifyLog(LogLevel.Warning, "USD", Times.Once());
    }

    [Fact]
    public async Task AuthorizeAsync_throws_with_bank_error_body_when_bank_returns_400()
    {
        var logger = new Mock<ILogger<BankClient>>();
        var handler = new DelegatingHandlerStub(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Invalid card number", Encoding.UTF8, "application/json")
            }));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8080") };
        var options = Options.Create(new BankApiConfig
        {
            BaseUrl = new Uri("https://localhost:8080"),
            PaymentEndpoint = "/payments",
            TimeoutSeconds = 10
        });

        var client = new BankClient(httpClient, logger.Object, options);

        var ex = await Should.ThrowAsync<HttpRequestException>(
            () => client.AuthorizeAsync(new BankPaymentRequest { CardNumber = "4111111111111111" }, CancellationToken.None));
        ex.Message.ShouldBe("Invalid card number");
        logger.VerifyLog(LogLevel.Warning, "Bank API rejected the payment authorization request", Times.Once());
        logger.VerifyLogDoesNotContain("Invalid card number");
    }

    [Fact]
    public async Task AuthorizeAsync_logs_status_code_before_throwing_when_bank_returns_non_400_error()
    {
        var logger = new Mock<ILogger<BankClient>>();
        var handler = new DelegatingHandlerStub(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8080") };
        var options = Options.Create(new BankApiConfig
        {
            BaseUrl = new Uri("https://localhost:8080"),
            PaymentEndpoint = "/payments",
            TimeoutSeconds = 10
        });
        var client = new BankClient(httpClient, logger.Object, options);

        await Should.ThrowAsync<HttpRequestException>(
            () => client.AuthorizeAsync(new BankPaymentRequest { CardNumber = "4111111111111111" }, CancellationToken.None));

        logger.VerifyLog(LogLevel.Warning, "ServiceUnavailable", Times.Once());
    }

    [Fact]
    public async Task AuthorizeAsync_throws_when_bank_returns_503()
    {
        var handler = new DelegatingHandlerStub(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8080") };
        var options = Options.Create(new BankApiConfig
        {
            BaseUrl = new Uri("https://localhost:8080"),
            PaymentEndpoint = "/payments",
            TimeoutSeconds = 10
        });

        var client = new BankClient(httpClient, MockLogger<BankClient>(), options);

        var ex = await Should.ThrowAsync<HttpRequestException>(
            () => client.AuthorizeAsync(new BankPaymentRequest { CardNumber = "4111111111111111" }, CancellationToken.None));
        ex.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task AuthorizeAsync_throws_when_bank_returns_empty_response_body()
    {
        // Arrange
        var handler = new DelegatingHandlerStub(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json")
            }));

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:8080")
        };
        var options = Options.Create(new BankApiConfig
        {
            BaseUrl = new Uri("https://localhost:8080"),
            PaymentEndpoint = "/payments",
            TimeoutSeconds = 10
        });

        var client = new BankClient(httpClient, MockLogger<BankClient>(), options);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await client.AuthorizeAsync(new BankPaymentRequest { CardNumber = "4111111111111111" }, CancellationToken.None));
    }

    private static Logger<T> MockLogger<T>() => new(new LoggerFactory());

    private sealed class DelegatingHandlerStub : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public DelegatingHandlerStub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}
