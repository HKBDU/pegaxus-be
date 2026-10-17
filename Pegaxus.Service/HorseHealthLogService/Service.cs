using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.HorseHealthLogService;

public sealed partial class Service(AppDbContext db, TimeProvider clock, IValidator<Request.RecordRequest> recordValidator,
    IValidator<Request.CreateRequest> createValidator, IMediaService media, IDocumentStorageService documentStorage,
    INotificationService notifications, ILogger<Service> logger) : IHorseHealthLogService
{
    private const string ForbiddenMessage = "Only the assigned escort can record horse health logs";
    private static readonly RoutePlanStatus[] AssignmentStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];

    public async Task<Response.CreateResult> CreateAsync(Guid transportId, Request.CreateRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        var result = await RecordAsync(transportId, request, request, accountId, cancellationToken);
        var log = result.Logs.Single();
        return new(log with { Effects = new(result.EmergencyNotified, result.CreatedCount > 0 && log.HealthStatus == "CRITICAL") }, result.CreatedCount > 0);
    }

    public async Task<Response.BatchResult> MarkAllNormalAsync(Guid transportId, Request.RecordRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        var result = await RecordAsync(transportId, request, null, accountId, cancellationToken);
        return new(new(transportId, request.LogType, result.CreatedCount, result.Logs.Select(log => new Response.Summary(log.HorseHealthLogId,
            new(log.Horse.HorseId, log.Horse.HorseName, log.Horse.MicrochipNumber), log.CheckedAt, log.LogType, log.HealthStatus,
            log.HydrationStatus, log.CheckedBy, log.VeterinarianRequired, log.HasPhoto)).ToArray()), result.CreatedCount > 0);
    }

    private async Task<Guid> GetEscortAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().Where(item => item.AccountId == accountId && !item.IsDeleted && item.Status == AccountStatus.ACTIVE)
            .Select(item => new
            {
                item.AccountType,
                Employee = db.Employees.Active().Where(employee => employee.AccountId == item.AccountId)
                    .Select(employee => new { employee.EmployeeId, employee.Role }).FirstOrDefault()
            }).SingleOrDefaultAsync(cancellationToken);
        if (account is null) throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
        if (account.AccountType != AccountType.EMPLOYEE) throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        if (account.Employee is not { } employee) throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
        if (employee.Role != EmployeeRole.ESCORT) throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        return employee.EmployeeId;
    }

    private async Task<Guid> GetTransportBookingAsync(Guid transportId, Guid escortId, CancellationToken cancellationToken)
    {
        var transport = await db.Transports.AsNoTracking().Where(item => item.TransportId == transportId)
            .Select(item => new { item.BookingId, item.Status }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");
        if (!await db.RoutePlanRoutes.AsNoTracking().AnyAsync(segment => segment.RoutePlan.TransportPlan.TransportId == transportId &&
                segment.EscortEmployeeId == escortId && !segment.TransportProviderId.HasValue && AssignmentStatuses.Contains(segment.RoutePlan.Status) &&
                (segment.SupersededAt == null || segment.ActualDepartureAt != null), cancellationToken))
            throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        if (transport.Status is not (TransportStatus.READY or TransportStatus.IN_TRANSIT))
            throw new ConflictException("Horse health logs can only be recorded while the transport is READY or IN_TRANSIT", "INVALID_TRANSPORT_STATUS");
        return transport.BookingId;
    }

    private sealed record WriteResult(IReadOnlyList<Response.HealthLog> Logs, int CreatedCount, bool EmergencyNotified);
}
