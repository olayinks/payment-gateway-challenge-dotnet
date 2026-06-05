using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Repository;
using PaymentGateway.Api.Services;

using FluentValidation;
using PaymentGateway.Api.Models.Validation;
using Microsoft.Extensions.Options;
using PaymentGateway.Api.Mapper;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAutoMapper(typeof(PaymentProfile));
builder.Services.AddValidatorsFromAssemblyContaining<PostPaymentRequestValidator>();

builder.Services.Configure<BankApiConfig>(builder.Configuration.GetSection("BankApi"));

builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddHttpClient<IBankClient, BankClient>((serviceProvider, client) =>
{
    var bankApiConfig = serviceProvider.GetRequiredService<IOptions<BankApiConfig>>().Value;
    client.BaseAddress = bankApiConfig.BaseUrl;
    client.Timeout = TimeSpan.FromSeconds(bankApiConfig.TimeoutSeconds);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}