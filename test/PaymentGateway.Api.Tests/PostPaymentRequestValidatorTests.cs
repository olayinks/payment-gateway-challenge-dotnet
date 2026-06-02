using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Validation;

using Shouldly;
namespace PaymentGateway.Api.Tests;

public class PostPaymentRequestValidatorTests
{
    private readonly PostPaymentRequestValidator _validator = new();

    [Fact]
    public void Validate_accepts_valid_payment_request()
    {
        var validator = new PostPaymentRequestValidator();

        var result = validator.Validate(TestData.PaymentRequest());

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1234567890123456")]
    [InlineData("4111111111111")]
    [InlineData("41111111111111111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("abcd1234efgh5678")]
    [InlineData("")]
    [InlineData(null)]

    public void Should_Have_Error_When_CardNumber_Is_Invalid(string cardNumber)
    {
        var result = _validator.Validate(TestData.PaymentRequest(cardNumber));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "CardNumber");

    }

    [Fact]
    public void Should_Have_Error_When_ExpiryDate_Is_Past()
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 1,
            ExpiryYear = DateTime.UtcNow.Year - 1,
            Amount = 100,
            Currency = "USD",
            Cvv = "123"
        };

        var result = _validator.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == "Expiry date must not be in the past.");
    }
    [Fact]
    public void Should_Have_Valid_Expiry_Month()
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 13,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Amount = 100,
            Currency = "USD",
            Cvv = "123"
        };

        var result = _validator.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "ExpiryMonth");
        result.Errors.ShouldContain(e => e.ErrorMessage == "Invalid expiry month. Must be between 1 and 12.");
    }

    [Theory]
    [InlineData("GP")]
    [InlineData("GB")]
    [InlineData("USDA")]
    [InlineData("usd")]
    public void Should_Have_Valid_Currency(string currency)
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Amount = 100,
            Currency = currency,
            Cvv = "123"
        };

        var result = _validator.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "Currency");
        result.Errors.ShouldContain(e => e.ErrorMessage == "Currency must be a valid 3-letter ISO code.");
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abc")]
    public void Should_Be_Valid_CVV(string cvv)
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Amount = 100,
            Currency = "USD",
            Cvv = cvv
        };

        var result = _validator.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "Cvv");
        result.Errors.ShouldContain(e => e.ErrorMessage == "CVV must be 3 or 4 digits long.");
    }
}
