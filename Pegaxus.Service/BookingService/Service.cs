using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using BookingEntity = Pegaxus.Repository.Entities.Booking;
using MailService = Pegaxus.Service.MailService;

namespace Pegaxus.Service.BookingService;

public class Service : IService
{
    private readonly AppDbContext _db;
    private readonly MailService.IService _mailService;

    public Service(AppDbContext db, MailService.IService mailService)
    {
        _db = db;
        _mailService = mailService;
    }

    public async Task<Response.BookingDetailResponse> GetAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employeeRole = await _db.Employees
            .Where(employee =>
                employee.AccountId == accountId &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);

        var canViewAll = employeeRole == EmployeeRole.LOGISTICS_MANAGER;
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(item => item.Customer)
            .Include(item => item.BookingHorses)
                .ThenInclude(item => item.Horse)
            .Include(item => item.AssignedEmployee)
            .Include(item => item.Transport)
            .Where(item =>
                item.BookingId == bookingId &&
                (canViewAll || item.Customer.AccountId == accountId))
            .SingleOrDefaultAsync(cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        return new Response.BookingDetailResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode,
            Status = booking.Status.ToString(),
            StatusName = GetStatusName(booking.Status),
            Customer = new Response.CustomerResponse
            {
                CustomerId = booking.Customer.CustomerId,
                FullName = booking.Customer.FullName,
                Phone = booking.Customer.Phone
            },
            OriginAddress = booking.OriginAddress,
            OriginCountry = booking.OriginCountry,
            DestinationAddress = booking.DestinationAddress,
            DestinationCountry = booking.DestinationCountry,
            RequestedDepartureDate = booking.RequestedDepartureDate,
            RequestedArrivalDate = booking.RequestedArrivalDate,
            TotalHorses = booking.BookingHorses.Count,
            Horses = booking.BookingHorses.Select(item => new Response.BookingHorseResponse
            {
                HorseId = item.HorseId,
                HorseName = item.Horse.HorseName,
                Sex = item.Horse.Sex?.ToString(),
                Breed = item.Horse.Breed,
                MicrochipNumber = item.Horse.MicrochipNumber,
                PassportNumber = item.Horse.PassportNumber,
                SpecialHandlingNotes = item.SpecialHandlingNotes
            }).ToList(),
            SpecialRequirements = booking.SpecialRequirements,
            CustomerNote = booking.CustomerNote,
            AssignedEmployee = booking.AssignedEmployee is null
                ? null
                : new Response.AssignedEmployeeResponse
                {
                    EmployeeId = booking.AssignedEmployee.EmployeeId,
                    FullName = booking.AssignedEmployee.FullName
                },
            SubmittedAt = AsUtc(booking.SubmittedAt ?? booking.CreatedAt),
            ApprovedAt = AsUtcNullable(booking.ApprovedAt),
            RejectedAt = AsUtcNullable(booking.RejectedAt),
            CancelledAt = AsUtcNullable(booking.CancelledAt),
            RejectionReason = booking.RejectionReason,
            CancellationReason = booking.CancellationReason,
            Transport = booking.Transport is null
                ? null
                : new Response.TransportDetailResponse
                {
                    TransportId = booking.Transport.TransportId,
                    TransportCode = booking.Transport.TransportCode,
                    Status = booking.Transport.Status.ToString()
                },
            CreatedAt = AsUtc(booking.CreatedAt),
            UpdatedAt = AsUtc(booking.UpdatedAt)
        };
    }

    public async Task<IReadOnlyList<Response.BookingHistoryResponse>> GetHistoriesAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employeeRole = await _db.Employees
            .Where(employee =>
                employee.AccountId == accountId &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);

        var canViewAll = employeeRole == EmployeeRole.LOGISTICS_MANAGER;
        var bookingExists = await _db.Bookings.AnyAsync(booking =>
            booking.BookingId == bookingId &&
            (canViewAll || booking.Customer.AccountId == accountId),
            cancellationToken);
        if (!bookingExists)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        var histories = await _db.BookingStatusHistories
            .AsNoTracking()
            .Where(history => history.BookingId == bookingId)
            .Include(history => history.ChangedByAccount)
                .ThenInclude(account => account.CustomerAccount)
            .Include(history => history.ChangedByAccount)
                .ThenInclude(account => account.EmployeeAccount)
            .OrderBy(history => history.ChangedAt)
            .ThenBy(history => history.BookingStatusHistoryId)
            .ToListAsync(cancellationToken);

        return histories.Select(history => new Response.BookingHistoryResponse
        {
            HistoryId = history.BookingStatusHistoryId,
            Action = GetHistoryAction(history),
            PreviousStatus = history.PreviousStatus?.ToString(),
            NewStatus = history.NewStatus.ToString(),
            Reason = history.Reason,
            ChangedBy = new Response.ChangedByResponse
            {
                AccountId = history.ChangedByAccountId,
                FullName = GetAccountFullName(history.ChangedByAccount),
                Role = GetAccountRole(history.ChangedByAccount)
            },
            ChangedAt = AsUtc(history.ChangedAt)
        }).ToList();
    }

    public async Task<Response.BookingListResponse> GetListAsync(
        Guid accountId,
        Request.BookingListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PageIndex < 1)
            throw new BadRequestException("PageIndex must be greater than or equal to 1");
        if (request.PageSize < 1 || request.PageSize > 100)
            throw new BadRequestException("PageSize must be between 1 and 100");

        var employeeRole = await _db.Employees
            .Where(employee =>
                employee.AccountId == accountId &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);

        var customerId = await _db.Customers
            .Where(customer =>
                customer.AccountId == accountId &&
                !customer.IsDeleted &&
                !customer.Account.IsDeleted &&
                customer.Account.Status == AccountStatus.ACTIVE)
            .Select(customer => (Guid?)customer.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);

        var canViewAll = employeeRole == EmployeeRole.LOGISTICS_MANAGER;
        if (!canViewAll && customerId is null)
            throw new ForbiddenException("Only customers and logistics managers can access bookings");

        var query = _db.Bookings
            .AsNoTracking()
            .Where(booking => canViewAll || booking.CustomerId == customerId!.Value);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<BookingStatus>(request.Status.Trim(), true, out var status))
                throw new BadRequestException("Booking status is invalid", "INVALID_STATUS");
            query = query.Where(booking => booking.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = canViewAll
                ? query.Where(booking =>
                    booking.BookingCode.ToLower().Contains(search) ||
                    booking.Customer.FullName.ToLower().Contains(search) ||
                    booking.OriginAddress.ToLower().Contains(search) ||
                    booking.OriginCountry.ToLower().Contains(search) ||
                    booking.DestinationAddress.ToLower().Contains(search) ||
                    booking.DestinationCountry.ToLower().Contains(search))
                : query.Where(booking =>
                    booking.BookingCode.ToLower().Contains(search) ||
                    booking.OriginAddress.ToLower().Contains(search) ||
                    booking.OriginCountry.ToLower().Contains(search) ||
                    booking.DestinationAddress.ToLower().Contains(search) ||
                    booking.DestinationCountry.ToLower().Contains(search));
        }

        query = ApplySorting(query, request.SortBy);

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(booking => new Response.BookingListItemResponse
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode,
                Status = booking.Status.ToString(),
                StatusName = GetStatusName(booking.Status),
                CustomerName = booking.Customer.FullName,
                OriginAddress = booking.OriginAddress,
                OriginCountry = booking.OriginCountry,
                DestinationAddress = booking.DestinationAddress,
                DestinationCountry = booking.DestinationCountry,
                RequestedDepartureDate = booking.RequestedDepartureDate,
                TotalHorses = booking.BookingHorses.Count,
                CreatedAt = AsUtc(booking.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        return new Response.BookingListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)request.PageSize)
        };
    }

    public async Task AddHorseIntoBookingAsync(
        Guid accountId,
        Guid bookingId,
        Request.AddHorseRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedPendingBookingAsync(accountId, bookingId, cancellationToken);
        var horse = await _db.Horses.SingleOrDefaultAsync(item =>
            item.HorseId == request.HorseId &&
            item.CustomerId == booking.CustomerId &&
            !item.IsDeleted,
            cancellationToken);
        if (horse is null)
            throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");

        if (await _db.BookingHorses.AnyAsync(item =>
                item.BookingId == bookingId && item.HorseId == request.HorseId,
                cancellationToken))
            throw new ConflictException("Horse already belongs to this booking", "HORSE_ALREADY_IN_BOOKING");

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.BookingHorses.Add(new BookingHorse
        {
            BookingId = bookingId,
            HorseId = request.HorseId,
            SpecialHandlingNotes = EmptyToNull(request.SpecialHandlingNotes),
            CreatedAt = now
        });
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.PENDING,
            BookingStatus.PENDING,
            $"Added horse: {horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateHorseNotesAsync(
        Guid accountId,
        Guid bookingId,
        Guid horseId,
        Request.UpdateHorseNotesRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedPendingBookingAsync(accountId, bookingId, cancellationToken);
        if (request.SpecialHandlingNotes?.Length > 2000)
            throw new BadRequestException("Special handling notes must not exceed 2000 characters");

        var bookingHorse = await _db.BookingHorses
            .Include(item => item.Horse)
            .SingleOrDefaultAsync(item =>
                item.BookingId == bookingId &&
                item.HorseId == horseId,
                cancellationToken);
        if (bookingHorse is null)
            throw new NotFoundException("Horse is not in this booking", "HORSE_NOT_IN_BOOKING");

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        bookingHorse.SpecialHandlingNotes = EmptyToNull(request.SpecialHandlingNotes);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.PENDING,
            BookingStatus.PENDING,
            $"Updated handling notes for horse: {bookingHorse.Horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveHorseAsync(
        Guid accountId,
        Guid bookingId,
        Guid horseId,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedPendingBookingAsync(accountId, bookingId, cancellationToken);
        var bookingHorse = await _db.BookingHorses
            .Include(item => item.Horse)
            .SingleOrDefaultAsync(item =>
                item.BookingId == bookingId &&
                item.HorseId == horseId,
                cancellationToken);
        if (bookingHorse is null)
            throw new NotFoundException("Horse is not in this booking", "HORSE_NOT_IN_BOOKING");

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.BookingHorses.Remove(bookingHorse);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.PENDING,
            BookingStatus.PENDING,
            $"Removed horse: {bookingHorse.Horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Response.CreateBookingResponse> CreateAsync(
        Guid accountId,
        Request.CreateBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateDates(request.RequestedDepartureDate, request.RequestedArrivalDate);
        if (request.Horses.Select(horse => horse.HorseId).Distinct().Count() != request.Horses.Count)
            throw new BadRequestException(
                "A horse can only be selected once per booking",
                "DUPLICATE_HORSE_IN_BOOKING");

        var customer = await GetCustomerAsync(accountId, cancellationToken);
        var horseIds = request.Horses.Select(horse => horse.HorseId).ToArray();
        if (horseIds.Length > 0)
        {
            var ownedHorseCount = await _db.Horses.CountAsync(horse =>
                horseIds.Contains(horse.HorseId) &&
                horse.CustomerId == customer.CustomerId &&
                !horse.IsDeleted,
                cancellationToken);
            if (ownedHorseCount != horseIds.Length)
                throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");
        }

        var now = UtcNow();
        var booking = new BookingEntity
        {
            BookingId = Guid.NewGuid(),
            BookingCode = await GenerateBookingCodeAsync(cancellationToken),
            CustomerId = customer.CustomerId,
            Status = BookingStatus.PENDING,
            OriginAddress = request.OriginAddress.Trim(),
            OriginCountry = request.OriginCountry.Trim(),
            DestinationAddress = request.DestinationAddress.Trim(),
            DestinationCountry = request.DestinationCountry.Trim(),
            RequestedDepartureDate = request.RequestedDepartureDate,
            RequestedArrivalDate = request.RequestedArrivalDate,
            SpecialRequirements = EmptyToNull(request.SpecialRequirements),
            CustomerNote = EmptyToNull(request.CustomerNote),
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Bookings.Add(booking);
        _db.BookingHorses.AddRange(request.Horses.Select(horse => new BookingHorse
        {
            BookingId = booking.BookingId,
            HorseId = horse.HorseId,
            SpecialHandlingNotes = EmptyToNull(horse.SpecialHandlingNotes),
            CreatedAt = now
        }));
        AddHistory(booking.BookingId, accountId, BookingHistoryType.SUBMIT, null, BookingStatus.PENDING, null, now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new Response.CreateBookingResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode
        };
    }

    public async Task<Response.UpdateBookingResponse> UpdateAsync(
        Guid accountId,
        Guid bookingId,
        Request.UpdateBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedPendingBookingAsync(accountId, bookingId, cancellationToken);
        var clientUpdatedAt = DateTime.SpecifyKind(request.LastUpdatedAt, DateTimeKind.Unspecified);
        if (booking.UpdatedAt != clientUpdatedAt)
            throw new ConflictException(
                "The booking was modified. Refresh and try again",
                "BOOKING_MODIFIED");

        var departureDate = request.RequestedDepartureDate ?? booking.RequestedDepartureDate;
        var arrivalDate = request.RequestedArrivalDate ?? booking.RequestedArrivalDate;
        ValidateDates(departureDate, arrivalDate);
        var changedFields = new List<string>();
        Set(request.OriginAddress, value => booking.OriginAddress = value, "originAddress", changedFields);
        Set(request.OriginCountry, value => booking.OriginCountry = value, "originCountry", changedFields);
        Set(request.DestinationAddress, value => booking.DestinationAddress = value, "destinationAddress", changedFields);
        Set(request.DestinationCountry, value => booking.DestinationCountry = value, "destinationCountry", changedFields);
        if (request.RequestedDepartureDate.HasValue)
        {
            booking.RequestedDepartureDate = request.RequestedDepartureDate.Value;
            changedFields.Add("requestedDepartureDate");
        }
        if (request.RequestedArrivalDate.HasValue)
        {
            booking.RequestedArrivalDate = request.RequestedArrivalDate;
            changedFields.Add("requestedArrivalDate");
        }
        SetOptional(request.SpecialRequirements, value => booking.SpecialRequirements = value, "specialRequirements", changedFields);
        SetOptional(request.CustomerNote, value => booking.CustomerNote = value, "customerNote", changedFields);

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.PENDING,
            BookingStatus.PENDING,
            $"Updated: {string.Join(", ", changedFields)}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Response.UpdateBookingResponse { UpdatedAt = AsUtc(now) };
    }

    public async Task CancelAsync(
        Guid accountId,
        Guid bookingId,
        Request.CancelBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedPendingBookingAsync(accountId, bookingId, cancellationToken);
        var now = UtcNow();
        var reason = request.Reason.Trim();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.CANCELLED;
        booking.CancelledAt = now;
        booking.CancellationReason = reason;
        booking.UpdatedAt = now;
        AddHistory(bookingId, accountId, BookingHistoryType.CANCEL, BookingStatus.PENDING, BookingStatus.CANCELLED, reason, now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Response.ApproveBookingResponse> ApproveAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetLogisticsManagerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings
            .Include(item => item.Customer).ThenInclude(customer => customer.Account)
            .Include(item => item.Transport)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        EnsurePending(booking);
        if (booking!.Transport is not null)
            throw new ConflictException("The booking already has a transport", "INVALID_BOOKING_STATUS");
        if (!await _db.BookingHorses.AnyAsync(item =>
                item.BookingId == bookingId && !item.Horse.IsDeleted,
                cancellationToken))
            throw new UnprocessableEntityException(
                "The booking has no horse and cannot be approved",
                "BOOKING_HAS_NO_HORSE");

        var now = UtcNow();
        var transport = new Transport
        {
            TransportId = Guid.NewGuid(),
            TransportCode = await GenerateTransportCodeAsync(cancellationToken),
            BookingId = bookingId,
            Status = TransportStatus.CREATED,
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.APPROVED;
        booking.AssignedEmployeeId = employee.EmployeeId;
        booking.ApprovedAt = now;
        booking.UpdatedAt = now;
        _db.Transports.Add(transport);
        AddHistory(bookingId, accountId, BookingHistoryType.APPROVE, BookingStatus.PENDING, BookingStatus.APPROVED, null, now);
        AddNotification(booking.Customer.AccountId, transport.TransportId, booking, "Booking approved", now);
        await _db.SaveChangesAsync(cancellationToken);
        await _mailService.SendMail(new MailService.MailContent
        {
            To = booking.Customer.Account.Email,
            Subject = "Booking approved",
            Body = $"<p>Booking <strong>{WebUtility.HtmlEncode(booking.BookingCode)}</strong> has been approved.</p>"
        });
        await transaction.CommitAsync(cancellationToken);

        return new Response.ApproveBookingResponse
        {
            Status = booking.Status,
            Transport = new Response.TransportResponse
            {
                TransportId = transport.TransportId,
                TransportCode = transport.TransportCode,
                Status = transport.Status
            }
        };
    }

    public async Task RejectAsync(
        Guid accountId,
        Guid bookingId,
        Request.RejectBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetLogisticsManagerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings
            .Include(item => item.Customer).ThenInclude(customer => customer.Account)
            .Include(item => item.Transport)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        EnsurePending(booking);
        if (booking!.Transport is not null)
            throw new ConflictException("The booking already has a transport", "INVALID_BOOKING_STATUS");

        var now = UtcNow();
        var reason = request.Reason.Trim();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.REJECTED;
        booking.AssignedEmployeeId = employee.EmployeeId;
        booking.RejectedAt = now;
        booking.RejectionReason = reason;
        booking.UpdatedAt = now;
        AddHistory(bookingId, accountId, BookingHistoryType.REJECT, BookingStatus.PENDING, BookingStatus.REJECTED, reason, now);
        AddNotification(booking.Customer.AccountId, null, booking, "Booking rejected", now);
        await _db.SaveChangesAsync(cancellationToken);
        await _mailService.SendMail(new MailService.MailContent
        {
            To = booking.Customer.Account.Email,
            Subject = "Booking rejected",
            Body = $"<p>Booking <strong>{WebUtility.HtmlEncode(booking.BookingCode)}</strong> has been rejected.</p><p>Reason: {WebUtility.HtmlEncode(reason)}</p>"
        });
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Customer> GetCustomerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.SingleOrDefaultAsync(item =>
            item.AccountId == accountId &&
            !item.IsDeleted &&
            !item.Account.IsDeleted &&
            item.Account.Status == AccountStatus.ACTIVE,
            cancellationToken);
        return customer ?? throw new ForbiddenException("You do not have permission to perform this action");
    }

    private async Task<BookingEntity> GetOwnedPendingBookingAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.SingleOrDefaultAsync(item =>
            item.BookingId == bookingId && item.Customer.AccountId == accountId,
            cancellationToken);
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.PENDING)
            throw new ConflictException("The action is not allowed in the current booking status", "INVALID_BOOKING_STATUS");
        return booking;
    }

    private async Task<Employee> GetLogisticsManagerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.SingleOrDefaultAsync(item =>
            item.AccountId == accountId &&
            item.Role == EmployeeRole.LOGISTICS_MANAGER &&
            item.IsActive &&
            !item.IsDeleted &&
            !item.Account.IsDeleted &&
            item.Account.Status == AccountStatus.ACTIVE,
            cancellationToken);
        return employee ?? throw new ForbiddenException("You do not have permission to perform this action");
    }

    private static void EnsurePending(BookingEntity? booking)
    {
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.PENDING)
            throw new ConflictException("The action is not allowed in the current booking status", "INVALID_BOOKING_STATUS");
    }

    private static IQueryable<BookingEntity> ApplySorting(
        IQueryable<BookingEntity> query,
        IReadOnlyCollection<string>? sortBy)
    {
        var sortParameters = sortBy is null || sortBy.Count == 0
            ? ["createdAt:desc"]
            : sortBy.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        IOrderedQueryable<BookingEntity>? orderedQuery = null;
        foreach (var sortParameter in sortParameters)
        {
            var parts = sortParameter.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                (parts[1].ToLowerInvariant() != "asc" && parts[1].ToLowerInvariant() != "desc") ||
                (parts[0].ToLowerInvariant() != "createdat" &&
                 parts[0].ToLowerInvariant() != "requesteddeparturedate"))
                throw new BadRequestException("Sort parameter is invalid", "INVALID_SORT_PARAMETER");

            var ascending = parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
            var field = parts[0].ToLowerInvariant();
            orderedQuery = orderedQuery is null
                ? ApplyPrimarySort(query, field, ascending)
                : ApplySecondarySort(orderedQuery, field, ascending);
        }

        return orderedQuery ?? query.OrderByDescending(booking => booking.CreatedAt);
    }

    private static IOrderedQueryable<BookingEntity> ApplyPrimarySort(
        IQueryable<BookingEntity> query,
        string field,
        bool ascending) => field switch
        {
            "createdat" when ascending => query.OrderBy(booking => booking.CreatedAt),
            "createdat" => query.OrderByDescending(booking => booking.CreatedAt),
            "requesteddeparturedate" when ascending => query.OrderBy(booking => booking.RequestedDepartureDate),
            _ => query.OrderByDescending(booking => booking.RequestedDepartureDate)
        };

    private static IOrderedQueryable<BookingEntity> ApplySecondarySort(
        IOrderedQueryable<BookingEntity> query,
        string field,
        bool ascending) => field switch
        {
            "createdat" when ascending => query.ThenBy(booking => booking.CreatedAt),
            "createdat" => query.ThenByDescending(booking => booking.CreatedAt),
            "requesteddeparturedate" when ascending => query.ThenBy(booking => booking.RequestedDepartureDate),
            _ => query.ThenByDescending(booking => booking.RequestedDepartureDate)
        };

    private static string GetStatusName(BookingStatus status) => status switch
    {
        BookingStatus.PENDING => "Pending",
        BookingStatus.APPROVED => "Approved",
        BookingStatus.REJECTED => "Rejected",
        BookingStatus.CANCELLED => "Cancelled",
        _ => status.ToString()
    };

    private static string GetHistoryAction(BookingStatusHistory history) => history.Type switch
    {
        BookingHistoryType.APPROVE => "APPROVED",
        BookingHistoryType.REJECT => "REJECTED",
        BookingHistoryType.CANCEL => "CANCELLED",
        BookingHistoryType.SUBMIT when history.PreviousStatus is null => "CREATED",
        BookingHistoryType.SUBMIT => "UPDATED",
        _ => history.Type.ToString()
    };

    private static string GetAccountFullName(Account account) =>
        account.CustomerAccount?.FullName ?? account.EmployeeAccount?.FullName ?? account.Email;

    private static string GetAccountRole(Account account) => account.AccountType == AccountType.CUSTOMER
        ? "CUSTOMER"
        : account.EmployeeAccount?.Role.ToString() ?? "EMPLOYEE";

    private void AddHistory(
        Guid bookingId,
        Guid accountId,
        BookingHistoryType type,
        BookingStatus? previousStatus,
        BookingStatus newStatus,
        string? reason,
        DateTime changedAt) => _db.BookingStatusHistories.Add(new BookingStatusHistory
    {
        BookingStatusHistoryId = Guid.NewGuid(),
        BookingId = bookingId,
        Type = type,
        PreviousStatus = previousStatus,
        NewStatus = newStatus,
        ChangedByAccountId = accountId,
        Reason = reason,
        ChangedAt = changedAt
    });

    private void AddNotification(Guid accountId, Guid? transportId, BookingEntity booking, string title, DateTime now)
    {
        var notificationId = Guid.NewGuid();
        _db.Notifications.Add(new Notification
        {
            NotificationId = notificationId,
            TransportId = transportId,
            NotificationType = NotificationType.BOOKING,
            Priority = NotificationPriority.NORMAL,
            Title = title,
            Message = $"Booking {booking.BookingCode}: {title.ToLowerInvariant()}.",
            ReferenceType = "Booking",
            ReferenceId = booking.BookingId,
            ActionUrl = $"/bookings/{booking.BookingId}",
            CreatedAt = now
        });
        _db.AccountNotifications.Add(new AccountNotification
        {
            AccountId = accountId,
            NotificationId = notificationId,
            Status = RecipientStatus.PENDING,
            CreatedAt = now
        });
    }

    private async Task<string> GenerateBookingCodeAsync(CancellationToken cancellationToken) =>
        await GenerateCodeAsync("REQ", code => _db.Bookings.AnyAsync(booking => booking.BookingCode == code, cancellationToken));

    private async Task<string> GenerateTransportCodeAsync(CancellationToken cancellationToken) =>
        await GenerateCodeAsync("TP", code => _db.Transports.AnyAsync(transport => transport.TransportCode == code, cancellationToken));

    private static async Task<string> GenerateCodeAsync(string prefix, Func<string, Task<bool>> exists)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = $"{prefix}-{date}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (!await exists(code)) return code;
        }
        throw new InvalidOperationException($"Unable to generate a unique {prefix} code");
    }

    private static void ValidateDates(DateOnly departureDate, DateOnly? arrivalDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (departureDate < today.AddDays(21) || departureDate > today.AddMonths(2))
            throw new BadRequestException(
                "The departure date must be between 3 weeks and 2 months from today",
                "DEPARTURE_DATE_OUT_OF_RANGE");
        if (arrivalDate.HasValue && arrivalDate.Value < departureDate)
            throw new BadRequestException(
                "The arrival date cannot be before the departure date",
                "ARRIVAL_DATE_INVALID");
    }

    private static void Set(string? value, Action<string> setter, string field, ICollection<string> changedFields)
    {
        if (value is null) return;
        setter(value.Trim());
        changedFields.Add(field);
    }

    private static void SetOptional(string? value, Action<string?> setter, string field, ICollection<string> changedFields)
    {
        if (value is null) return;
        setter(EmptyToNull(value));
        changedFields.Add(field);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? AsUtcNullable(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
