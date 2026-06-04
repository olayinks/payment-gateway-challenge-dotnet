using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

using Shouldly;

namespace PaymentGateway.Api.Integration.Tests
{
    public class PaymentsApiIntegrationTests
    {
        [Fact]
        public async Task PostPaymentAsync_ReturnsCreated_WhenBankAuthorizes()
        {
            var bankClient = CreateBankClientMock(authorize: true);
            using var factory = CreateFactory(bankClient.Object);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/payments", CreatePaymentRequest());

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            var result = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();
            result.ShouldNotBeNull();
            result.Status.ShouldBe(PaymentStatus.Authorized);
            result.CardNumberLastFour.ShouldBe("1111");
        }

        [Fact]
        public async Task PostPaymentAsync_ReturnsOkWithDeclined_WhenBankIsUnavailable()
        {
            var bankClient = new Mock<IBankClient>();
            bankClient
                .Setup(c => c.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Bank service unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable));

            using var factory = CreateFactory(bankClient.Object);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/payments", CreatePaymentRequest());

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();
            result.ShouldNotBeNull();
            result.Status.ShouldBe(PaymentStatus.Declined);
            result.Errors.ShouldContain("Bank service unavailable");
        }

        [Fact]
        public async Task PostPaymentAsync_ReturnsOk_WhenSameIdempotencyKeyIsReusedWithSamePayload()
        {
            var bankClient = CreateBankClientMock(authorize: true);
            using var factory = CreateFactory(bankClient.Object);
            using var client = factory.CreateClient();

            var request = CreatePaymentRequest();
            var first = await SendPostPaymentRequest(client, request, "same-key");
            var second = await SendPostPaymentRequest(client, request, "same-key");

            first.StatusCode.ShouldBe(HttpStatusCode.Created);
            second.StatusCode.ShouldBe(HttpStatusCode.OK);

            var secondResult = await second.Content.ReadFromJsonAsync<PostPaymentResponse>();
            secondResult.ShouldNotBeNull();
            secondResult.Status.ShouldBe(PaymentStatus.Authorized);
        }

        [Fact]
        public async Task PostPaymentASync_Should_CallBankClient_Once_WhenSameIdempotencyKeyIsReusedWithSamePayload()
        {
            var bankClient = CreateBankClientMock(authorize: true);
            using var factory = CreateFactory(bankClient.Object);
            using var client = factory.CreateClient();

            var request = CreatePaymentRequest();
            await SendPostPaymentRequest(client, request, "same-key");
            await SendPostPaymentRequest(client, request, "same-key");

            bankClient.Verify(c => c.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PostPaymentAsync_ReturnsConflict_WhenSameIdempotencyKeyIsReusedWithDifferentPayload()
        {
            var bankClient = CreateBankClientMock(authorize: true);
            using var factory = CreateFactory(bankClient.Object);
            using var client = factory.CreateClient();

            var first = await SendPostPaymentRequest(client, CreatePaymentRequest("4111111111111111"), "same-key");
            var second = await SendPostPaymentRequest(client, CreatePaymentRequest("4242424242424242"), "same-key");

            first.StatusCode.ShouldBe(HttpStatusCode.Created);
            second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var secondResult = await second.Content.ReadFromJsonAsync<PostPaymentResponse>();
            secondResult.ShouldNotBeNull();
            secondResult?.Status.ShouldBe(PaymentStatus.Rejected);
            secondResult?.Errors.ShouldNotBeEmpty();
        }

        private static WebApplicationFactory<Program> CreateFactory(IBankClient bankClient)
        {
            return new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IBankClient>();
                    services.AddSingleton(bankClient);
                }));
        }

        private static async Task<HttpResponseMessage> SendPostPaymentRequest(HttpClient client, PostPaymentRequest request, string idempotencyKey)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Add("Idempotency-Key", idempotencyKey);
            return await client.SendAsync(httpRequest);
        }

        private static Mock<IBankClient> CreateBankClientMock(bool authorize)
        {
            var bankClient = new Mock<IBankClient>();
            bankClient
                .Setup(c => c.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BankPaymentResponse
                {
                    Authorized = authorize,
                    AuthorizationCode = authorize ? "auth-123" : string.Empty
                });
            return bankClient;
        }

        private static PostPaymentRequest CreatePaymentRequest(string cardNumber = "4111111111111111")
        {
            return new PostPaymentRequest
            {
                CardNumber = cardNumber,
                ExpiryMonth = 12,
                ExpiryYear = DateTime.UtcNow.Year + 1,
                Currency = "GBP",
                Amount = 1250,
                Cvv = "123",
            };
        }
    }
}
