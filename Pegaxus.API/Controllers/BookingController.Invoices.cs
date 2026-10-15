using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;
using Pegaxus.Service.Exceptions;

using InvoiceService = Pegaxus.Service.InvoiceService;

namespace Pegaxus.API.Controllers;

public sealed partial class BookingController
{
    /// <summary>
    /// Check invoice eligibility and preview deposit deductions.
    /// </summary>
    /// <remarks>Only Logistics Managers may check an APPROVED booking. Invoice type determines the allowed transport status, duplicate checks and paid FINAL prerequisite for CREDIT_NOTE. FINAL previews contain one read-only deduction per paid deposit; other types return no fee suggestions. This endpoint does not write data.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The required invoice type: QUOTATION, DEPOSIT, FINAL, SUPPLEMENTARY or CREDIT_NOTE.</param>
    /// <param name="invoiceService">The injected invoice service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with booking and transport details, payment requirement and deposit deduction previews.</returns>
    /// <exception cref="BadRequestException">The invoice type is missing or invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The booking does not exist.</exception>
    /// <exception cref="ConflictException">The booking or transport status is invalid, or a conflicting invoice exists.</exception>
    /// <exception cref="UnprocessableEntityException">A CREDIT_NOTE requires a paid FINAL invoice; FINAL deductions cannot combine deposits with different currencies.</exception>
    [HttpGet("{bookingId:guid}/invoices/suggestion")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> GetInvoiceSuggestion(Guid bookingId,
        [FromQuery] InvoiceService.Request.SuggestionRequest request,
        [FromServices] InvoiceService.IInvoiceService invoiceService, CancellationToken cancellationToken)
    {
        var response = await invoiceService.GetSuggestionAsync(AccountId, bookingId, request, cancellationToken);
        return Success(response, "Invoice precheck retrieved successfully");
    }

    /// <summary>
    /// Create a draft invoice.
    /// </summary>
    /// <remarks>Only active Logistics Managers may create invoices for APPROVED bookings. The server validates transport status and duplicate invoices, calculates fixed 20 percent tax without discounts, adds paid deposit deductions for FINAL, and records CREATE history atomically. Empty draft items are allowed. Payment and itemDate fields are ignored until their database fields are available.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The invoice type, EUR or VND currency, due date and manually entered fee items.</param>
    /// <param name="invoiceService">The injected invoice service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the complete draft invoice, calculated totals and available actions.</returns>
    /// <exception cref="BadRequestException">Request fields, monetary precision or horse references are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The booking does not exist.</exception>
    /// <exception cref="ConflictException">The booking or transport status is invalid, a conflicting invoice exists, or data changed concurrently.</exception>
    /// <exception cref="UnprocessableEntityException">A CREDIT_NOTE requires a paid FINAL invoice; FINAL currency must match all paid deposits.</exception>
    [HttpPost("{bookingId:guid}/invoices")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> CreateInvoice(Guid bookingId, [FromBody] InvoiceService.Request.CreateRequest request,
        [FromServices] InvoiceService.IInvoiceService invoiceService, CancellationToken cancellationToken)
    {
        var response = await invoiceService.CreateAsync(AccountId, bookingId, request, cancellationToken);
        return SuccessCreated(response, "Invoice created successfully");
    }
}
