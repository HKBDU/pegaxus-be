using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

public sealed class Service(AppDbContext db, TimeProvider clock, IValidator<Request.CreateRequest> validator,
    IValidator<Request.SuggestionRequest> suggestionValidator) : IService
{
    private const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public async Task<Response.SuggestionResponse> GetSuggestionAsync(Guid accountId, Guid bookingId,
        Request.SuggestionRequest request, CancellationToken cancellationToken = default)
    {
        var allowed = await db.Employees.AsNoTracking().AnyAsync(employee => employee.AccountId == accountId &&
            employee.Role == EmployeeRole.LOGISTICS_MANAGER && employee.IsActive && !employee.IsDeleted &&
            !employee.Account.IsDeleted && employee.Account.Status == AccountStatus.ACTIVE &&
            employee.Account.AccountType == AccountType.EMPLOYEE, cancellationToken);
        if (!allowed)
            throw new ForbiddenException("Only Logistics Manager is authorized to check invoice eligibility.");

        var validation = suggestionValidator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(error => new FieldError("type", error.ErrorMessage)).ToArray());
        var type = Enum.Parse<InvoiceType>(request.Type.Trim(), true);

        var booking = await db.Bookings.AsNoTracking().Where(item => item.BookingId == bookingId)
            .Select(item => new
            {
                item.BookingId, item.BookingCode, item.Status,
                TransportId = item.Transport == null ? (Guid?)null : item.Transport.TransportId,
                TransportStatus = item.Transport == null ? (TransportStatus?)null : item.Transport.Status
            }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("Booking must be APPROVED", "INVALID_BOOKING_STATUS");

        var validTransport = booking.TransportStatus.HasValue && type switch
        {
            InvoiceType.DEPOSIT => booking.TransportStatus is TransportStatus.CREATED or TransportStatus.PLANNING or TransportStatus.READY,
            InvoiceType.FINAL => booking.TransportStatus == TransportStatus.COMPLETED,
            _ => booking.TransportStatus != TransportStatus.CANCELLED
        };
        if (!validTransport)
            throw new ConflictException("Transport status does not allow this invoice type", "INVALID_TRANSPORT_STATUS");

        var invoices = await db.Invoices.AsNoTracking().Where(invoice => invoice.BookingId == bookingId)
            .OrderBy(invoice => invoice.CreatedAt).ThenBy(invoice => invoice.InvoiceId)
            .Select(invoice => new { invoice.Type, invoice.Status, invoice.InvoiceCode, invoice.Currency, invoice.TotalAmount })
            .ToListAsync(cancellationToken);
        if (type == InvoiceType.FINAL && invoices.Any(invoice => invoice.Type == InvoiceType.FINAL && invoice.Status != InvoiceStatus.CANCELLED))
            throw new ConflictException("This booking already has a FINAL invoice. A second FINAL invoice is only allowed after the previous one is cancelled.",
                "FINAL_INVOICE_ALREADY_EXISTS");
        if (invoices.Any(invoice => invoice.Type == type && invoice.Status is InvoiceStatus.DRAFT or InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE))
            throw new ConflictException("An unresolved invoice of the same type already exists. Please edit or cancel the existing invoice.",
                "INVOICE_ALREADY_EXISTS");
        if (type == InvoiceType.CREDIT_NOTE && !invoices.Any(invoice => invoice.Type == InvoiceType.FINAL && invoice.Status == InvoiceStatus.PAID))
            throw new UnprocessableEntityException("Cannot create a claim/compensation invoice. The FINAL invoice of this booking must be PAID first.",
                "UNPAID_FINAL_INVOICE");

        var deposits = invoices.Where(invoice => invoice.Type == InvoiceType.DEPOSIT && invoice.Status == InvoiceStatus.PAID).ToArray();
        if (type == InvoiceType.FINAL && deposits.Select(deposit => deposit.Currency).Distinct().Skip(1).Any())
            throw new UnprocessableEntityException("Paid deposits must use the same currency for a FINAL invoice.", "CURRENCY_MISMATCH");
        var items = type == InvoiceType.FINAL
            ? deposits.Select(deposit => new Response.ItemResponse(null, nameof(InvoiceItemType.OTHER),
                $"Deposit deduction ({deposit.InvoiceCode})", null, 1, -deposit.TotalAmount, -deposit.TotalAmount,
                null, null, true, "Deposit is not refunded in cash; it is deducted from this final invoice.")).ToArray()
            : [];
        return new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), booking.TransportId!.Value,
            booking.TransportStatus!.Value.ToString(), type.ToString(), deposits.FirstOrDefault()?.Currency,
            type != InvoiceType.QUOTATION, items);
    }

    public async Task<Response.DraftResponse> CreateAsync(Guid accountId, Guid bookingId,
        Request.CreateRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : await db.Database.BeginTransactionAsync(cancellationToken);
        var employee = await db.Employees.AsNoTracking().Include(item => item.Account)
            .SingleOrDefaultAsync(item => item.AccountId == accountId && item.Role == EmployeeRole.LOGISTICS_MANAGER &&
                item.IsActive && !item.IsDeleted && !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE &&
                item.Account.AccountType == AccountType.EMPLOYEE, cancellationToken)
            ?? throw new ForbiddenException("Only Logistics Manager is authorized to create invoices.");
        var validation = validator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(error => new FieldError(
                string.Join(".", error.PropertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)), error.ErrorMessage)).ToArray());

        var type = Enum.Parse<InvoiceType>(request.Type.Trim(), true);
        var booking = await db.Bookings.AsNoTracking().Include(item => item.Customer).ThenInclude(item => item.Account)
            .Include(item => item.Transport).ThenInclude(item => item!.TransportPlans)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("Booking must be APPROVED", "INVALID_BOOKING_STATUS");
        var validTransport = booking.Transport is not null && type switch
        {
            InvoiceType.DEPOSIT => booking.Transport.Status is TransportStatus.CREATED or TransportStatus.PLANNING or TransportStatus.READY,
            InvoiceType.FINAL => booking.Transport.Status == TransportStatus.COMPLETED,
            _ => booking.Transport.Status != TransportStatus.CANCELLED
        };
        if (!validTransport)
            throw new ConflictException("Transport status does not allow this invoice type", "INVALID_TRANSPORT_STATUS");

        var invoices = await db.Invoices.AsNoTracking().Where(item => item.BookingId == bookingId)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceId)
            .Select(item => new { item.Type, item.Status, item.InvoiceCode, item.Currency, item.TotalAmount })
            .ToListAsync(cancellationToken);
        if (type == InvoiceType.FINAL && invoices.Any(item => item.Type == InvoiceType.FINAL && item.Status != InvoiceStatus.CANCELLED))
            throw new ConflictException("This booking already has a FINAL invoice. A second FINAL invoice is only allowed after the previous one is cancelled.",
                "FINAL_INVOICE_ALREADY_EXISTS");
        if (invoices.Any(item => item.Type == type && item.Status is InvoiceStatus.DRAFT or InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE))
            throw new ConflictException("An unresolved invoice of the same type already exists. Please edit or cancel the existing invoice.", "INVOICE_ALREADY_EXISTS");
        if (type == InvoiceType.CREDIT_NOTE && !invoices.Any(item => item.Type == InvoiceType.FINAL && item.Status == InvoiceStatus.PAID))
            throw new UnprocessableEntityException("Cannot create a claim/compensation invoice. The FINAL invoice of this booking must be PAID first.", "UNPAID_FINAL_INVOICE");
        var deposits = invoices.Where(item => type == InvoiceType.FINAL && item.Type == InvoiceType.DEPOSIT && item.Status == InvoiceStatus.PAID).ToArray();
        if (deposits.Any(item => item.Currency != request.Currency))
            throw new UnprocessableEntityException("Currency must match the paid deposit currency", "CURRENCY_MISMATCH");

        var requestedItems = request.Items!.Where(item => !item.IsDepositDeduction).ToArray();
        var horseIds = requestedItems.Where(item => item.HorseId.HasValue).Select(item => item.HorseId!.Value).Distinct().ToArray();
        var horses = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == bookingId && horseIds.Contains(item.HorseId))
            .Select(item => new Response.HorseResponse(item.HorseId, item.Horse.HorseName)).ToDictionaryAsync(item => item.HorseId, cancellationToken);
        var invalidHorses = request.Items!.Select((item, index) => (item, index))
            .Where(entry => !entry.item.IsDepositDeduction && entry.item.HorseId.HasValue && !horses.ContainsKey(entry.item.HorseId.Value))
            .Select(entry => new FieldError($"items[{entry.index}].horseId", "Horse must belong to this booking.")).ToArray();
        if (invalidHorses.Length > 0) throw new FieldValidationException(invalidHorses);

        var now = new DateTime(clock.GetUtcNow().Ticks / 10 * 10, DateTimeKind.Unspecified);
        var invoice = new Invoice
        {
            InvoiceId = Guid.NewGuid(), BookingId = bookingId, EmployeeId = employee.EmployeeId, Type = type,
            Status = InvoiceStatus.DRAFT, Currency = request.Currency, DueDate = request.DueDate,
            BillingNote = request.Note, CreatedAt = now, UpdatedAt = now
        };
        try
        {
            foreach (var item in requestedItems)
                invoice.InvoiceItems.Add(new InvoiceItem
                {
                    InvoiceItemId = Guid.NewGuid(), ItemType = Enum.Parse<InvoiceItemType>(item.ItemType.Trim(), true),
                    Description = item.Description.Trim(), Quantity = item.Quantity, UnitPrice = item.Price,
                    LineAmount = Money(decimal.Round(item.Quantity * item.Price, 2, MidpointRounding.AwayFromZero)),
                    HorseId = item.HorseId, Note = item.Note, CreatedAt = now, UpdatedAt = now
                });
            invoice.Subtotal = Money(invoice.InvoiceItems.Sum(item => item.LineAmount));
            invoice.TaxAmount = Money(decimal.Round(invoice.Subtotal * 0.20m, 2, MidpointRounding.AwayFromZero));
            foreach (var deposit in deposits)
                invoice.InvoiceItems.Add(new InvoiceItem
                {
                    InvoiceItemId = Guid.NewGuid(), ItemType = InvoiceItemType.OTHER,
                    Description = $"Deposit deduction ({deposit.InvoiceCode})", Quantity = 1,
                    UnitPrice = -deposit.TotalAmount, LineAmount = -deposit.TotalAmount,
                    Note = "Deposit is not refunded in cash; it is deducted from this final invoice.", CreatedAt = now, UpdatedAt = now
                });
            invoice.TotalAmount = Money(invoice.Subtotal + invoice.TaxAmount - deposits.Sum(item => item.TotalAmount));
        }
        catch (OverflowException)
        {
            throw new FieldValidationException([new("items", "Invoice amounts must fit decimal(18,2).")]);
        }
        invoice.InvoiceStatusHistories.Add(new InvoiceStatusHistory
        {
            InvoiceStatusHistoryId = Guid.NewGuid(), Type = InvoiceHistoryType.CREATE, NewStatus = InvoiceStatus.DRAFT,
            ChangedByAccountId = accountId, ChangedAt = now, CreatedAt = now
        });
        db.Invoices.Add(invoice);
        await SaveWithUniqueCodeAsync(invoice, cancellationToken);
        var response = Map(invoice, booking, employee, horses);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private async Task SaveWithUniqueCodeAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            invoice.InvoiceCode = $"INV-{invoice.CreatedAt:yyyyMMdd}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (await db.Invoices.AsNoTracking().AnyAsync(item => item.InvoiceCode == invoice.InvoiceCode, cancellationToken)) continue;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "Invoice_invoice_code_key" })
            {
                // EF restores its savepoint after a failed insert; retry the code inside the same transaction.
            }
        }
        throw new InvalidOperationException("Unable to generate a unique INV code");
    }

    private static decimal Money(decimal amount) => amount is >= -MaxAmount and <= MaxAmount
        ? amount : throw new FieldValidationException([new("items", "Invoice amounts must fit decimal(18,2).")]);

    private static Response.DraftResponse Map(Invoice invoice, Booking booking, Employee employee,
        IReadOnlyDictionary<Guid, Response.HorseResponse> horses)
    {
        var transport = booking.Transport!;
        var plan = transport.TransportPlans.Where(item => item.Status != TransportPlanStatus.CANCELLED)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.TransportPlanId).FirstOrDefault();
        var customer = booking.Customer;
        return new()
        {
            InvoiceId = invoice.InvoiceId, InvoiceNumber = invoice.InvoiceCode, Type = invoice.Type.ToString(),
            InvoiceDate = DateOnly.FromDateTime(invoice.CreatedAt), DueDate = invoice.DueDate!.Value,
            Currency = invoice.Currency, Note = invoice.BillingNote,
            Customer = new(customer.CustomerId, customer.CustomerCode, customer.FullName, customer.Phone, customer.Address, customer.Account.Email),
            Items = invoice.InvoiceItems.Select(item => new Response.ItemResponse(item.InvoiceItemId, item.ItemType.ToString(),
                item.Description, null, item.Quantity, item.UnitPrice, item.LineAmount,
                item.HorseId.HasValue ? horses[item.HorseId.Value] : null, null,
                invoice.Type == InvoiceType.FINAL && item.LineAmount < 0, item.Note)).ToArray(),
            Summary = new(invoice.Subtotal, invoice.TaxAmount, invoice.InvoiceItems.Where(item => item.LineAmount < 0).Sum(item => item.LineAmount), invoice.TotalAmount),
            ContactPerson = new(employee.FullName, employee.Account.Email, employee.Phone),
            Booking = new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), booking.OriginAddress,
                booking.OriginCountry, booking.DestinationAddress, booking.DestinationCountry, booking.RequestedDepartureDate),
            Transport = new(transport.TransportId, transport.TransportCode, transport.Status.ToString()),
            TransportPlan = plan is null ? null : new(plan.TransportPlanId, plan.PlanCode, plan.Status.ToString()),
            CreatedAt = new(DateTime.SpecifyKind(invoice.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = new(DateTime.SpecifyKind(invoice.UpdatedAt, DateTimeKind.Utc))
        };
    }
}
