using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.InvoiceService;

public sealed partial class Service(AppDbContext db, PaymentGatewayService.IPaymentGatewayService gateway, ILogger<Service> logger,
    TimeProvider clock, IValidator<Request.CreateRequest> validator,
    IValidator<Request.SuggestionRequest> suggestionValidator, IValidator<Request.UpdateRequest> updateValidator,
    IValidator<Request.ListRequest> listValidator, IValidator<Request.BookingListRequest> bookingListValidator,
    IValidator<Request.IssueRequest> issueValidator, IValidator<Request.RecordPaymentRequest> paymentValidator,
    IValidator<Request.CancelRequest> cancelValidator, NotificationService.INotificationService notifications) : IInvoiceService
{
    private const decimal MaxAmount = 9_999_999_999_999_999.99m;

    private static DateTime NextVersion(DateTime previous, DateTime now) => now > previous ? now : Truncate(previous).AddTicks(10);

    private static string InvoiceTypeLabel(InvoiceType type) => type switch
    {
        InvoiceType.DEPOSIT => "Deposit invoice",
        InvoiceType.FINAL => "Final invoice",
        InvoiceType.SUPPLEMENTARY => "Supplementary invoice",
        _ => "Invoice"
    };

    /// <summary>Says which transport blocks this invoice type, what state it is in and what has to happen first; carries the status for the form.</summary>
    private static ConflictException TransportStatusNotAllowed(InvoiceType type, string? transportCode, TransportStatus? status)
    {
        if (status is null)
            return new("This booking has no transport yet, so this invoice cannot be created.", "INVALID_TRANSPORT_STATUS",
                new { TransportStatus = (string?)null });
        var state = status switch
        {
            TransportStatus.CREATED => "just created",
            TransportStatus.PLANNING => "still being planned",
            TransportStatus.READY => "ready but has not left yet",
            TransportStatus.IN_TRANSIT => "still on the way",
            TransportStatus.COMPLETED => "already completed",
            _ => "cancelled"
        };
        var message = type switch
        {
            _ when status == TransportStatus.CANCELLED => $"Transport {transportCode} is cancelled, so no invoice can be created for it.",
            InvoiceType.FINAL => $"A final invoice can be created only after the horses are delivered to the customer. Transport {transportCode} is {state}.",
            InvoiceType.DEPOSIT => $"A deposit invoice can be created only before the trip starts. Transport {transportCode} is {state}.",
            _ => $"Transport {transportCode} is {state}, so this invoice cannot be created."
        };
        return new(message, "INVALID_TRANSPORT_STATUS", new { TransportStatus = status.ToString() });
    }

    private static ConflictException Modified() => new("The invoice was just modified. Please reload the page", "INVOICE_MODIFIED");
}
