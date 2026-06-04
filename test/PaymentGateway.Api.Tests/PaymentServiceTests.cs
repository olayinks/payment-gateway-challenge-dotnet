using System.Threading.Tasks;

using AutoMapper;

using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;

using Moq;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Mapper;
using PaymentGateway.Api.Models.Validation;
using PaymentGateway.Api.Services;

using Shouldly;
namespace PaymentGateway.Api.Tests;

public class PaymentServiceTests
{
    [Fact]
    public async Task ProcessAsync_should_Authorize_From_Bank_and_stores_payment_with_last4Digit_card_number()
    {
        // Arrange
        var repository = new PaymentsRepository();
        var bankClientMock = new Mock<IBankClient>();
        bankClientMock.Setup(b => b.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad" });

        var paymentService = CreatePaymentService(repository, bankClientMock.Object);

        // Act
        var result = await paymentService.ProcessAsync(TestData.PaymentRequest(), null, CancellationToken.None);
        var storedPayment = repository.Get(result.Payment!.Id);
        var expectedExpiryDate = $"12/{TestData.PaymentRequest().ExpiryYear}";

        // Assert
        result.Payment.Status.ShouldBe(PaymentStatus.Authorized);
        result.Payment.CardNumberLastFour.ShouldBe("1111");
        storedPayment.ShouldNotBeNull();
        storedPayment.CardNumberLastFour.ShouldBe("1111");
        bankClientMock.Verify(client => client.AuthorizeAsync(It.Is<BankPaymentRequest>(request => request.CardNumber == "4111111111111111" && request.ExpiryDate == expectedExpiryDate), It.IsAny<CancellationToken>()), Times.Once);


    }

    [Fact]
    public async Task ProcessAsync_sends_zero_padded_expiry_month_to_bank()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object);

        var request = TestData.PaymentRequest();
        request.ExpiryMonth = 3;

        await service.ProcessAsync(request, null, CancellationToken.None);

        bankClient.Verify(c => c.AuthorizeAsync(
            It.Is<BankPaymentRequest>(r => r.ExpiryDate == $"03/{request.ExpiryYear}"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_replays_same_payment_when_idempotency_key_and_payload_match()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        var repository = new PaymentsRepository();
        var service = CreatePaymentService(repository, bankClient.Object);
        var request = TestData.PaymentRequest();

        var first = await service.ProcessAsync(request, "same-key", CancellationToken.None);
        var second = await service.ProcessAsync(request, "same-key", CancellationToken.None);

        first.AlreadyProcessed.ShouldBeFalse();
        second.AlreadyProcessed.ShouldBeTrue();
        second.Payment!.Id.ShouldBe(first.Payment!.Id);
        bankClient.Verify(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_stores_declined_payment_when_bank_declines()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = false, AuthorizationCode = string.Empty });
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest("4242424242424242"), idempotencyKey: null, CancellationToken.None);
        result.Payment.ShouldNotBeNull();
        var payment = result.Payment!;

        payment.Status.ShouldBe(PaymentStatus.Declined);
        payment.CardNumberLastFour.ShouldBe("4242");
    }

    [Fact]
    public async Task ProcessAsync_rejects_same_idempotency_key_with_different_payload()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object);

        await service.ProcessAsync(TestData.PaymentRequest("4111111111111111"), "same-key", CancellationToken.None);

        await Should.ThrowAsync<IdempotencyConflictException>(() =>
            service.ProcessAsync(TestData.PaymentRequest("4417123456789113"), "same-key", CancellationToken.None));
    }


    [Fact]
    public async Task ProcessAsync_should_run_successfully_when_idempotencyKey_provided_But_record_does_not_exist()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        var repository = new PaymentsRepository();
        var service = CreatePaymentService(repository, bankClient.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest(), "new-key", CancellationToken.None);
        var storedPayment = repository.Get(result.Payment!.Id);

        result.Payment.Status.ShouldBe(PaymentStatus.Authorized);
        storedPayment.ShouldNotBeNull();
        storedPayment.Status.ShouldBe(PaymentStatus.Authorized);
    }

    [Fact]
    public async Task ProcessAsync_stores_rejected_payment_when_bank_is_unavailable()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Bank service unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable));
        var repository = new PaymentsRepository();
        var service = CreatePaymentService(repository, bankClient.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest("4000000000000010"), idempotencyKey: null, CancellationToken.None);
        var storedPayment = repository.Get(result.Payment!.Id);

        result.Payment.Status.ShouldBe(PaymentStatus.Declined);
        result.Payment.ErrorMessage.ShouldBe("Bank service unavailable");
        storedPayment.ShouldNotBeNull();
        storedPayment.Status.ShouldBe(PaymentStatus.Declined);
    }

    [Fact]
    public async Task ProcessAsync_returns_validation_errors_and_does_not_call_bank_when_request_is_invalid()
    {
        var bankClient = new Mock<IBankClient>();
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest("123"), idempotencyKey: null, CancellationToken.None);

        result.Payment.ShouldBeNull();
        result.Errors.ShouldNotBeEmpty();
        bankClient.Verify(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_trims_idempotency_key_before_lookup()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        var repository = new PaymentsRepository();
        var service = CreatePaymentService(repository, bankClient.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest(), "  same-key  ", CancellationToken.None);

        result.AlreadyProcessed.ShouldBeFalse();
        result.Payment.ShouldNotBeNull();
        repository.GetIdempotencyRecord("same-key").ShouldNotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_stores_declined_payment_when_bank_client_throws_unexpected_exception()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected failure"));
        var repository = new PaymentsRepository();
        var service = CreatePaymentService(repository, bankClient.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest(), null, CancellationToken.None);

        result.Payment.ShouldNotBeNull();
        result.Payment!.Status.ShouldBe(PaymentStatus.Declined);
        result.Payment.ErrorMessage.ShouldBe("An unexpected error occurred during payment processing");
        repository.Get(result.Payment.Id)!.Status.ShouldBe(PaymentStatus.Declined);
    }

    [Fact]
    public async Task ProcessAsync_should_throw_exception_when_IdempotencyKey_isUsed_with_different_payload()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object);

        await service.ProcessAsync(TestData.PaymentRequest("4111111111111111"), "same-key", CancellationToken.None);

        await Should.ThrowAsync<IdempotencyConflictException>(() =>
            service.ProcessAsync(TestData.PaymentRequest("4417123456789113"), "same-key", CancellationToken.None));
    }

    private static PaymentService CreatePaymentService(PaymentsRepository repository, IBankClient bankClient)
    {
        var logger = new Logger<PaymentService>(new LoggerFactory());
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<PaymentProfile>());
        var mapper = mapperConfig.CreateMapper();
        return new PaymentService(repository, logger, mapper, bankClient, new PostPaymentRequestValidator());
    }

}