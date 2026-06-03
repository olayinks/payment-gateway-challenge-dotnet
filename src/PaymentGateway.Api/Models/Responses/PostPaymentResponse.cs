using PaymentGateway.Api.Enums;

using System;

namespace PaymentGateway.Api.Models.Responses;

public class PostPaymentResponse
{
    public Guid? Id { get; set; }
    public PaymentStatus Status { get; set; }
    public int? CardNumberLastFour { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? Currency { get; set; }
    public int? Amount { get; set; }
    public IReadOnlyCollection<string> Errors { get; set; } = [];
}
