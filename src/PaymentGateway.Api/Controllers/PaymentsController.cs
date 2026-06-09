using AutoMapper;

using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController(IPaymentService paymentService, ILogger<PaymentsController> logger, IMapper mapper) : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetPaymentResponse?>> GetPayment(Guid id, CancellationToken cancellationToken)
    {
        var payment = await paymentService.GetPaymentAsync(id, cancellationToken);
        if (payment is null)
        {
            logger.LogInformation("Payment with id {PaymentId} was not found.", id);
            return NotFound();
        }
        logger.LogInformation("Payment with id {PaymentId} was retrieved successfully.", id);
        return new OkObjectResult(mapper.Map<GetPaymentResponse>(payment));
    }

    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync(PostPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey);
            var result = await paymentService.ProcessAsync(request, idempotencyKey, cancellationToken);
            if (result.Payment is null)
            {
                return BadRequest(new PostPaymentResponse
                {
                    Status = PaymentStatus.Rejected,
                    Errors = result.Errors,
                });
            }

            var response = mapper.Map<PostPaymentResponse>(result.Payment);

            if (result.AlreadyProcessed || result.Payment.Status == PaymentStatus.Declined)
            {
                return Ok(response);
            }
            return CreatedAtAction(nameof(GetPayment), new { id = result.Payment.Id }, response);
        }
        catch (IdempotencyConflictException ex)
        {
            Request.Headers.TryGetValue(IdempotencyKeyHeader, out var conflictingKey);
            logger.LogWarning(ex, "Idempotency key {IdempotencyKey} was reused with a different payload.", (string?)conflictingKey);

            return Conflict(new PostPaymentResponse
            {
                Status = PaymentStatus.Rejected,
                Errors = [ex.Message],
            });
        }
    }
}
