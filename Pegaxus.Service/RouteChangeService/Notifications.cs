using System.Data;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RouteChangeService;

public sealed partial class Service
{
    public async Task<Response.DriverNoticeResult> NotifyDriversAsync(Guid routePlanId, Guid accountId, Request.DriverNoticeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Message?.Length > 4000) throw new BadRequestException("Message must not exceed 4000 characters.");
        var actor = await ActorAsync(accountId, [EmployeeRole.FLEET_ROUTE_COORDINATOR], cancellationToken);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var plan = await LockPlanAsync(routePlanId, cancellationToken);
        await EnsureCoordinatorAsync(plan, actor, cancellationToken);
        var segments = await SegmentsAsync(routePlanId, cancellationToken);
        if (plan.Status is not (RoutePlanStatus.CONFIRMED or RoutePlanStatus.ACTIVE)) throw InvalidStatus(plan);
        EnsureTypeStatus(plan, segments, "RETURN", plan.Status);
        var primary = await PrimaryAsync(plan, cancellationToken);
        var now = Now;
        var drivers = await NotifyPrimaryDriversAsync(plan, primary.RoutePlanId, "Begin return journey", request.Message, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        return new(routePlanId, drivers.Select(x => Employee(x)!).ToArray(), AsUtc(now));
    }

    private async Task NotifyAssignedAsync(RoutePlan plan, EmployeeRole role, string title, string? message,
        NotificationPriority priority, DateTime now, CancellationToken token)
    {
        var recipients = await db.EmployeeTransportPlans.AsNoTracking().Where(x => x.TransportPlanId == plan.TransportPlanId &&
            x.Employee.Role == role && x.Employee.IsActive && !x.Employee.IsDeleted &&
            !x.Employee.Account.IsDeleted && x.Employee.Account.Status == AccountStatus.ACTIVE)
            .Select(x => x.Employee.AccountId).Distinct().ToArrayAsync(token);
        foreach (var recipient in recipients) Notify(recipient, plan, title, message, priority, now);
    }

    private async Task<List<Employee>> NotifyPrimaryDriversAsync(RoutePlan plan, Guid primaryId, string title, string? message, DateTime now, CancellationToken token)
    {
        var driverIds = db.RoutePlanRoutes.Where(x => x.RoutePlanId == primaryId && x.DriverEmployeeId.HasValue).Select(x => x.DriverEmployeeId!.Value);
        var drivers = await db.Employees.AsNoTracking().Active().Where(x => driverIds.Contains(x.EmployeeId) && x.Role == EmployeeRole.DRIVER).OrderBy(x => x.EmployeeCode).ToListAsync(token);
        foreach (var driver in drivers) Notify(driver.AccountId, plan, title, message, NotificationPriority.URGENT, now);
        return drivers;
    }

    private void Notify(Guid accountId, RoutePlan plan, string title, string? message, NotificationPriority priority, DateTime now) =>
        notifications.Queue(new() { AccountId = accountId, TransportId = plan.TransportPlan.TransportId, Type = NotificationType.ROUTE,
            Priority = priority, Title = title, Message = $"{plan.RoutePlanCode}: {message}", ReferenceType = "RoutePlan",
            ReferenceId = plan.RoutePlanId, ActionUrl = $"/admin/route-plans/{plan.RoutePlanId}", CreatedAt = now });

    private void Audit(RoutePlan plan, Guid accountId, string action, string? note, DateTime now) =>
        db.CatalogAuditLogs.Add(new() { CatalogAuditLogId = Guid.NewGuid(), EntityType = "RoutePlan", EntityId = plan.RoutePlanId,
            ActorAccountId = accountId, ActionType = action, Summary = note, BeforeData = JsonDocument.Parse("{}"),
            AfterData = JsonSerializer.SerializeToDocument(new { status = plan.Status.ToString() }), CreatedAt = now });
}
