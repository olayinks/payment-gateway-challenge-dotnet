using System.Net;
using System.Net.Http.Json;

using AutoMapper;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Moq;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Mapper;
using PaymentGateway.Api.Repository;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

using Shouldly;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private readonly Random _random = new();

    [Fact]
    public async Task PostPaymentAsync_should_return_created_for_authorized_payment()
    {
        // Arrange
        var payment = CreatePayment(PaymentStatus.Authorized);
        var paymentServiceMock = new Mock<IPaymentService>();
        paymentServiceMock.Setup(service => service.ProcessAsync(It.IsAny<PostPaymentRequest>(),
        It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Models.PaymentProcessingResult(payment, alreadyProcessed: false));
        var controller = CreateController(paymentServiceMock.Object);

        // Act
        var result = await controller.PostPaymentAsync(TestData.PaymentRequest(), CancellationToken.None);

        // Assert
        var createdResult = result.Result.ShouldBeOfType<CreatedAtActionResult>();
        createdResult.ActionName.ShouldBe(nameof(PaymentsController.GetPayment));
        createdResult.RouteValues!["id"].ShouldBe(payment.Id);
        createdResult.StatusCode.ShouldBe(StatusCodes.Status201Created);
        var response = createdResult.Value.ShouldBeOfType<PostPaymentResponse>();
        response?.Status.ShouldBe(PaymentStatus.Authorized);
    }

    [Fact]
    public async Task PostPaymentAsync_returns_bad_request_when_service_returns_validation_errors()
    {
        var paymentService = new Mock<IPaymentService>();
        paymentService
            .Setup(service => service.ProcessAsync(It.IsAny<PostPaymentRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentProcessingResult(Payment: null, AlreadyProcessed: false, Errors: new[] { "Card number is invalid." }));
        var controller = CreateController(paymentService.Object);

        var result = await controller.PostPaymentAsync(TestData.PaymentRequest("123"), CancellationToken.None);

        var badRequest = result.Result.ShouldBeOfType<BadRequestObjectResult>();
        var response = badRequest.Value.ShouldBeOfType<PostPaymentResponse>();
        response.Status.ShouldBe(PaymentStatus.Rejected);
        response.Errors.ShouldContain("Card number is invalid.");
    }
    [Fact]
    public async Task PostPaymentAsync_returns_ok_200statuscode_for_existing_idempotent_key()
    {
        var payment = CreatePayment(PaymentStatus.Authorized);
        var paymentService = new Mock<IPaymentService>();
        paymentService
            .Setup(service => service.ProcessAsync(It.IsAny<PostPaymentRequest>(), "same-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentProcessingResult(payment, alreadyProcessed: true));
        var controller = CreateController(paymentService.Object);
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "same-key";

        var result = await controller.PostPaymentAsync(TestData.PaymentRequest(), CancellationToken.None);

        var ok = result.Result.ShouldBeOfType<OkObjectResult>();
        var response = ok.Value.ShouldBeOfType<PostPaymentResponse>();
        response.Id.ShouldBe(payment.Id);
        ok.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task PostPaymentAsync_returns_ok_when_payment_is_declined_by_bank()
    {
        var payment = CreatePayment(PaymentStatus.Declined, errorMessage: "Bank service unavailable");
        var paymentService = new Mock<IPaymentService>();
        paymentService
            .Setup(service => service.ProcessAsync(It.IsAny<PostPaymentRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentProcessingResult(payment, alreadyProcessed: false));
        var controller = CreateController(paymentService.Object);

        var result = await controller.PostPaymentAsync(TestData.PaymentRequest(), CancellationToken.None);

        var ok = result.Result.ShouldBeOfType<OkObjectResult>();
        ok.StatusCode.ShouldBe(StatusCodes.Status200OK);
        var response = ok.Value.ShouldBeOfType<PostPaymentResponse>();
        response.Status.ShouldBe(PaymentStatus.Declined);
        response.Errors.ShouldContain("Bank service unavailable");
    }

    [Fact]
    public async Task PostPaymentAsync_returns_conflict_for_idempotency_conflict()
    {
        var paymentService = new Mock<IPaymentService>();
        paymentService
            .Setup(service => service.ProcessAsync(It.IsAny<PostPaymentRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdempotencyConflictException("same-key"));
        var controller = CreateController(paymentService.Object);

        var result = await controller.PostPaymentAsync(TestData.PaymentRequest(), CancellationToken.None);

        var conflict = result.Result.ShouldBeOfType<ConflictObjectResult>();
        var response = conflict.Value.ShouldBeOfType<PostPaymentResponse>();
        response.Status.ShouldBe(PaymentStatus.Rejected);
        response.Errors.ShouldNotBeEmpty();
    }



    [Fact]
    public async Task GetPaymentAsync_returns_ok_when_payment_exists()
    {
        // Arrange
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            ExpiryYear = _random.Next(2026, 2030),
            ExpiryMonth = _random.Next(7, 12),
            Amount = _random.Next(1, 10000),
            CardNumberLastFour = _random.Next(1111, 9999).ToString(),
            Currency = "GBP"
        };

        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPaymentsRepository>();
                services.AddSingleton<IPaymentsRepository>(paymentsRepository);
            }))
            .CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{payment.Id}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        paymentResponse.ShouldNotBeNull();
        paymentResponse.Id.ShouldBe(payment.Id);
    }

    [Fact]
    public async Task GetPaymentAsync_returns_not_found_when_payment_does_not_exist()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static PaymentsController CreateController(IPaymentService paymentService)
    {
        return new PaymentsController(paymentService, Mock.Of<ILogger<PaymentsController>>(), CreateMapper())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private Payment CreatePayment(PaymentStatus status, string? errorMessage = null)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            Status = status,
            CardNumberLastFour = _random.Next(1111, 9999).ToString(),
            ExpiryMonth = _random.Next(7, 12),
            ExpiryYear = _random.Next(2026, 2030),
            Currency = "GBP",
            Amount = _random.Next(1, 10000),
            ErrorMessage = errorMessage
        };
    }

    private static IMapper CreateMapper()
    {
        return new MapperConfiguration(configuration => configuration.AddProfile<PaymentProfile>()).CreateMapper();
    }

}
