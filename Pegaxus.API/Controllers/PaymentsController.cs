using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Exceptions;

using PaymentService = Pegaxus.Service.PaymentService;

namespace Pegaxus.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/payments")]
public sealed class PaymentsController(PaymentService.IPaymentService service) : ApiControllerBase
{
    /// <summary>Process a SePay payment gateway IPN.</summary>
    /// <remarks>Configure SePay IPN authentication as SECRET_KEY. X-Secret-Key is required; JWT is not used.</remarks>
    /// <param name="request">The SePay order and transaction notification. The order invoice number is the receipt code.</param>
    /// <param name="secretKey">The secret configured for IPN authentication in the SePay merchant dashboard.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after processing or acknowledging an already processed payment.</returns>
    /// <exception cref="BadRequestException">The webhook authentication or payload is invalid.</exception>
    /// <exception cref="NotFoundException">The receipt code does not exist.</exception>
    /// <exception cref="UnprocessableEntityException">The amount, currency, or invoice type cannot be accepted.</exception>
    /// <exception cref="ConflictException">The invoice status is invalid or the transaction was already recorded.</exception>
    [HttpPost("webhook")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Webhook([FromBody] PaymentService.Request.WebhookRequest request,
        [FromHeader(Name = "X-Secret-Key")] string? secretKey, CancellationToken cancellationToken)
    {
        await service.HandleWebhookAsync(request, Request.Headers["X-Secret-Key"].Count == 1 ? secretKey : null, cancellationToken);
        return Success(null, "Webhook processed successfully");
    }
}
