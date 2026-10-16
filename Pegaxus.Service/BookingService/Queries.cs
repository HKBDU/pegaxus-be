using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.BookingService;

public sealed partial class Service
{
    public async Task<Response.BookingDetailResponse> GetAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employeeRole = await _db.Employees
            .Active()
            .Where(employee => employee.AccountId == accountId)
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
                (canViewAll && item.Status != BookingStatus.DRAFT ||
                 !canViewAll && item.Customer.AccountId == accountId))
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
                CustomerCode = booking.Customer.CustomerCode,
                FullName = booking.Customer.FullName,
                Phone = booking.Customer.Phone
            },
            OriginAddress = booking.OriginAddress,
            OriginCountry = booking.OriginCountry,
            DestinationAddress = booking.DestinationAddress,
            DestinationCountry = booking.DestinationCountry,
            OriginLatitude = booking.OriginLatitude,
            OriginLongitude = booking.OriginLongitude,
            DestinationLatitude = booking.DestinationLatitude,
            DestinationLongitude = booking.DestinationLongitude,
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
            ApprovedAt = AsUtc(booking.ApprovedAt),
            RejectedAt = AsUtc(booking.RejectedAt),
            CancelledAt = AsUtc(booking.CancelledAt),
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
            .Active()
            .Where(employee => employee.AccountId == accountId)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);

        var canViewAll = employeeRole == EmployeeRole.LOGISTICS_MANAGER;
        var bookingExists = await _db.Bookings.AnyAsync(booking =>
            booking.BookingId == bookingId &&
            (canViewAll && booking.Status != BookingStatus.DRAFT ||
             !canViewAll && booking.Customer.AccountId == accountId),
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
            .Active()
            .Where(employee => employee.AccountId == accountId)
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
            .Where(booking =>
                (canViewAll && booking.Status != BookingStatus.DRAFT) ||
                (!canViewAll && booking.CustomerId == customerId!.Value));

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
            TotalPages = Paging.TotalPages(totalItems, request.PageSize)
        };
    }
}
