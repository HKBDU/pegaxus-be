using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;
using DossierService = Pegaxus.Service.ComplianceDossierService.Service;

namespace Pegaxus.Service.Test;

public class DossierRequirementTests
{
    [Fact]
    public async Task AddUpdateRemove_UpdatesProgress_NotificationAndAudit()
    {
        await using var db = TestSupport.CreateDb();
        var (booking, dossier, specialist, requirement) = await SeedAsync(db);
        var service = CreateService(db);
        var added = await service.AddRequirementAsync(specialist, dossier.ComplianceDossierId, new DossierRequest.RequirementRequest
        {
            ComplianceRequirementId = requirement.ComplianceRequirementId, LeadTimeDays = 5,
            IsMandatory = false, Instruction = "Upload Modello 4", Reason = "Italian border crossing"
        });
        Assert.Equal(booking.RequestedDepartureDate.AddDays(-5), added.DueDate);
        Assert.Single(await db.AccountNotifications.ToListAsync());
        Assert.Equal("ADDED", (await db.AuditDossiers.SingleAsync()).ActionType);
        var updated = await service.UpdateRequirementAsync(specialist, dossier.ComplianceDossierId,
            added.DossierRequirementId, new DossierRequest.UpdateRequirementRequest
            { LeadTimeDays = 7, Instruction = "Upload revised Modello 4", Reason = "Earlier checkpoint" });
        Assert.Equal(booking.RequestedDepartureDate.AddDays(-7), updated.DueDate);
        Assert.False(updated.IsMandatory);

        var removal = await service.RemoveRequirementAsync(specialist, dossier.ComplianceDossierId, added.DossierRequirementId,
            new DossierRequest.RemoveRequirementRequest { Reason = "Route changed" });
        Assert.Equal(1, removal.RequirementCount);
        Assert.Equal(100m, removal.CompletionPercent);
        Assert.Equal(3, await db.AuditDossiers.CountAsync());
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

    private static DossierService CreateService(AppDbContext db) =>
        new(db, null!);
}
