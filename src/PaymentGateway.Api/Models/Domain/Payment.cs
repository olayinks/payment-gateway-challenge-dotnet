
using System;

using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Models.Domain;

public class Payment
{
    public Guid Id { get; set; } = new Guid();
    public PaymentStatus Status { get; set; }
    public string CardNumberLastFour { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set;} = 0;
    public string Currency { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string? ErrorMessage { get; set; }

}