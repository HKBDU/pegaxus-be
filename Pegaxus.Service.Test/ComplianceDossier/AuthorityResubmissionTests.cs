using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;

namespace Pegaxus.Service.Test;

/// <summary>
/// A required authority that rejects the dossier sends it back to preparation; after the fix it is submitted to that authority again and
/// only the latest submission per authority decides whether the dossier is approved.
/// </summary>
public class AuthorityResubmissionTests
{
    private static readonly string Dgal = CompetentAuthorities.All[5].Name;
    private static readonly string Bmel = CompetentAuthorities.All[7].Name;
    private static readonly string Apha = CompetentAuthorities.All[1].Name;
    private static readonly string Fasfc = CompetentAuthorities.All[9].Name;

    [Fact]
    public async Task Rejection_ReturnsDossierToPreparation_AndAResubmissionCanApproveIt()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var apha = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Bmel));
        var dgal = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Dgal.ToUpperInvariant()));

        await service.RecordAuthorityDecisionAsync(specialist, apha.AuthoritySubmissionId, Reject());
        var dossier = await db.ComplianceDossiers.AsNoTracking().SingleAsync(item => item.ComplianceDossierId == dossierId);
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, dossier.Status);
        Assert.NotNull(dossier.RejectedAt); Assert.Equal("Missing vaccination record", dossier.RejectionReason); Assert.Null(dossier.ClosedAt);

        // Another authority answering while the dossier is being revised does not pull it out of preparation.
        await service.RecordAuthorityDecisionAsync(specialist, dgal.AuthoritySubmissionId, Approve());
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, (await db.ComplianceDossiers.AsNoTracking().SingleAsync()).Status);

        await service.MarkReadyForSubmissionAsync(specialist, dossierId);
        var again = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Bmel));
        var decided = await service.RecordAuthorityDecisionAsync(specialist, again.AuthoritySubmissionId, Approve());

        Assert.Equal("APPROVED", decided.DossierStatus);
        Assert.Equal(3, await db.AuthoritySubmissions.CountAsync());
        Assert.Equal(Dgal, (await db.AuthoritySubmissions.AsNoTracking().SingleAsync(item => item.AuthoritySubmissionId == dgal.AuthoritySubmissionId)).AuthorityName);
    }

    [Fact]
    public async Task Approval_WaitsForTheLatestSubmissionOfEveryRequiredAuthority()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var apha = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Bmel));
        await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Dgal));

        var decided = await service.RecordAuthorityDecisionAsync(specialist, apha.AuthoritySubmissionId, Approve());

        Assert.Equal("PENDING", decided.DossierStatus);
    }

    [Fact]
    public async Task Submission_RejectsAnUnlistedAuthority()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission("Some local office")));

        Assert.Equal("INVALID_AUTHORITY", error.ErrorCode);
        Assert.Empty(await db.AuthoritySubmissions.ToListAsync());
    }

    [Fact]
    public async Task Submission_OnlyAcceptsAuthoritiesOfCountriesOnTheRoute()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());

        // The seeded booking goes from France to Germany: a UK authority has nothing to do with it.
        var offRoute = await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Apha)));
        Assert.Equal("AUTHORITY_NOT_ON_ROUTE", offRoute.ErrorCode);
        var listed = (await service.GetAuthoritySubmissionsAsync(specialist, dossierId)).RouteAuthorities;
        Assert.Equal(["FR", "DE"], listed.RouteCountries);
        Assert.DoesNotContain(listed.AllowedAuthorities, item => item.CountryCode == "GB");

        // A leg through Belgium adds the Belgian authority.
        var dossier = await db.ComplianceDossiers.SingleAsync(item => item.ComplianceDossierId == dossierId);
        var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = dossier.TransportId, PlanCode = "TP-AUTH", Status = TransportPlanStatus.APPROVED };
        var routePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlan = plan, RoutePlanCode = "RP-AUTH", Status = RoutePlanStatus.DRAFT };
        routePlan.RoutePlanRoutes.Add(new RoutePlanRoute
        {
            RoutePlanRouteId = Guid.NewGuid(), RoutePlan = routePlan, SequenceNo = 1, RouteType = RouteType.PRIMARY,
            Route = new Route
            {
                RouteId = Guid.NewGuid(), RouteCode = "RT-AUTH", TransportMode = TransportMode.ROAD,
                FromLocation = new Location { LocationId = Guid.NewGuid(), LocationName = "Lille", Country = "FR" },
                ToLocation = new Location { LocationId = Guid.NewGuid(), LocationName = "Brussels", Country = "BE" }
            }
        });
        db.TransportPlans.Add(plan); db.RoutePlans.Add(routePlan);
        await db.SaveChangesAsync();

        var recorded = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Fasfc));
        Assert.Equal(Fasfc, (await db.AuthoritySubmissions.AsNoTracking().SingleAsync(item => item.AuthoritySubmissionId == recorded.AuthoritySubmissionId)).AuthorityName);
        Assert.Equal(["FR", "BE", "DE"], (await service.GetAuthoritySubmissionsAsync(specialist, dossierId)).RouteAuthorities.RouteCountries);
    }

    private static DossierRequest.RecordAuthoritySubmissionRequest Submission(string authority) => new()
    {
        AuthorityName = authority, SubmissionReference = "REF-" + Guid.NewGuid().ToString("N")[..8], SubmittedAt = DateTimeOffset.UtcNow,
        IsRequired = true, ProofFile = DocumentUploadTests.File("proof.pdf", "application/pdf")
    };

    private static DossierRequest.RecordAuthorityDecisionRequest Approve() =>
        new() { Decision = "APPROVED", PermitNumber = "PERMIT-" + Guid.NewGuid().ToString("N")[..6], DecisionFile = DocumentUploadTests.File("permit.pdf", "application/pdf") };

    private static DossierRequest.RecordAuthorityDecisionRequest Reject() =>
        new() { Decision = "REJECTED", RejectionReason = "Missing vaccination record", DecisionFile = DocumentUploadTests.File("rejection.pdf", "application/pdf") };
}
