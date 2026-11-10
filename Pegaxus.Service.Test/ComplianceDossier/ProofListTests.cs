using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;

namespace Pegaxus.Service.Test;

public class ProofListTests
{
    [Fact]
    public async Task QuarantineList_NewestFirst_FiltersByStatus_AndNotificationOpensVerifyPage()
    {
        await using var db = TestSupport.CreateDb();
        var (driverAccountId, dossierId) = await ProofUploadTests.SeedAsync(db);
        var specialistAccountId = await AddSpecialistAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());

        var proofId = await service.SubmitQuarantineProofAsync(driverAccountId, dossierId, new DossierRequest.SubmitQuarantineProofRequest
        {
            BcpName = "BCP Calais", Files = [DocumentUploadTests.File("stamp.png", "image/png")]
        });

        var all = await service.ListQuarantineProofsAsync(specialistAccountId, dossierId, null);
        Assert.Equal(2, all.Count);
        Assert.Equal(proofId, all[0].QuarantineProofId);
        Assert.Equal("PENDING_VERIFICATION", all[0].Status);
        Assert.Equal("BCP Calais", all[0].BcpName);
        Assert.False(string.IsNullOrEmpty(all[0].SubmittedBy.FullName));
        Assert.Equal("PASSED", all[1].Status);

        var pending = await service.ListQuarantineProofsAsync(specialistAccountId, dossierId, "pending_verification");
        Assert.Equal(proofId, Assert.Single(pending).QuarantineProofId);

        Assert.Equal($"/quarantine-proofs/{proofId}/verify", await ActionUrlAsync(db, specialistAccountId));
    }

    [Fact]
    public async Task CustomsList_ReturnsSubmittedProof_AndNotificationOpensVerifyPage()
    {
        await using var db = TestSupport.CreateDb();
        var (driverAccountId, dossierId) = await ProofUploadTests.SeedAsync(db);
        var specialistAccountId = await AddSpecialistAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());

        var proofId = await service.SubmitCustomsClearanceProofAsync(driverAccountId, dossierId, new DossierRequest.SubmitCustomsClearanceProofRequest
        {
            CustomsPortName = "Port of Dover", Files = [DocumentUploadTests.File("stamp.png", "image/png")]
        });

        var row = Assert.Single(await service.ListCustomsClearanceProofsAsync(specialistAccountId, dossierId, null));
        Assert.Equal(proofId, row.CustomsClearanceProofId);
        Assert.Equal("PENDING_VERIFICATION", row.Status);
        Assert.Equal("Port of Dover", row.CustomsPortName);
        Assert.Empty(await service.ListCustomsClearanceProofsAsync(specialistAccountId, dossierId, "CLEARED"));

        Assert.Equal($"/customs-clearance-proofs/{proofId}/verify", await ActionUrlAsync(db, specialistAccountId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_RejectsNonReviewer_UnknownDossier_AndUnknownStatus(bool customs)
    {
        await using var db = TestSupport.CreateDb();
        var (driverAccountId, dossierId) = await ProofUploadTests.SeedAsync(db);
        var specialistAccountId = await AddSpecialistAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        Task List(Guid accountId, Guid id, string? status) => customs
            ? service.ListCustomsClearanceProofsAsync(accountId, id, status)
            : service.ListQuarantineProofsAsync(accountId, id, status);

        await Assert.ThrowsAsync<ForbiddenException>(() => List(driverAccountId, dossierId, null));
        Assert.Equal("DOSSIER_NOT_FOUND",
            (await Assert.ThrowsAsync<NotFoundException>(() => List(specialistAccountId, Guid.NewGuid(), null))).ErrorCode);
        Assert.Equal("INVALID_PROOF_STATUS",
            (await Assert.ThrowsAsync<BadRequestException>(() => List(specialistAccountId, dossierId, "APPROVED"))).ErrorCode);
    }

    private static async Task<Guid> AddSpecialistAsync(AppDbContext db)
    {
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        await db.SaveChangesAsync();
        return account.AccountId;
    }

    private static Task<string?> ActionUrlAsync(AppDbContext db, Guid accountId) => db.Notifications
        .Where(item => item.AccountNotifications.Any(link => link.AccountId == accountId))
        .Select(item => item.ActionUrl)
        .SingleAsync();
}
