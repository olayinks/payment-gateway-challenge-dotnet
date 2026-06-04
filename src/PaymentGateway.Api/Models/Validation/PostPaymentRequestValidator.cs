
using FluentValidation;

using PaymentGateway.Api.Models.Requests;
namespace PaymentGateway.Api.Models.Validation;

public class PostPaymentRequestValidator : AbstractValidator<PostPaymentRequest>
{
    public PostPaymentRequestValidator()
    {
        RuleFor(x => x.CardNumber)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Matches("^[0-9]{14,19}$").WithMessage("Card number must be between 14 and 19 digits long and contain only digits.");
        RuleFor(x => x.CardNumber)
            .CreditCard()
            .WithMessage("Card number must be a valid credit card number.");

        RuleFor(x => x.ExpiryMonth).InclusiveBetween(1, 12).WithMessage("Invalid expiry month. Must be between 1 and 12.");
        RuleFor(x => x).Must(HaveFutureExpiry).WithMessage("Expiry date must not be in the past.");
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[A-Z]{3}$").WithMessage("Currency must be a valid 3-letter ISO code.");
        RuleFor(x => x.Cvv).NotEmpty().Matches("^[0-9]{3,4}$").WithMessage("CVV must be 3 or 4 digits long.");
    }

    private static bool HaveFutureExpiry(PostPaymentRequest request)
    {
        var now = DateTime.UtcNow;
        return request.ExpiryYear > now.Year || request.ExpiryYear == now.Year && request.ExpiryMonth >= now.Month;
    }
   
}