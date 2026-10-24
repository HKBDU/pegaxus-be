using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using Claims = Pegaxus.Service.ClaimService;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class ClaimPostgresTests
{
    [PostgresFact]
    public async Task Postgres_ClaimsTranslateAndTwoManagersCannotBothResolveTheSameClaim()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_claims_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(ClaimStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(ClaimStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var f = await ClaimTests.Fixture.CreateAsync(seed);
            var claim = await f.SubmitAsync();

            // Two managers open the pending claim together: the review is recorded once and both get a view.
            var opened = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
            {
                await using var context = new AppDbContext(options);
                return await f.ServiceFor(context).GetAsync(index == 0 ? f.ManagerAccount.AccountId : f.OtherManagerAccount.AccountId, claim.ClaimId);
            }));
            Assert.Equal(opened[0].ReviewedAt, opened[1].ReviewedAt); Assert.NotNull(opened[0].ReviewedAt);

            await using (var context = new AppDbContext(options))
            {
                var service = f.ServiceFor(context);
                Assert.Equal(1, (await service.GetListAsync(f.ManagerAccount.AccountId, new() { Search = claim.ClaimCode.ToLowerInvariant(), SortBy = "requestedCompensationAmount:desc" })).TotalItems);
                Assert.Equal(1, (await service.GetListAsync(f.ManagerAccount.AccountId, new() { ClaimType = "HORSE_INJURY", Status = "PENDING", SubmittedFrom = new(2026, 10, 15), SubmittedTo = new(2026, 10, 15) })).TotalItems);
                Assert.Equal(1, (await service.GetListAsync(f.CustomerAccount.AccountId, new() { BookingId = f.Booking.BookingId })).TotalItems);
            }

            // Both resolve with the version they saw: exactly one wins, the other is told the claim changed.
            var results = await Task.WhenAll(new[] { f.ManagerAccount.AccountId, f.OtherManagerAccount.AccountId }.Select(async accountId =>
            {
                await using var context = new AppDbContext(options);
                try
                {
                    await f.ServiceFor(context).ResolveAsync(accountId, claim.ClaimId, new()
                    {
                        LastUpdatedAt = opened[0].UpdatedAt, ResolutionType = "REFUND", ApprovedCompensationAmount = 5_000, ResolutionSummary = "Refund of the transport fee."
                    });
                    return (Exception?)null;
                }
                catch (Exception error) { return error; }
            }));
            Assert.Single(results, error => error is null);
            var failure = Assert.IsType<ConflictException>(Assert.Single(results, error => error is not null));
            Assert.Contains(failure.ErrorCode, new[] { "CLAIM_MODIFIED", "CLAIM_ALREADY_RESOLVED" });

            await using var verify = new AppDbContext(options);
            Assert.Single(await verify.ClaimResolutions.ToListAsync());
            Assert.Equal(ClaimStatus.APPROVED, (await verify.Claims.SingleAsync()).Status);
            // One confirmation email for the filing and one for the outcome, never a second outcome.
            var subjects = (await verify.OutboxMessages.Where(item => item.MessageType == MessageTypes.Email).ToListAsync())
                .Select(item => OutboxQueue.Deserialize<EmailPayload>(item.Payload).Subject).Order().ToArray();
            Assert.Equal(2, subjects.Length); Assert.Contains(subjects, subject => subject.StartsWith("Claim received")); Assert.Contains(subjects, subject => subject.StartsWith("Claim approved"));
        }
        finally { await seed.Database.EnsureDeletedAsync(); }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}
