using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            await client.AuthorizeAsync(new BankPaymentRequest(), CancellationToken.None));
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
