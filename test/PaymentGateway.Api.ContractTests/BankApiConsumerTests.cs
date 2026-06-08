using System.Net;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PactNet;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

using Shouldly;

namespace PaymentGateway.Api.ContractTests;

public class BankApiConsumerTests
{
    private static readonly string PactDir = Path.Join(Directory.GetCurrentDirectory(), "pacts");

    private static IPactBuilderV3 BuildPact() =>
        Pact.V3("Payment Gateway", "Bank API", new PactConfig
        {
            PactDir = PactDir,
            LogLevel = PactLogLevel.Error,
        }).WithHttpInteractions();

    [Fact]
    public async Task AuthorizeAsync_returns_authorized_when_bank_approves()
    {
        var pact = BuildPact();

        pact.UponReceiving("a valid payment authorization request")
                .WithRequest(HttpMethod.Post, "/payments")
                .WithJsonBody(new
                {
                    card_number = "4111111111111111",
                    expiry_date = "12/2027",
                    currency = "GBP",
                    amount = 1250,
                    cvv = "123"
                })
            .WillRespond()
                .WithStatus(HttpStatusCode.OK)
                .WithJsonBody(new
                {
                    authorized = true,
                    authorization_code = "auth-code-123"
                });

        await pact.VerifyAsync(async ctx =>
        {
            var client = CreateBankClient(ctx.MockServerUri);

            var response = await client.AuthorizeAsync(BuildRequest(), CancellationToken.None);

            response.Authorized.ShouldBeTrue();
            response.AuthorizationCode.ShouldNotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task AuthorizeAsync_returns_not_authorized_when_bank_declines()
    {
        var pact = BuildPact();

        pact.UponReceiving("a payment authorization request the bank declines")
                .WithRequest(HttpMethod.Post, "/payments")
                .WithJsonBody(new
                {
                    card_number = "4111111111111111",
                    expiry_date = "12/2027",
                    currency = "GBP",
                    amount = 1250,
                    cvv = "123"
                })
            .WillRespond()
                .WithStatus(HttpStatusCode.OK)
                .WithJsonBody(new
                {
                    authorized = false,
                    authorization_code = (string?)null
                });

        await pact.VerifyAsync(async ctx =>
        {
            var client = CreateBankClient(ctx.MockServerUri);

            var response = await client.AuthorizeAsync(BuildRequest(), CancellationToken.None);

            response.Authorized.ShouldBeFalse();
            response.AuthorizationCode.ShouldBeNull();
        });
    }

    [Fact]
    public async Task AuthorizeAsync_throws_HttpRequestException_when_bank_returns_bad_request()
    {
        var pact = BuildPact();

        pact.UponReceiving("a malformed payment authorization request")
                .WithRequest(HttpMethod.Post, "/payments")
                .WithJsonBody(new
                {
                    card_number = "0000000000000000",
                    expiry_date = "01/2020",
                    currency = "GBP",
                    amount = 0,
                    cvv = "000"
                })
            .WillRespond()
                .WithStatus(HttpStatusCode.BadRequest)
                .WithBody("Invalid payment request", "text/plain");

        await pact.VerifyAsync(async ctx =>
        {
            var client = CreateBankClient(ctx.MockServerUri);
            var request = new BankPaymentRequest
            {
                CardNumber = "0000000000000000",
                ExpiryDate = "01/2020",
                Currency = "GBP",
                Amount = 0,
                Cvv = "000"
            };

            await Should.ThrowAsync<HttpRequestException>(() =>
                client.AuthorizeAsync(request, CancellationToken.None));
        });
    }

    private static BankClient CreateBankClient(Uri mockServerUri) =>
        new(
            new HttpClient { BaseAddress = mockServerUri },
            NullLogger<BankClient>.Instance,
            Options.Create(new BankApiConfig
            {
                BaseUrl = mockServerUri,
                PaymentEndpoint = "/payments",
                TimeoutSeconds = 30
            }));

    private static BankPaymentRequest BuildRequest() => new()
    {
        CardNumber = "4111111111111111",
        ExpiryDate = "12/2027",
        Currency = "GBP",
        Amount = 1250,
        Cvv = "123"
    };
}
