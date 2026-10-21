using System.Data;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RouteChangeService;

public sealed partial class Service
{
    public async Task<Response.DriverNoticeResult> NotifyDriversAsync(Guid routePlanId, Guid accountId, Request.DriverNoticeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Message?.Length > 4000) throw new BadRequestException("Message must not exceed 4000 characters.");
        var actor = await ActorAsync(accountId, [EmployeeRole.FLEET_ROUTE_COORDINATOR, EmployeeRole.LOGISTICS_MANAGER], cancellationToken);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var plan = await LockPlanAsync(routePlanId, cancellationToken);
        // A manager covers for an absent coordinator, so only a coordinator must be assigned to the plan.
        if (actor.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR) await EnsureCoordinatorAsync(plan, actor, cancellationToken);
        var segments = await SegmentsAsync(routePlanId, cancellationToken);
        if (plan.Status is not (RoutePlanStatus.CONFIRMED or RoutePlanStatus.ACTIVE)) throw InvalidStatus(plan);
        EnsureTypeStatus(plan, segments, "RETURN", plan.Status);
        var primary = await PrimaryAsync(plan, cancellationToken);
        var now = Now;
        var drivers = await NotifyPrimaryDriversAsync(plan, primary.RoutePlanId, "Begin return journey",
            $"Start the return journey on route plan {plan.RoutePlanCode}.{NoteSuffix(request.Message)}", now, cancellationToken);
        await MailChangedAsync(plan, drivers.Select(item => item.AccountId), MailService.EmailBodies.RouteChangeKind.DriverBeginReturn, request.Message, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        return new(routePlanId, drivers.Select(x => Employee(x)!).ToArray(), AsUtc(now));
    }

    private async Task<Guid[]> NotifyAssignedAsync(RoutePlan plan, EmployeeRole role, string title, string message,
        NotificationPriority priority, DateTime now, CancellationToken token)
    {
        var recipients = await db.EmployeeTransportPlans.AsNoTracking().Where(x => x.TransportPlanId == plan.TransportPlanId &&
            x.Employee.Role == role && x.Employee.IsActive && !x.Employee.IsDeleted &&
            !x.Employee.Account.IsDeleted && x.Employee.Account.Status == AccountStatus.ACTIVE)
            .Select(x => x.Employee.AccountId).Distinct().ToArrayAsync(token);
        foreach (var recipient in recipients) Notify(recipient, plan, title, message, priority, now, NotificationLinks.RoutePlan(role, plan.RoutePlanId));
        return recipients;
    }

    private Task MailReviewAsync(RoutePlan plan, IEnumerable<Guid> accountIds, MailService.EmailBodies.RouteReviewKind kind, string? detail,
        DateTime now, CancellationToken token)
    {
        var view = kind is MailService.EmailBodies.RouteReviewKind.EmergencyApproval or MailService.EmailBodies.RouteReviewKind.ReturnApproval
            ? "change-decision" : null;
        return MailService.MailRecipients.QueueAsync(db, accountIds, now, recipient => MailService.MailRecipients.Mail(recipient,
            $"{MailService.EmailBodies.RouteReviewTitle(kind)} — {plan.RoutePlanCode}",
            MailService.EmailBodies.RouteReviewBody(recipient.FullName, plan.RoutePlanCode, kind, detail,
                NotificationLinks.RoutePlan(recipient.Role, plan.RoutePlanId, view))), token);
    }

    /// <summary>Customers get a link to their booking; drivers have no page for a route plan, so their mail has no button.</summary>
    private Task MailChangedAsync(RoutePlan plan, IEnumerable<Guid> accountIds, MailService.EmailBodies.RouteChangeKind kind, string? note,
        DateTime now, CancellationToken token) =>
        MailService.MailRecipients.QueueAsync(db, accountIds, now, recipient => MailService.MailRecipients.Mail(recipient,
            $"{MailService.EmailBodies.RouteChangedTitle(kind)} — {plan.RoutePlanCode}",
            MailService.EmailBodies.RouteChangedBody(recipient.FullName, plan.RoutePlanCode, kind, note,
                recipient.Role is null ? NotificationLinks.Booking(null, plan.TransportPlan.Transport.BookingId) : null)), token);

    private async Task<List<Employee>> NotifyPrimaryDriversAsync(RoutePlan plan, Guid primaryId, string title, string message, DateTime now, CancellationToken token)
    {
        var driverIds = db.RoutePlanRoutes.Where(x => x.RoutePlanId == primaryId && x.DriverEmployeeId.HasValue).Select(x => x.DriverEmployeeId!.Value);
        var drivers = await db.Employees.AsNoTracking().Active().Where(x => driverIds.Contains(x.EmployeeId) && x.Role == EmployeeRole.DRIVER).OrderBy(x => x.EmployeeCode).ToListAsync(token);
        foreach (var driver in drivers) Notify(driver.AccountId, plan, title, message, NotificationPriority.URGENT, now, null);
        return drivers;
    }

    private async Task NotifyPrimaryEscortsAsync(RoutePlan plan, Guid primaryId, string title, string message, DateTime now, CancellationToken token)
    {
        var escortIds = db.RoutePlanRoutes.Where(x => x.RoutePlanId == primaryId &&
            x.ActualArrivalAt == null && x.EscortEmployeeId.HasValue)
            .OrderBy(x => x.SequenceNo).Take(1).Select(x => x.EscortEmployeeId!.Value);
        var escorts = await db.Employees.AsNoTracking().Active().Where(x => escortIds.Contains(x.EmployeeId) && x.Role == EmployeeRole.ESCORT).ToListAsync(token);
        foreach (var escort in escorts) Notify(escort.AccountId, plan, title, message, NotificationPriority.URGENT, now, null);
    }

    private void Notify(Guid accountId, RoutePlan plan, string title, string message, NotificationPriority priority, DateTime now, string? actionUrl) =>
        notifications.Queue(new() { AccountId = accountId, TransportId = plan.TransportPlan.TransportId, Type = NotificationType.ROUTE,
            Priority = priority, Title = title, Message = message, ReferenceType = "RoutePlan",
            ReferenceId = plan.RoutePlanId, ActionUrl = actionUrl, CreatedAt = now });

    private static string NoteSuffix(string? note) => string.IsNullOrWhiteSpace(note) ? "" : $" Note: {note.Trim()}";

    private void Audit(RoutePlan plan, Guid accountId, string action, string? note, DateTime now) =>
        db.CatalogAuditLogs.Add(new() { CatalogAuditLogId = Guid.NewGuid(), EntityType = "RoutePlan", EntityId = plan.RoutePlanId,
            ActorAccountId = accountId, ActionType = action, Summary = note, BeforeData = JsonDocument.Parse("{}"),
            AfterData = JsonSerializer.SerializeToDocument(new { status = plan.Status.ToString() }), CreatedAt = now });
}
