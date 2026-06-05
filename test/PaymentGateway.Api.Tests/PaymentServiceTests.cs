using AutoMapper;

using Microsoft.Extensions.Logging;

using Moq;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Mapper;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Models.Validation;
using PaymentGateway.Api.Repository;
using PaymentGateway.Api.Services;

using Shouldly;

namespace PaymentGateway.Api.Tests;

public class PaymentServiceTests
{
    [Fact]
    public async Task GetPaymentAsync_returns_payment_when_it_exists()
    {
        var repository = new PaymentsRepository();
        var payment = new Payment { Id = Guid.NewGuid(), Status = PaymentStatus.Authorized };
        repository.Add(payment);
        var service = CreatePaymentService(repository, Mock.Of<IBankClient>());

        var result = await service.GetPaymentAsync(payment.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(payment.Id);
    }

    [Fact]
    public async Task GetPaymentAsync_throws_when_cancellation_is_requested()
    {
        var service = CreatePaymentService(new PaymentsRepository(), Mock.Of<IBankClient>());
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            service.GetPaymentAsync(Guid.NewGuid(), cancellationTokenSource.Token));
    }

    [Fact]
    public async Task ProcessAsync_should_Authorize_From_Bank_and_stores_payment_with_last4Digit_card_number()
    {
        var repository = new PaymentsRepository();
        var bankClientMock = new Mock<IBankClient>();
        bankClientMock.Setup(b => b.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad" });
        var paymentService = CreatePaymentService(repository, bankClientMock.Object);

        var result = await paymentService.ProcessAsync(TestData.PaymentRequest(), null, CancellationToken.None);
        var storedPayment = repository.Get(result.Payment!.Id);
        var expectedExpiryDate = $"12/{TestData.PaymentRequest().ExpiryYear}";

        result.Payment.Status.ShouldBe(PaymentStatus.Authorized);
        result.Payment.CardNumberLastFour.ShouldBe("1111");
        storedPayment.ShouldNotBeNull();
        storedPayment.CardNumberLastFour.ShouldBe("1111");
        bankClientMock.Verify(client => client.AuthorizeAsync(
            It.Is<BankPaymentRequest>(r => r.CardNumber == "4111111111111111" && r.ExpiryDate == expectedExpiryDate),
            It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task ProcessAsync_stores_declined_payment_when_bank_declines()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = false, AuthorizationCode = string.Empty });
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest("4242424242424242"), null, CancellationToken.None);

        result.Payment.ShouldNotBeNull();
        result.Payment!.Status.ShouldBe(PaymentStatus.Declined);
        result.Payment.CardNumberLastFour.ShouldBe("4242");
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

        var result = await service.ProcessAsync(TestData.PaymentRequest("4000000000000010"), null, CancellationToken.None);
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

        var result = await service.ProcessAsync(TestData.PaymentRequest("123"), null, CancellationToken.None);

        result.Payment.ShouldBeNull();
        result.Errors.ShouldNotBeEmpty();
        bankClient.Verify(client => client.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task ProcessAsync_returns_existingPayment_when_idempotency_service_returns_existing_result()
    {
        var existingPayment = new Payment { Id = Guid.NewGuid(), Status = PaymentStatus.Authorized };
        var replay = new PaymentProcessingResult(existingPayment, AlreadyProcessed: true);
        var idempotencyService = new Mock<IIdempotencyService>();
        idempotencyService.Setup(s => s.Check("existing-key", It.IsAny<PostPaymentRequest>())).Returns(replay);
        var bankClient = new Mock<IBankClient>();
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object, idempotencyService.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest(), "existing-key", CancellationToken.None);

        result.AlreadyProcessed.ShouldBeTrue();
        result.Payment!.Id.ShouldBe(existingPayment.Id);
        bankClient.Verify(b => b.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_records_idempotency_after_bank_authorization()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        var idempotencyService = new Mock<IIdempotencyService>();
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object, idempotencyService.Object);

        var result = await service.ProcessAsync(TestData.PaymentRequest(), "my-key", CancellationToken.None);

        idempotencyService.Verify(s => s.Record("my-key", It.IsAny<PostPaymentRequest>(), result.Payment!.Id), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_does_not_record_idempotency_when_no_key_provided()
    {
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        var idempotencyService = new Mock<IIdempotencyService>();
        var service = CreatePaymentService(new PaymentsRepository(), bankClient.Object, idempotencyService.Object);

        await service.ProcessAsync(TestData.PaymentRequest(), null, CancellationToken.None);

        idempotencyService.Verify(s => s.Record(It.IsAny<string>(), It.IsAny<PostPaymentRequest>(), It.IsAny<Guid>()), Times.Never);
    }

    private static PaymentService CreatePaymentService(IPaymentsRepository repository, IBankClient bankClient, IIdempotencyService? idempotencyService = null)
    {
        var logger = new Logger<PaymentService>(new LoggerFactory());
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<PaymentProfile>());
        var mapper = mapperConfig.CreateMapper();
        idempotencyService ??= Mock.Of<IIdempotencyService>();
        return new PaymentService(repository, idempotencyService, logger, mapper, bankClient, new PostPaymentRequestValidator());
    }
}
