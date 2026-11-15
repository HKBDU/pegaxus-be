using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;

namespace Pegaxus.Service.Test;

/// <summary>
/// Dates on an authority decision must fit the submission and the trip, and a dossier an authority sent back must be revised before it
/// is marked ready for submission again.
/// </summary>
public class AuthorityDecisionRulesTests
{
    private static readonly string Bmel = CompetentAuthorities.All[7].Name;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task RevisionDueDate_CannotBeInThePast()
    {
        var (service, specialist, _, submissionId, db) = await SubmittedAsync();
        await using var owned = db;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthorityDecisionAsync(specialist, submissionId,
            Returned(Today.AddDays(-1))));

        Assert.Equal("REVISION_DUE_DATE_IN_PAST", error.ErrorCode);
        Assert.Equal("RETURNED", (await service.RecordAuthorityDecisionAsync(specialist, submissionId, Returned(Today))).SubmissionStatus);
    }

    [Fact]
    public async Task PermitIssuedDate_MustBeBetweenTheSubmissionAndToday()
    {
        var (service, specialist, _, submissionId, db) = await SubmittedAsync();
        await using var owned = db;

        var future = await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthorityDecisionAsync(specialist, submissionId,
            Approved(Today.AddDays(1), null)));
        var early = await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthorityDecisionAsync(specialist, submissionId,
            Approved(Today.AddDays(-1), null)));

        Assert.Equal("PERMIT_ISSUED_DATE_IN_FUTURE", future.ErrorCode);
        Assert.Equal("PERMIT_ISSUED_BEFORE_SUBMISSION", early.ErrorCode);
        Assert.Equal("APPROVED", (await service.RecordAuthorityDecisionAsync(specialist, submissionId, Approved(Today, null))).SubmissionStatus);
    }

    [Fact]
    public async Task PermitExpiry_MustCoverTheWholeTrip()
    {
        var (service, specialist, dossierId, submissionId, db) = await SubmittedAsync();
        await using var owned = db;
        var tripEnd = (await service.GetAuthoritySubmissionsAsync(specialist, dossierId)).TripEndDate;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthorityDecisionAsync(specialist, submissionId,
            Approved(Today, tripEnd.AddDays(-1))));

        Assert.Equal("PERMIT_EXPIRES_BEFORE_TRIP_END", error.ErrorCode);
        Assert.Equal("APPROVED", (await service.RecordAuthorityDecisionAsync(specialist, submissionId, Approved(Today, tripEnd))).SubmissionStatus);
    }

    [Fact]
    public async Task TripWindow_FollowsTheBookingUntilARouteIsScheduled()
    {
        var (service, specialist, dossierId, _, db) = await SubmittedAsync();
        await using var owned = db;
        var booking = await db.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId)
            .Select(item => item.Transport.Booking).SingleAsync();

        var listed = await service.GetAuthoritySubmissionsAsync(specialist, dossierId);

        Assert.Equal(booking.RequestedDepartureDate, listed.TripStartDate);
        Assert.Equal(booking.RequestedArrivalDate ?? booking.RequestedDepartureDate, listed.TripEndDate);
    }

    [Fact]
    public async Task SentBackDossier_CannotBeMarkedReadyUntilTheSpecialistRevisesIt()
    {
        var (service, specialist, dossierId, submissionId, db) = await SubmittedAsync();
        await using var owned = db;
        await service.RecordAuthorityDecisionAsync(specialist, submissionId, new()
        {
            Decision = "REJECTED", RejectionReason = "An export health certificate is missing",
            DecisionFile = DocumentUploadTests.File("rejection.pdf", "application/pdf")
        });

        var blocked = await Assert.ThrowsAsync<ConflictException>(() => service.MarkReadyForSubmissionAsync(specialist, dossierId));
        var pending = (await service.GetDossierAsync(specialist, dossierId, new DossierRequest.ViewRequest())).PendingAuthorityRevision;

        Assert.Equal("AUTHORITY_REVISION_PENDING", blocked.ErrorCode);
        Assert.NotNull(pending);
        Assert.Equal(Bmel, pending.AuthorityName);
        Assert.Equal("REJECTED", pending.Decision);

        await ReviseRequirementAsync(db, dossierId);

        Assert.Null((await service.GetDossierAsync(specialist, dossierId, new DossierRequest.ViewRequest())).PendingAuthorityRevision);
        Assert.Equal("READY_FOR_SUBMISSION", (await service.MarkReadyForSubmissionAsync(specialist, dossierId)).Status);
    }

    [Fact]
    public async Task ReturnedForMoreInformation_AlsoWaitsForARevision()
    {
        var (service, specialist, dossierId, submissionId, db) = await SubmittedAsync();
        await using var owned = db;
        await service.RecordAuthorityDecisionAsync(specialist, submissionId, Returned(Today.AddDays(3)));
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, (await db.ComplianceDossiers.AsNoTracking().SingleAsync()).Status);

        var blocked = await Assert.ThrowsAsync<ConflictException>(() => service.MarkReadyForSubmissionAsync(specialist, dossierId));

        Assert.Equal("AUTHORITY_REVISION_PENDING", blocked.ErrorCode);
    }

    /// <summary>Stands in for the specialist changing a requirement (adding one, waiving one, or re-reviewing its document).</summary>
    internal static async Task ReviseRequirementAsync(Repository.AppDbContext db, Guid dossierId)
    {
        var requirement = await db.DossierRequirements.FirstAsync(item => item.ComplianceDossierId == dossierId);
        requirement.UpdatedAt = DbTime.UtcNow().AddMinutes(1);
        await db.SaveChangesAsync();
    }

    private static async Task<(ComplianceDossierService.Service Service, Guid Specialist, Guid DossierId, Guid SubmissionId, Repository.AppDbContext Db)>
        SubmittedAsync()
    {
        var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var submission = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, new DossierRequest.RecordAuthoritySubmissionRequest
        {
            AuthorityName = Bmel, SubmissionReference = "REF-" + Guid.NewGuid().ToString("N")[..8], SubmittedAt = DateTimeOffset.UtcNow,
            IsRequired = true, ProofFile = DocumentUploadTests.File("proof.pdf", "application/pdf")
        });
        return (service, specialist, dossierId, submission.AuthoritySubmissionId, db);
    }

    private static DossierRequest.RecordAuthorityDecisionRequest Approved(DateOnly issued, DateOnly? expiry) => new()
    {
        Decision = "APPROVED", PermitNumber = "PERMIT-" + Guid.NewGuid().ToString("N")[..6], PermitIssuedDate = issued, PermitExpiryDate = expiry,
        DecisionFile = DocumentUploadTests.File("permit.pdf", "application/pdf")
    };

    private static DossierRequest.RecordAuthorityDecisionRequest Returned(DateOnly due) => new()
    {
        Decision = "RETURNED", RevisionDueDate = due, DecisionNote = "Add the export health certificate",
        DecisionFile = DocumentUploadTests.File("request.pdf", "application/pdf")
    };
}
