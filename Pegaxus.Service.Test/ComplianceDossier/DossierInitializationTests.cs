using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;
using DossierService = Pegaxus.Service.ComplianceDossierService.Service;

namespace Pegaxus.Service.Test;

public class DossierInitializationTests
{
    [Fact]
    public async Task Initialize_ExpandsHorses_CopiesTemplate_AndNotifiesCustomer()
    {
        await using var db = TestSupport.CreateDb();
        var (booking, template, specialist) = await SeedAsync(db);
        var realtime = new RecordingRealtimeService();
        var service = CreateService(db, realtime);
        var request = new DossierRequest.InitializeRequest { DossierTemplateId = template.DossierTemplateId };
        var preview = await service.PreviewAsync(specialist, booking.BookingId, request);
        Assert.Equal(3, preview.Requirements.Count);
        Assert.Equal(2, preview.Items.Count);
        Assert.True(preview.Items[0].AppliesPerHorse);
        Assert.Empty(await db.ComplianceDossiers.ToListAsync());
        Assert.Empty(realtime.Notifications);

        var result = await service.InitializeAsync(specialist, booking.BookingId, request);
        Assert.Equal("IN_PREPARATION", result.Status);
        Assert.Equal("MAIN", result.DossierType);
        Assert.Equal(DossierType.MAIN, (await db.ComplianceDossiers.SingleAsync()).DossierType);
        Assert.Matches(@"^CD-\d{8}-\d{4}$", (await db.ComplianceDossiers.SingleAsync()).DossierCode);
        Assert.Equal(3, result.Requirements.Count);
        Assert.Equal(2, result.Requirements.Count(item => item.HorseId != null));
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), result.Requirements.First().ValidUntil);
        Assert.Equal(template.DossierTemplateId, (await db.ComplianceDossiers.SingleAsync()).DossierTemplateId);
        Assert.Equal(booking.Customer.AccountId, (await db.AccountNotifications.SingleAsync()).AccountId);
        var push = Assert.Single(realtime.Notifications);
        Assert.Equal(booking.Customer.AccountId, push.AccountId);
        Assert.Equal((await db.Notifications.SingleAsync()).NotificationId, push.Notification.Id);
        Assert.Equal(2, template.DossierTemplateItems.Count);
        await Assert.ThrowsAsync<ConflictException>(() => service.InitializeAsync(specialist, booking.BookingId, request));
    }

    [Fact]
    public async Task CustomSelection_DoesNotChangeTemplate_AndWorksWithoutTemplate()
    {
        await using var db = TestSupport.CreateDb();
        var (booking, template, specialist) = await SeedAsync(db);
        var service = CreateService(db);
        var request = new DossierRequest.InitializeRequest
        {
            DossierTemplateId = template.DossierTemplateId,
            Items = [new() { ComplianceRequirementId = template.DossierTemplateItems.Last().ComplianceRequirementId }]
        };
        Assert.Single((await service.PreviewAsync(specialist, booking.BookingId, request)).Requirements);
        Assert.Equal(2, template.DossierTemplateItems.Count);
        request.DossierTemplateId = null;
        var result = await service.InitializeAsync(specialist, booking.BookingId, request);
        Assert.Single(result.Requirements);
        Assert.Null((await db.ComplianceDossiers.SingleAsync()).DossierTemplateId);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("inactive-template")]
    [InlineData("deleted-template")]
    [InlineData("expired-template")]
    [InlineData("inactive-requirement")]
    [InlineData("duplicate-item")]
    [InlineData("empty-items")]
    [InlineData("no-horses")]
    [InlineData("stale-booking")]
    [InlineData("wrong-role")]
    [InlineData("null-item")]
    [InlineData("invalid-validity")]
    [InlineData("no-template-or-items")]
    public async Task InvalidInitialization_WritesNothing(string invalid)
    {
        await using var db = TestSupport.CreateDb();
        var (booking, template, specialist) = await SeedAsync(db);
        var request = new DossierRequest.InitializeRequest { DossierTemplateId = template.DossierTemplateId };
        switch (invalid)
        {
            case "pending": booking.Status = BookingStatus.PENDING; break;
            case "inactive-template": template.Status = DossierTemplateStatus.INACTIVE; break;
            case "deleted-template": template.IsDeleted = true; break;
            case "expired-template": template.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1); break;
            case "inactive-requirement": (await db.ComplianceRequirements.FirstAsync()).Status = ComplianceRequirementStatus.INACTIVE; break;
            case "duplicate-item": request.Items = [new() { ComplianceRequirementId = template.DossierTemplateItems.First().ComplianceRequirementId }, new() { ComplianceRequirementId = template.DossierTemplateItems.First().ComplianceRequirementId }]; break;
            case "empty-items": request.Items = []; break;
            case "no-horses": db.BookingHorses.RemoveRange(booking.BookingHorses); break;
            case "stale-booking": request.BookingUpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(booking.UpdatedAt.AddMinutes(-1), DateTimeKind.Utc)); break;
            case "wrong-role": (await db.Employees.SingleAsync()).Role = EmployeeRole.DRIVER; break;
            case "null-item": request.Items = [null!]; break;
            case "invalid-validity": (await db.ComplianceRequirements.FirstAsync()).ValidityPeriodDays = int.MaxValue; break;
            case "no-template-or-items": request.DossierTemplateId = null; break;
        }
        await db.SaveChangesAsync();
        await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() =>
            CreateService(db).InitializeAsync(specialist, booking.BookingId, request));
        Assert.Empty(await db.ComplianceDossiers.ToListAsync());
        Assert.Empty(await db.DossierRequirements.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData("CANCELLED", ComplianceDossierStatus.CLOSED)]
    [InlineData("DRAFT", ComplianceDossierStatus.DRAFT)]
    public async Task Negotiation_DisablesDossierRequirements_AndNotifiesSpecialist(string status, ComplianceDossierStatus dossierStatus)
    {
        await using var db = TestSupport.CreateDb();
        var (booking, template, specialist) = await SeedAsync(db);
        await CreateService(db).InitializeAsync(specialist, booking.BookingId,
            new() { DossierTemplateId = template.DossierTemplateId });
        var assignedEmployee = await db.Employees.SingleAsync(item => item.AccountId == specialist);
        db.EmployeeTransportPlans.Add(new()
        {
            Employee = assignedEmployee,
            TransportPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), Transport = booking.Transport!, PlanCode = "PLAN-NEGOTIATE" }
        });
        var (_, unassigned) = await TestSupport.AddLogisticsManagerAsync(db);
        unassigned.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        await db.SaveChangesAsync();
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var realtime = new RecordingRealtimeService();
        var service = new Pegaxus.Service.BookingService.Service(db, TestSupport.CreateMailService(),
            new NotificationService.Service(db, realtime));
        await service.NegotiateAsync(manager.AccountId, booking.BookingId,
            new() { Status = status, Reason = "Customer negotiated a change" });

        Assert.Equal(dossierStatus, (await db.ComplianceDossiers.SingleAsync()).Status);
        Assert.All(await db.DossierRequirements.ToListAsync(), item => Assert.True(item.IsDeleted));
        Assert.Contains(await db.AccountNotifications.ToListAsync(), item => item.AccountId == specialist);
        Assert.Equal(2, realtime.Notifications.Count);
        Assert.Contains(realtime.Notifications, item => item.AccountId == specialist);
        Assert.Contains(realtime.Notifications, item => item.AccountId == booking.Customer.AccountId);
        Assert.DoesNotContain(realtime.Notifications, item => item.AccountId == unassigned.AccountId);
        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateService(db).InitializeAsync(
            specialist, booking.BookingId, new() { DossierTemplateId = template.DossierTemplateId, BookingUpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) }));
        Assert.Equal("BOOKING_STATUS_CHANGED", error.ErrorCode);
    }

    [Fact]
    public async Task StaleBookingStatus_CannotOverwriteNegotiation()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var (booking, _, _) = await SeedAsync(db);
        await using var staleDb = new AppDbContext(options);
        var staleBooking = await staleDb.Bookings.SingleAsync();
        booking.Status = BookingStatus.CANCELLED;
        await db.SaveChangesAsync();
        staleBooking.Status = BookingStatus.DRAFT;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
        Assert.Equal(BookingStatus.CANCELLED, (await db.Bookings.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task TemplateOptions_ExcludeInactiveTemplates_AndRankCountryMatches()
    {
        await using var db = TestSupport.CreateDb();
        var (booking, template, specialist) = await SeedAsync(db);
        var options = await CreateService(db).GetTemplateOptionsAsync(specialist, booking.BookingId);
        Assert.Equal(2, Assert.Single(options).MatchingRequirements);
        template.Status = DossierTemplateStatus.INACTIVE;
        await db.SaveChangesAsync();
        Assert.Empty(await CreateService(db).GetTemplateOptionsAsync(specialist, booking.BookingId));
    }

    [Fact]
    public async Task Negotiation_RejectsCustomer_WithoutChangingBooking()
    {
        await using var db = TestSupport.CreateDb();
        var (booking, _, _) = await SeedAsync(db);
        var service = new Pegaxus.Service.BookingService.Service(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.NegotiateAsync(booking.Customer.AccountId,
            booking.BookingId, new() { Status = "CANCELLED", Reason = "Unauthorized request" }));
        Assert.Equal(BookingStatus.APPROVED, booking.Status);
    }

    private static async Task<(Booking Booking, DossierTemplate Template, Guid Specialist)> SeedAsync(AppDbContext db)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        booking.Status = BookingStatus.APPROVED;
        booking.Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TP-01", BookingId = booking.BookingId };
        db.Transports.Add(booking.Transport);
        db.BookingHorses.Add(new BookingHorse
        {
            BookingId = booking.BookingId,
            Horse = new Horse { HorseId = Guid.NewGuid(), HorseName = "Second horse", CustomerId = customer.CustomerId }
        });
        var template = new DossierTemplate
        {
            DossierTemplateId = Guid.NewGuid(), TemplateCode = "DT-UK-EU-01", TemplateName = "UK to EU", VersionNo = 1,
            Status = DossierTemplateStatus.ACTIVE,
            DossierTemplateItems = Enumerable.Range(1, 2).Select(index => new DossierTemplateItem
            {
                DossierTemplateItemId = Guid.NewGuid(), SequenceNo = index, AppliesPerHorse = index == 1,
                IsMandatory = true, Instruction = "Original instruction",
                ComplianceRequirement = new ComplianceRequirement
                {
                    ComplianceRequirementId = Guid.NewGuid(), RequirementCode = $"CR-{index}", RequirementName = $"Requirement {index}",
                    ValidityPeriodDays = 30, Country = "France"
                }
            }).ToList()
        };
        db.DossierTemplates.Add(template);
        await db.SaveChangesAsync();
        return (booking, template, account.AccountId);
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
