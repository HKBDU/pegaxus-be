using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.InvoiceService;

public sealed partial class Service
{
    // QUOTATION < DEPOSIT < FINAL is the order a customer pays in. SUPPLEMENTARY can appear before FINAL, so it only follows the first two.
    // CREDIT_NOTE only exists after FINAL is paid, so it follows FINAL.
    private static InvoiceType[] DueAfter(InvoiceType type) => type switch
    {
        InvoiceType.DEPOSIT => [InvoiceType.QUOTATION],
        InvoiceType.FINAL or InvoiceType.SUPPLEMENTARY => [InvoiceType.QUOTATION, InvoiceType.DEPOSIT],
        InvoiceType.CREDIT_NOTE => [InvoiceType.FINAL],
        _ => []
    };

    private static InvoiceType[] DueBefore(InvoiceType type) => type switch
    {
        InvoiceType.QUOTATION => [InvoiceType.DEPOSIT, InvoiceType.FINAL],
        InvoiceType.DEPOSIT => [InvoiceType.FINAL],
        _ => []
    };

    /// <summary>A due date must fall on a later day than the invoices that come before it and on an earlier day than those that come after it. Cancelled invoices are ignored.</summary>
    private async Task EnsureDueDateOrderAsync(Guid bookingId, Guid? invoiceId, InvoiceType type, DateTimeOffset dueDate, CancellationToken cancellationToken)
    {
        var after = DueAfter(type);
        var before = DueBefore(type);
        if (after.Length == 0 && before.Length == 0) return;
        var others = await db.Invoices.AsNoTracking()
            .Where(item => item.BookingId == bookingId && item.InvoiceId != invoiceId && item.Status != InvoiceStatus.CANCELLED && item.DueDate != null &&
                (after.Contains(item.Type) || before.Contains(item.Type)))
            .Select(item => new { item.Type, item.InvoiceCode, DueDate = item.DueDate!.Value }).ToListAsync(cancellationToken);
        var day = DateOnly.FromDateTime(dueDate.UtcDateTime);
        var earlier = others.Where(item => after.Contains(item.Type)).OrderByDescending(item => item.DueDate).FirstOrDefault();
        if (earlier is not null && day <= DateOnly.FromDateTime(earlier.DueDate))
            throw new FieldValidationException([new("dueDate",
                $"Due date must be after the {UserFacingText.Label(earlier.Type)} invoice due date ({earlier.InvoiceCode}, {DateOnly.FromDateTime(earlier.DueDate):yyyy-MM-dd}).")]);
        var later = others.Where(item => before.Contains(item.Type)).OrderBy(item => item.DueDate).FirstOrDefault();
        if (later is not null && day >= DateOnly.FromDateTime(later.DueDate))
            throw new FieldValidationException([new("dueDate",
                $"Due date must be before the {UserFacingText.Label(later.Type)} invoice due date ({later.InvoiceCode}, {DateOnly.FromDateTime(later.DueDate):yyyy-MM-dd}).")]);
    }
}
