using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;
using DossierService = Pegaxus.Service.ComplianceDossierService.Service;
using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.Test;

public class DossierRequirementTests
{
    [Fact]
    public async Task AddUpdateRemove_UpdatesProgress_NotificationAndAudit()
    {
        await using var db = TestSupport.CreateDb();
        var (booking, dossier, specialist, requirement) = await SeedAsync(db);
        var realtime = new RecordingRealtimeService();
        var service = CreateService(db, realtime);
        var added = await service.AddRequirementAsync(specialist, dossier.ComplianceDossierId, new DossierRequest.RequirementRequest
        {
            ComplianceRequirementId = requirement.ComplianceRequirementId, LeadTimeDays = 5,
            IsMandatory = false, Instruction = "Upload Modello 4", Reason = "Italian border crossing"
        });
        Assert.Equal(booking.RequestedDepartureDate.AddDays(-5), added.DueDate);
        Assert.Single(await db.AccountNotifications.ToListAsync());
        Assert.Equal(booking.Customer.AccountId, Assert.Single(realtime.Notifications).AccountId);
        var updated = await service.UpdateRequirementAsync(specialist, dossier.ComplianceDossierId,
            added.DossierRequirementId, new DossierRequest.UpdateRequirementRequest
            { LeadTimeDays = 7, Instruction = "Upload revised Modello 4", Reason = "Earlier checkpoint" });
        Assert.Equal(booking.RequestedDepartureDate.AddDays(-7), updated.DueDate);
        Assert.False(updated.IsMandatory);
        Assert.Equal(2, realtime.Notifications.Count);

        var removal = await service.RemoveRequirementAsync(specialist, dossier.ComplianceDossierId, added.DossierRequirementId,
            new DossierRequest.RemoveRequirementRequest { Reason = "Route changed" });
        Assert.Equal(1, removal.RequirementCount);
        Assert.Equal(100m, removal.CompletionPercent);
        Assert.Equal(3, await db.AuditDossiers.CountAsync());
        Assert.Equal(3, realtime.Notifications.Count);
        Assert.All(realtime.Notifications, push => Assert.Equal(booking.Customer.AccountId, push.AccountId));
        Assert.Equal((await db.Notifications.Select(item => item.NotificationId).ToListAsync()).OrderBy(id => id),
            realtime.Notifications.Select(push => push.Notification.Id).OrderBy(id => id));
        foreach (var action in new[] { "ADDED", "UPDATED", "REMOVED" })
        {
            var history = await service.GetAuditHistoryAsync(specialist, dossier.ComplianceDossierId,
                new DossierRequest.AuditHistoryRequest { ActionType = $"DOSSIER_REQUIREMENT_{action}" });
            Assert.Equal(added.DossierRequirementId, Assert.Single(history.Items).TargetEntityId);
        }
        Assert.True((await db.DossierRequirements.SingleAsync(item => item.DossierRequirementId == added.DossierRequirementId)).IsDeleted);

        var replacement = await service.AddRequirementAsync(specialist, dossier.ComplianceDossierId, new DossierRequest.RequirementRequest
        {
            ComplianceRequirementId = requirement.ComplianceRequirementId, IsMandatory = false, Reason = "Route restored"
        });
        Assert.NotEqual(added.DossierRequirementId, replacement.DossierRequirementId);
    }

    [Fact]
    public async Task SecurityAndSubmissionState_BlockMutations()
    {
        await using var db = TestSupport.CreateDb();
        var (booking, dossier, specialist, requirement) = await SeedAsync(db);
        var service = CreateService(db);
        var core = await db.DossierRequirements.SingleAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.RemoveRequirementAsync(specialist,
            dossier.ComplianceDossierId, core.DossierRequirementId,
            new DossierRequest.RemoveRequirementRequest { Reason = "Try remove" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AddRequirementAsync(booking.Customer.AccountId,
            dossier.ComplianceDossierId, new DossierRequest.RequirementRequest
            { ComplianceRequirementId = requirement.ComplianceRequirementId, Reason = "Unauthorized" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.RemoveRequirementAsync(booking.Customer.AccountId,
            dossier.ComplianceDossierId, core.DossierRequirementId,
            new DossierRequest.RemoveRequirementRequest { Reason = "Unauthorized" }));
        dossier.Status = ComplianceDossierStatus.SUBMITTED;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.AddRequirementAsync(specialist,
            dossier.ComplianceDossierId, new DossierRequest.RequirementRequest
            { ComplianceRequirementId = requirement.ComplianceRequirementId, Reason = "Too late" }));
        Assert.Single(await db.DossierRequirements.ToListAsync());
    }

    private static async Task<(Booking Booking, Pegaxus.Repository.Entities.ComplianceDossier Dossier,
        Guid Specialist, ComplianceRequirement Requirement)> SeedAsync(AppDbContext db)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (employeeAccount, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        booking.Status = BookingStatus.APPROVED;
        var coreRequirement = new ComplianceRequirement
        {
            ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "EHC", RequirementName = "Export Health Certificate"
        };
        var requirement = new ComplianceRequirement
        {
            ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "MOD4", RequirementName = "Modello 4"
        };
        db.ComplianceRequirements.AddRange(coreRequirement, requirement);
        var dossier = new Pegaxus.Repository.Entities.ComplianceDossier
        {
            ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-TEST", Status = ComplianceDossierStatus.IN_PREPARATION,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TP-TEST", Booking = booking },
            DossierRequirements = [new DossierRequirement
            {
                DossierRequirementId = Guid.NewGuid(), ComplianceRequirement = coreRequirement,
                IsMandatory = true, Status = DossierRequirementStatus.SATISFIED
            }]
        };
        db.ComplianceDossiers.Add(dossier);
        await db.SaveChangesAsync();
        return (booking, dossier, employeeAccount.AccountId, requirement);
    }

    private static DossierService CreateService(AppDbContext db, NotificationService.INotificationRealtimeService? realtime = null) =>
        new(db, null!, new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(),
            new NotificationService.Service(db, realtime ?? new NotificationService.NoopNotificationRealtimeService()));

    private sealed class RecordingRealtimeService : NotificationService.INotificationRealtimeService
    {
        public List<(Guid AccountId, NotificationService.NotificationRealtimeDto Notification)> Notifications { get; } = [];

        public Task SendToAccountAsync(Guid accountId, NotificationService.NotificationRealtimeDto notification,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add((accountId, notification));
            return Task.CompletedTask;
        }
    }
}
