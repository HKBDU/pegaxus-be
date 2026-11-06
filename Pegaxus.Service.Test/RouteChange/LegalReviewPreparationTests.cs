using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

public class LegalReviewPreparationTests
{
    [Theory]
    [InlineData(ComplianceDossierStatus.DRAFT, true)]
    [InlineData(ComplianceDossierStatus.IN_PREPARATION, false)]
    [InlineData(ComplianceDossierStatus.APPROVED, false)]
    [InlineData(ComplianceDossierStatus.CLOSED, false)]
    public async Task PrimaryLegalReview_AcceptsInitializedDossierWithoutReopeningAdvancedStates(ComplianceDossierStatus status, bool allowed)
    {
        await using var db = TestSupport.CreateDb();
        var fixture = await RouteChangeTests.Fixture.CreateAsync(db);
        fixture.Plan.Status = RoutePlanStatus.DRAFT;
        fixture.Transport.MainDossier!.Status = status;
        await db.SaveChangesAsync();
        var specialist = await db.Employees.SingleAsync(item => item.Role == EmployeeRole.TRANSPORT_SPECIALIST);
        var service = TestSupport.CreateRoutePlans(db);
        var request = new Pegaxus.Service.RoutePlanService.Request.LegalReviewRequest
        {
            LastUpdatedAt = RouteChangeTests.Fixture.Version(fixture.Plan), IsFeasible = true
        };
        if (allowed)
        {
            Assert.Equal("CONFIRMED", (await service.ReviewLegalAsync(specialist.AccountId, fixture.Plan.RoutePlanId, request)).Status);
            Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, fixture.Transport.MainDossier.Status);
        }
        else
            Assert.Equal("INVALID_DOSSIER_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => service.ReviewLegalAsync(specialist.AccountId, fixture.Plan.RoutePlanId, request))).ErrorCode);
    }
}
