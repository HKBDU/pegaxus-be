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

public sealed partial class Service(AppDbContext db, PaymentGatewayService.IService gateway, ILogger<Service> logger,
    TimeProvider clock, IValidator<Request.CreateRequest> validator,
    IValidator<Request.SuggestionRequest> suggestionValidator, IValidator<Request.UpdateRequest> updateValidator,
    IValidator<Request.ListRequest> listValidator, IValidator<Request.BookingListRequest> bookingListValidator,
    IValidator<Request.IssueRequest> issueValidator, IValidator<Request.RecordPaymentRequest> paymentValidator,
    IValidator<Request.CancelRequest> cancelValidator, NotificationService.IService notifications) : IService
{
    private const decimal MaxAmount = 9_999_999_999_999_999.99m;

    private static DateTime NextVersion(DateTime previous, DateTime now) => now > previous ? now : Truncate(previous).AddTicks(10);

    private static ConflictException Modified() => new("The invoice was just modified. Please reload the page", "INVOICE_MODIFIED");
}
