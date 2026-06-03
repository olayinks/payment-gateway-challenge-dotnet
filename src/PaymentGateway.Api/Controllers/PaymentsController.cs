using AutoMapper;

using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController(IPaymentService paymentService, IValidator<PostPaymentRequest> validator, ILogger<PaymentsController> logger, IMapper mapper) : Controller
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PostPaymentResponse?>> GetPaymentAsync(Guid id, CancellationToken cancellationToken)
    {
        var payment = await paymentService.GetPaymentAsync(id, cancellationToken);

        return new OkObjectResult(payment);
    }

    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync(PostPaymentRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            logger.LogError("Validation failed for PostPaymentRequest: {Errors}", validationResult.Errors);
            return BadRequest(new PostPaymentResponse
            {
                Status = PaymentStatus.Rejected,
                Errors = [.. validationResult.Errors.Select(e => e.ErrorMessage)]
            });
        }
        try
        {
            Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey);
            var result = await paymentService.ProcessAsync(request, idempotencyKey, cancellationToken);
            var response = mapper.Map<PostPaymentResponse>(result.Payment);
            if (result.Payment.Status is PaymentStatus.Declined)
            {
                return StatusCode(StatusCodes.Status502BadGateway, response);
            }
            if (result.AlreadyProcessed)
            {
                return Ok(response);
            }
            return Created($"/api/payments/{result.Payment.Id}", response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing payment");
            return StatusCode(500, new PostPaymentResponse
            {
                Status = PaymentStatus.Rejected,
                Errors = ["An error occurred while processing the payment. Please try again later."]
            });
        }
    }
}