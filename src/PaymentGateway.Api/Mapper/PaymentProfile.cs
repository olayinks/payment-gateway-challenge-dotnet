
using AutoMapper;

using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

public class PaymentProfile : Profile
{
    public PaymentProfile()
    {
        CreateMap<PostPaymentRequest, Payment>()
        .ForMember(destination => destination.CardNumberLastFour, option => option.MapFrom(source => source.CardNumber.Substring(source.CardNumber.Length - 4)));
        CreateMap<Payment, PostPaymentResponse>()
            .ForMember(destination => destination.Errors, option => option.MapFrom(source =>
                source.ErrorMessage == null ? Array.Empty<string>() : new[] { source.ErrorMessage }));
        ;
        CreateMap<Payment, GetPaymentResponse>();
        CreateMap<PostPaymentRequest, BankPaymentRequest>()
            .ForMember(destination => destination.ExpiryDate, option => option.MapFrom(source => $"{source.ExpiryMonth}/{source.ExpiryYear}"));

    }
}