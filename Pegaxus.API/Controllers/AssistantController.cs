using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Assistant;

namespace Pegaxus.API.Controllers;

/// <summary>Stateless, read-only chat for the authenticated Customer.</summary>
[ApiController]
[Route("api/v1/assistant")]
[Authorize(Policy = JwtExtensions.CustomerPolicy)]
public sealed class AssistantController(
    IAssistantService assistant,
    IValidator<ChatRequest> validator) : ApiControllerBase
{
    /// <summary>Ask about the current Customer's bookings, dossier requirements, and transport progress.</summary>
    /// <remarks>Authentication: Customer access token required. Identity comes from the authenticated token. The request cannot select an account, tool, or model.</remarks>
    /// <param name="request">The conversation messages and optional booking context.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A plain text answer and up to three internal domain references.</returns>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(object), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        var invalid = await ValidateRequestAsync(request, validator, cancellationToken, traceId: HttpContext.TraceIdentifier);
        if (invalid is not null) return invalid;
        return Success(await assistant.ChatAsync(AccountId, request, cancellationToken), "Assistant response generated successfully.");
    }
}
