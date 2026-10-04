using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Controllers;
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
        var denied = await Assert.ThrowsAsync<ForbiddenException>(() => service.AddRequirementAsync(booking.Customer.AccountId,
            dossier.ComplianceDossierId, new DossierRequest.RequirementRequest
            { ComplianceRequirementId = requirement.ComplianceRequirementId, Reason = "Unauthorized" }));
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateRequirementAsync(booking.Customer.AccountId,
            dossier.ComplianceDossierId, core.DossierRequirementId,
            new DossierRequest.UpdateRequirementRequest { Instruction = "Unauthorized change", Reason = "Unauthorized" }));
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

    [Fact]
    public async Task ApiLifecycle_ReturnsCreatedAndOk_AndPersistsChanges()
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier, specialist, requirement) = await SeedAsync(db);
        var realtime = new RecordingRealtimeService();
        var controller = new ComplianceDossiersController(CreateService(db, realtime))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, specialist.ToString())], "test"))
            } }
        };
        var created = Assert.IsType<ObjectResult>(await controller.AddRequirement(dossier.ComplianceDossierId,
            new DossierRequest.RequirementRequest
            {
                ComplianceRequirementId = requirement.ComplianceRequirementId, IsMandatory = false,
                LeadTimeDays = 5, Instruction = "Upload Modello 4", Reason = "Italian border crossing"
            }, CancellationToken.None));
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var added = await db.DossierRequirements.SingleAsync(item => item.ComplianceRequirementId == requirement.ComplianceRequirementId);
        Assert.IsType<OkObjectResult>(await controller.UpdateRequirement(dossier.ComplianceDossierId, added.DossierRequirementId,
            new DossierRequest.UpdateRequirementRequest { Instruction = "Revised guidance", Reason = "Checkpoint changed" }, CancellationToken.None));
        Assert.Equal("Revised guidance", added.Instruction);
        Assert.IsType<OkObjectResult>(await controller.RemoveRequirement(dossier.ComplianceDossierId, added.DossierRequirementId,
            new DossierRequest.RemoveRequirementRequest { Reason = "Route changed" }, CancellationToken.None));
        Assert.True(added.IsDeleted);
        Assert.Equal(3, realtime.Notifications.Count);
        var audits = await db.AuditDossiers.ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.All(audits, audit => Assert.Equal(specialist, audit.ActorAccountId));
        Assert.Contains(audits, audit => audit.Summary == "Route changed" && audit.TargetId == added.DossierRequirementId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddMandatoryRequirement_SupportsWholeTripOrBookedHorse(bool appliesToHorse)
    {
        await using var db = TestSupport.CreateDb();
        var (booking, dossier, specialist, requirement) = await SeedAsync(db);
        Guid? horseId = appliesToHorse ? (await db.BookingHorses.SingleAsync()).HorseId : null;
        var realtime = new RecordingRealtimeService();
        var added = await CreateService(db, realtime).AddRequirementAsync(specialist, dossier.ComplianceDossierId,
            new DossierRequest.RequirementRequest
            {
                ComplianceRequirementId = requirement.ComplianceRequirementId, HorseId = horseId,
                IsMandatory = true, LeadTimeDays = 3, Instruction = " Upload Modello 4 ", Reason = " Italian border crossing "
            });
        Assert.True(added.IsMandatory);
        Assert.Equal(horseId, added.HorseId);
        Assert.Equal(booking.RequestedDepartureDate.AddDays(-3), added.DueDate);
        Assert.Equal("Upload Modello 4", added.Instruction);
        Assert.Equal(booking.Customer.AccountId, Assert.Single(realtime.Notifications).AccountId);
        var audit = await db.AuditDossiers.SingleAsync();
        Assert.Equal(specialist, audit.ActorAccountId);
        Assert.Equal(added.DossierRequirementId, audit.TargetId);
        Assert.Equal("Italian border crossing", audit.Summary);
    }

    [Theory]
    [InlineData(ComplianceDossierStatus.SUBMITTED)]
    [InlineData(ComplianceDossierStatus.APPROVED)]
    public async Task SubmittedOrApprovedDossier_BlocksEveryMutation(ComplianceDossierStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier, specialist, requirement) = await SeedAsync(db);
        var optional = new DossierRequirement
        {
            DossierRequirementId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId,
            ComplianceRequirementId = requirement.ComplianceRequirementId, IsMandatory = false
        };
        db.DossierRequirements.Add(optional);
        dossier.Status = status;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var addError = await Assert.ThrowsAsync<ConflictException>(() => service.AddRequirementAsync(specialist,
            dossier.ComplianceDossierId, new DossierRequest.RequirementRequest
            { ComplianceRequirementId = requirement.ComplianceRequirementId, Reason = "Too late" }));
        var updateError = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateRequirementAsync(specialist,
            dossier.ComplianceDossierId, optional.DossierRequirementId,
            new DossierRequest.UpdateRequirementRequest { Instruction = "Late change", Reason = "Too late" }));
        var removeError = await Assert.ThrowsAsync<ConflictException>(() => service.RemoveRequirementAsync(specialist,
            dossier.ComplianceDossierId, optional.DossierRequirementId, new DossierRequest.RemoveRequirementRequest { Reason = "Too late" }));
        Assert.All(new[] { addError, updateError, removeError }, error => Assert.Equal(
            "Cannot modify DossierRequirements after dossier submission to official authorities.", error.Message));
        Assert.False(optional.IsDeleted);
        Assert.Null(optional.Instruction);
        Assert.Empty(await db.AuditDossiers.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(-1, "Italy", 0)]
    [InlineData(3651, "Italy", 0)]
    [InlineData(0, "", 0)]
    [InlineData(0, " ", 0)]
    [InlineData(0, "Italy", 2001)]
    public async Task InvalidInput_DoesNotChangeDossier(int leadTimeDays, string reason, int instructionLength)
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier, specialist, requirement) = await SeedAsync(db);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db).AddRequirementAsync(specialist,
            dossier.ComplianceDossierId, new DossierRequest.RequirementRequest
            {
                ComplianceRequirementId = requirement.ComplianceRequirementId, LeadTimeDays = leadTimeDays,
                Instruction = new string('x', instructionLength), Reason = reason
            }));
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Single(await db.DossierRequirements.ToListAsync());
        Assert.Empty(await db.AuditDossiers.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
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
            new NotificationService.Service(db, realtime ?? new NotificationService.NoopNotificationRealtimeService()),
            null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance);

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
