using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.BookingService;

public partial class Service
{
    public async Task<IReadOnlyList<Response.BookingAssignmentResponse>> GetAssignmentsAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEmployeeAsync(accountId, cancellationToken);
        if (!await _db.Bookings.AnyAsync(item => item.BookingId == bookingId, cancellationToken))
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        return await _db.BookingEmployeeAssignments
            .AsNoTracking()
            .Where(item => item.BookingId == bookingId)
            .OrderByDescending(item => item.Status == BookingAssignmentStatus.ACTIVE)
            .ThenByDescending(item => item.AssignedAt)
            .Select(MapAssignment())
            .ToListAsync(cancellationToken);
    }

    public async Task<Response.BookingAssignmentResponse> AssignEmployeeAsync(
        Guid accountId,
        Guid bookingId,
        Request.AssignBookingEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        var manager = await GetLogisticsManagerAsync(accountId, cancellationToken);
        if (!Enum.TryParse<EmployeeRole>(request.Role.Trim(), true, out var role) ||
            role is not (EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new BadRequestException("Role must be TRANSPORT_SPECIALIST or FLEET_ROUTE_COORDINATOR", "INVALID_ASSIGNMENT_ROLE");

        var booking = await _db.Bookings.SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("Only approved bookings can receive employee assignments", "INVALID_BOOKING_STATUS");

        var employee = await _db.Employees.SingleOrDefaultAsync(item =>
            item.EmployeeId == request.EmployeeId && !item.IsDeleted && item.IsActive &&
            item.Account.Status == AccountStatus.ACTIVE, cancellationToken);
        if (employee is null)
            throw new NotFoundException("Employee not found or inactive", "EMPLOYEE_NOT_FOUND");
        if (employee.Role != role)
            throw new BadRequestException("Employee role does not match assignment role", "ASSIGNMENT_ROLE_MISMATCH");

        var current = await _db.BookingEmployeeAssignments.SingleOrDefaultAsync(item =>
            item.BookingId == bookingId && item.AssignmentRole == role && item.Status == BookingAssignmentStatus.ACTIVE,
            cancellationToken);
        var now = UtcNow();
        if (current is not null)
        {
            if (current.EmployeeId == employee.EmployeeId)
            {
                current.Note = string.IsNullOrWhiteSpace(request.Note) ? current.Note : request.Note.Trim();
                await _db.SaveChangesAsync(cancellationToken);
                return await LoadAssignmentAsync(current.BookingEmployeeAssignmentId, cancellationToken);
            }

            current.Status = BookingAssignmentStatus.REVOKED;
            current.RevokedAt = now;
        }

        var assignment = new BookingEmployeeAssignment
        {
            BookingEmployeeAssignmentId = Guid.NewGuid(),
            BookingId = bookingId,
            EmployeeId = employee.EmployeeId,
            AssignmentRole = role,
            AssignedByEmployeeId = manager.EmployeeId,
            Status = BookingAssignmentStatus.ACTIVE,
            AssignedAt = now,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };
        _db.BookingEmployeeAssignments.Add(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        return await LoadAssignmentAsync(assignment.BookingEmployeeAssignmentId, cancellationToken);
    }

    public async Task<Response.BookingAssignmentResponse> RevokeAssignmentAsync(
        Guid accountId,
        Guid bookingId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        await GetLogisticsManagerAsync(accountId, cancellationToken);
        var assignment = await _db.BookingEmployeeAssignments.SingleOrDefaultAsync(item =>
            item.BookingEmployeeAssignmentId == assignmentId && item.BookingId == bookingId,
            cancellationToken);
        if (assignment is null)
            throw new NotFoundException("Booking assignment not found", "ASSIGNMENT_NOT_FOUND");
        if (assignment.Status == BookingAssignmentStatus.REVOKED)
            return await LoadAssignmentAsync(assignmentId, cancellationToken);

        assignment.Status = BookingAssignmentStatus.REVOKED;
        assignment.RevokedAt = UtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return await LoadAssignmentAsync(assignmentId, cancellationToken);
    }

    private async Task<Employee> EnsureEmployeeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.SingleOrDefaultAsync(item =>
            item.AccountId == accountId && !item.IsDeleted && item.IsActive && item.Account.Status == AccountStatus.ACTIVE,
            cancellationToken) ?? throw new ForbiddenException("Employee access required");
        if (employee.Role is not (EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new ForbiddenException("Only Logistics Managers, Transport Specialists and Fleet Route Coordinators can view booking assignments");
        return employee;
    }

    private async Task<Response.BookingAssignmentResponse> LoadAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        await _db.BookingEmployeeAssignments.AsNoTracking()
            .Where(item => item.BookingEmployeeAssignmentId == assignmentId)
            .Select(MapAssignment())
            .SingleAsync(cancellationToken);

    private static Expression<Func<BookingEmployeeAssignment, Response.BookingAssignmentResponse>> MapAssignment() => item =>
        new Response.BookingAssignmentResponse
        {
            AssignmentId = item.BookingEmployeeAssignmentId,
            BookingId = item.BookingId,
            EmployeeId = item.EmployeeId,
            EmployeeName = item.Employee.FullName,
            Role = item.AssignmentRole.ToString(),
            Status = item.Status.ToString(),
            AssignedByEmployeeId = item.AssignedByEmployeeId,
            AssignedAt = new DateTimeOffset(DateTime.SpecifyKind(item.AssignedAt, DateTimeKind.Utc)),
            RevokedAt = item.RevokedAt == null ? null : new DateTimeOffset(DateTime.SpecifyKind(item.RevokedAt.Value, DateTimeKind.Utc)),
            Note = item.Note
        };
}
