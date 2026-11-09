using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using DossierService = Pegaxus.Service.ComplianceDossierService.Service;

namespace Pegaxus.Service.Test;

/// <summary>The dossier of a return booking reuses the papers checked on the original trip: the TS waives the open checklist at once.</summary>
public class ReturnDossierWaiveTests
{
    [Fact]
    public async Task WaiveForReturn_WaivesOnlyOpenRequirements_NamesOriginalTrip_AndAuditsEach()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, isReturn: true);
        var service = CreateService(db);

        var result = await service.WaiveForReturnAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, new());

        Assert.Equal(2, result.WaivedCount); Assert.Equal(3, result.RequirementCount);
        Assert.Equal("TR-ORIG", result.ReturnOf.TransportCode); Assert.Equal("DOS-ORIG", result.ReturnOf.DossierCode);
        Assert.Equal("Thunder", result.ReturnOf.HorseName);
        var stored = await db.DossierRequirements.AsNoTracking().Where(item => item.ComplianceDossierId == seed.Dossier.ComplianceDossierId).ToListAsync();
        Assert.All(stored, item => Assert.Contains(item.Status, new[] { DossierRequirementStatus.SATISFIED, DossierRequirementStatus.WAIVED }));
        var waived = stored.Where(item => item.Status == DossierRequirementStatus.WAIVED).ToList();
        Assert.Equal(2, waived.Count);
        Assert.All(waived, item => { Assert.Contains("TR-ORIG", item.WaivedReason); Assert.Contains("DOS-ORIG", item.WaivedReason); });
        Assert.Null(stored.Single(item => item.Status == DossierRequirementStatus.SATISFIED).WaivedReason);
        Assert.Equal(2, await db.AuditDossiers.CountAsync(item => item.ActionType == "DOSSIER_REQUIREMENT_WAIVED"));

        var staffView = await service.GetDossierAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, new());
        Assert.Equal("TR-ORIG", staffView.ReturnOf!.TransportCode); Assert.Equal(100m, staffView.CompletionPercent);
        Assert.Null((await service.GetDossierAsync(seed.CustomerAccountId, seed.Dossier.ComplianceDossierId, new())).ReturnOf);

        var again = await service.WaiveForReturnAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, new() { Reason = " Same papers " });
        Assert.Equal(0, again.WaivedCount);
    }

    [Fact]
    public async Task WaiveForReturn_RejectsOrdinaryDossier_AndCustomer()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, isReturn: false);
        var service = CreateService(db);

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            service.WaiveForReturnAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, new()));
        Assert.Equal("NOT_RETURN_BOOKING", error.ErrorCode);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.WaiveForReturnAsync(seed.CustomerAccountId, seed.Dossier.ComplianceDossierId, new()));
        Assert.DoesNotContain(await db.DossierRequirements.AsNoTracking().ToListAsync(), item => item.Status == DossierRequirementStatus.WAIVED);
    }

    private sealed record Seed(ComplianceDossier Dossier, Guid Specialist, Guid CustomerAccountId);

    private static async Task<Seed> SeedAsync(AppDbContext db, bool isReturn)
    {
        var (customerAccount, customer) = await TestSupport.AddCustomerAsync(db);
        var (specialistAccount, specialist) = await TestSupport.AddLogisticsManagerAsync(db);
        specialist.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        var (original, horse) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        horse!.HorseName = "Thunder";
        original.Status = BookingStatus.APPROVED;
        var originalTransport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-ORIG", Booking = original, Status = TransportStatus.IN_TRANSIT };
        db.ComplianceDossiers.Add(new ComplianceDossier
        {
            ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-ORIG", DossierType = DossierType.MAIN, Status = ComplianceDossierStatus.APPROVED,
            Transport = originalTransport
        });
        await db.SaveChangesAsync();
        var link = await db.BookingHorses.SingleAsync(item => item.BookingId == original.BookingId);
        link.DetachedAt = DateTime.UtcNow;

        var (returning, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        returning.Status = BookingStatus.APPROVED;
        returning.ReturnOfTransportId = isReturn ? originalTransport.TransportId : null;
        db.BookingHorses.Add(new BookingHorse { BookingId = returning.BookingId, HorseId = horse.HorseId });
        DossierRequirement Requirement(string code, DossierRequirementStatus status) => new()
        {
            DossierRequirementId = Guid.NewGuid(), IsMandatory = true, Status = status,
            ComplianceRequirement = new ComplianceRequirement { ComplianceRequirementId = Guid.NewGuid(), RequirementCode = code, RequirementName = code }
        };
        var dossier = new ComplianceDossier
        {
            ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-RET", DossierType = DossierType.MAIN, Status = ComplianceDossierStatus.IN_PREPARATION,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-RET", Booking = returning },
            DossierRequirements = [Requirement("EHC", DossierRequirementStatus.SATISFIED), Requirement("PASSPORT", DossierRequirementStatus.PENDING),
                Requirement("MOD4", DossierRequirementStatus.REJECTED)]
        };
        db.ComplianceDossiers.Add(dossier);
        await db.SaveChangesAsync();
        return new(dossier, specialistAccount.AccountId, customerAccount.AccountId);
    }

    private static DossierService CreateService(AppDbContext db) =>
        new(db, null!, new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(),
            new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService()),
            null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance);
}
