using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.BookingService;

public partial class Service
{
    public async Task<IReadOnlyList<Response.BookingAssignmentResponse>> GetAssignmentsAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEmployeeAsync(accountId, cancellationToken);
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(item => item.Transport)
                .ThenInclude(item => item!.TransportPlans)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        var transportPlan = booking.Transport?.TransportPlans
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();
        if (transportPlan is null)
            return [];

        return await _db.EmployeeTransportPlans
            .AsNoTracking()
            .Where(item => item.TransportPlanId == transportPlan.TransportPlanId)
            .Include(item => item.Employee)
            .OrderByDescending(item => item.IsPrimary)
            .ThenByDescending(item => item.AssignedAt)
            .Select(item => new Response.BookingAssignmentResponse
            {
                BookingId = bookingId,
                TransportPlanId = item.TransportPlanId,
                EmployeeId = item.EmployeeId,
                EmployeeCode = item.Employee.EmployeeCode,
                EmployeeName = item.Employee.FullName,
                Role = item.Employee.Role.ToString(),
                AssignedAt = AsUtc(item.AssignedAt),
                IsPrimary = item.IsPrimary,
                Responsibility = item.Responsibility,
                Note = item.Note
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Response.BookingAssignmentResponse> AssignEmployeeAsync(
        Guid accountId,
        Guid bookingId,
        Request.AssignBookingEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        await GetLogisticsManagerAsync(accountId, cancellationToken);
        if (!Enum.TryParse<EmployeeRole>(request.Role.Trim(), true, out var role) ||
            role is not (EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new BadRequestException("Role must be TRANSPORT_SPECIALIST or FLEET_ROUTE_COORDINATOR", "INVALID_ASSIGNMENT_ROLE");

        var booking = await _db.Bookings
            .Include(item => item.Transport)
                .ThenInclude(item => item!.TransportPlans)
                    .ThenInclude(item => item.EmployeeTransportPlans)
                        .ThenInclude(item => item.Employee)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("Only approved bookings can receive employee assignments", "INVALID_BOOKING_STATUS");

        var transportPlan = booking.Transport?.TransportPlans
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();
        if (transportPlan is null)
            throw new ConflictException("A TransportPlan must exist before assigning employees", "TRANSPORT_PLAN_NOT_FOUND");
        if (transportPlan.Status != TransportPlanStatus.APPROVED)
            throw new ConflictException("Only an approved TransportPlan can receive employee assignments", "INVALID_TRANSPORT_PLAN_STATUS");

        var employee = await _db.Employees.SingleOrDefaultAsync(item =>
            item.EmployeeId == request.EmployeeId && !item.IsDeleted && item.IsActive &&
            item.Account.Status == AccountStatus.ACTIVE, cancellationToken);
        if (employee is null)
            throw new NotFoundException("Employee not found or inactive", "EMPLOYEE_NOT_FOUND");
        if (employee.Role != role)
            throw new BadRequestException("Employee role does not match assignment role", "ASSIGNMENT_ROLE_MISMATCH");

        var current = transportPlan.EmployeeTransportPlans
            .SingleOrDefault(item => item.Employee.Role == role);
        var now = UtcNowTruncated();
        if (current is not null)
        {
            if (current.EmployeeId == employee.EmployeeId)
            {
                current.Note = string.IsNullOrWhiteSpace(request.Note) ? current.Note : request.Note.Trim();
                current.Responsibility = role.ToString();
                await _db.SaveChangesAsync(cancellationToken);
                return MapAssignment(bookingId, current);
            }

            _db.EmployeeTransportPlans.Remove(current);
        }

        var assignment = new EmployeeTransportPlan
        {
            TransportPlanId = transportPlan.TransportPlanId,
            EmployeeId = employee.EmployeeId,
            AssignedAt = now,
            IsPrimary = true,
            Responsibility = role.ToString(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };
        _db.EmployeeTransportPlans.Add(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        assignment.Employee = employee;
        return MapAssignment(bookingId, assignment);
    }

    public async Task<Response.BookingAssignmentResponse> RevokeAssignmentAsync(
        Guid accountId,
        Guid bookingId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        await GetLogisticsManagerAsync(accountId, cancellationToken);
        var assignment = await _db.EmployeeTransportPlans
            .Include(item => item.Employee)
            .Include(item => item.TransportPlan)
                .ThenInclude(item => item.Transport)
            .SingleOrDefaultAsync(item => item.EmployeeId == employeeId &&
                item.TransportPlan.Transport.BookingId == bookingId,
                cancellationToken);
        if (assignment is null)
            throw new NotFoundException("Booking assignment not found", "ASSIGNMENT_NOT_FOUND");

        var response = MapAssignment(bookingId, assignment);
        _db.EmployeeTransportPlans.Remove(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        return response;
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

    private static Response.BookingAssignmentResponse MapAssignment(Guid bookingId, EmployeeTransportPlan item) =>
        new()
        {
            BookingId = bookingId,
            TransportPlanId = item.TransportPlanId,
            EmployeeId = item.EmployeeId,
            EmployeeCode = item.Employee.EmployeeCode,
            EmployeeName = item.Employee.FullName,
            Role = item.Employee.Role.ToString(),
            AssignedAt = AsUtc(item.AssignedAt),
            IsPrimary = item.IsPrimary,
            Responsibility = item.Responsibility,
            Note = item.Note
        };
}
