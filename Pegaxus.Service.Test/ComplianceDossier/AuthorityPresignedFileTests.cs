using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;

namespace Pegaxus.Service.Test;

public class AuthorityPresignedFileTests
{
    [Fact]
    public async Task Submission_StoresConfirmedFileAndListSignsItsReadUrl()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());

        await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(DocumentUploadTests.File("proof.pdf", "application/pdf")));

        var stored = await db.AuthoritySubmissionEvidenceFiles.SingleAsync();
        Assert.Equal("proof.pdf", stored.FileName);
        Assert.Equal("application/pdf", stored.MimeType);
        Assert.StartsWith("https://res.cloudinary.com/test-cloud/raw/authenticated/", stored.FileUrl);
        var listed = (await service.GetAuthoritySubmissionsAsync(specialist, dossierId)).Items.Single().EvidenceFiles.Single();
        Assert.StartsWith("https://api.cloudinary.com/", listed.FileUrl);
    }

    [Fact]
    public async Task Submission_RejectsMissingAndOversizedProofFiles()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());

        var missing = await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(null)));
        Assert.Equal("INVALID_FILE_TYPE", missing.ErrorCode);
        var oversized = await Assert.ThrowsAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.RecordAuthoritySubmissionAsync(specialist, dossierId,
            Submission(DocumentUploadTests.File("proof.pdf", "application/pdf", 2 * 1024 * 1024 + 1))));
        Assert.Equal("FILE_TOO_LARGE", oversized.ErrorCode);
        Assert.Empty(await db.AuthoritySubmissions.ToListAsync());
    }

    [Fact]
    public async Task Decision_StoresConfirmedFileAndRequiresOne()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var submission = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(DocumentUploadTests.File("proof.pdf", "application/pdf")));

        var missing = await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthorityDecisionAsync(specialist, submission.AuthoritySubmissionId,
            new() { Decision = "APPROVED", PermitNumber = "PERMIT-1" }));
        Assert.Equal("DECISION_FILE_REQUIRED", missing.ErrorCode);

        var decided = await service.RecordAuthorityDecisionAsync(specialist, submission.AuthoritySubmissionId,
            new() { Decision = "APPROVED", PermitNumber = "PERMIT-1", DecisionFile = DocumentUploadTests.File("permit.png", "image/png") });
        Assert.Equal("APPROVED", decided.SubmissionStatus);
        Assert.Equal(2, await db.AuthoritySubmissionEvidenceFiles.CountAsync());
        var decision = await db.AuthoritySubmissionEvidenceFiles.SingleAsync(item => item.EvidenceType == AuthorityEvidenceType.Acceptance);
        Assert.Equal("permit.png", decision.FileName);
        Assert.Equal("image/png", decision.MimeType);
    }

    private static DossierRequest.RecordAuthoritySubmissionRequest Submission(Pegaxus.Service.UploadService.Request.UploadedAssetReference? proof) => new()
    {
        AuthorityName = "Authority", SubmissionReference = "REF-" + Guid.NewGuid().ToString("N")[..8], SubmittedAt = DateTimeOffset.UtcNow, ProofFile = proof
    };

    internal static async Task<(Guid Specialist, Guid DossierId)> SeedAsync(Pegaxus.Repository.AppDbContext db)
    {
        var (_, dossier, requirement) = await DocumentUploadTests.SeedAsync(db);
        var (specialist, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        requirement.Status = DossierRequirementStatus.SATISFIED;
        dossier.Status = ComplianceDossierStatus.READY_FOR_SUBMISSION;
        await db.SaveChangesAsync();
        return (specialist.AccountId, dossier.ComplianceDossierId);
    }
}
