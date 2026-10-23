using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

using Claims = Pegaxus.Service.ClaimService;
using ClaimEntity = Pegaxus.Repository.Entities.Claim;

namespace Pegaxus.Service.Test;

public class ClaimTests
{
    // The delivery handover was signed 4 hours before this moment, so 20 hours of the claim window are left.
    internal static readonly DateTimeOffset Now = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    // Stored timestamps are UTC with an unspecified kind.
    internal static DateTime NowDb => DateTime.SpecifyKind(Now.UtcDateTime, DateTimeKind.Unspecified);

    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Value;
    }

    internal sealed class Fixture
    {
        internal required AppDbContext Db { get; init; }
        internal required FixedClock Clock { get; init; }
        internal required Account CustomerAccount { get; init; }
        internal required Customer Customer { get; init; }
        internal required Account ManagerAccount { get; init; }
        internal required Employee Manager { get; init; }
        internal required Account OtherManagerAccount { get; init; }
        internal required Account StrangerAccount { get; init; }
        internal required Booking Booking { get; init; }
        internal required Transport Transport { get; init; }
        internal HandoverRecord? Delivery { get; init; }

        internal Claims.Service Service => ServiceFor(Db);

        /// <summary>The same service on another context, for tests where two requests run at the same time.</summary>
        internal Claims.Service ServiceFor(AppDbContext context) => new(context, Clock, new Pegaxus.Service.NotificationService.Service(context, new NoopNotificationRealtimeService()),
            new HandoverCompletionTests.SignatureMedia(), new Claims.Validator.SubmitRequestValidator(), new Claims.Validator.ListRequestValidator(),
            new Claims.Validator.ResolveRequestValidator(), new Claims.Validator.CancelRequestValidator(), new Claims.Validator.CompleteRequestValidator());

        internal static async Task<Fixture> CreateAsync(AppDbContext db, double? deliveredHoursAgo = 4)
        {
            var (customerAccount, customer) = await TestSupport.AddCustomerAsync(db);
            var (managerAccount, manager) = await TestSupport.AddLogisticsManagerAsync(db);
            var (otherManagerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
            var (strangerAccount, _) = await TestSupport.AddCustomerAsync(db);
            var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
            booking.Status = BookingStatus.APPROVED;
            var time = new DateTime(2026, 10, 10, 8, 0, 0);
            var transport = new Transport { TransportId = Guid.NewGuid(), BookingId = booking.BookingId, Booking = booking, Status = TransportStatus.COMPLETED,
                TransportCode = "TR-" + Guid.NewGuid().ToString("N"), CreatedAt = time, UpdatedAt = time };
            db.Transports.Add(transport);
            db.HandoverRecords.Add(Record(transport, HandoverType.PICKUP, 1, new DateTime(2026, 10, 14, 6, 30, 0), "All horses in good condition", null));
            HandoverRecord? delivery = null;
            if (deliveredHoursAgo is { } hours)
            {
                delivery = Record(transport, HandoverType.DELIVERY, 2, NowDb.AddHours(-hours), "Pegasus King has a small scratch", "Scratch noted at delivery");
                db.HandoverRecords.Add(delivery);
            }
            await db.SaveChangesAsync();
            return new() { Db = db, Clock = new FixedClock(Now), CustomerAccount = customerAccount, Customer = customer, ManagerAccount = managerAccount,
                Manager = manager, OtherManagerAccount = otherManagerAccount, StrangerAccount = strangerAccount, Booking = booking, Transport = transport, Delivery = delivery };
        }

        private static HandoverRecord Record(Transport transport, HandoverType type, int sequence, DateTime completedAt, string condition, string? discrepancy) => new()
        {
            HandoverRecordId = Guid.NewGuid(), TransportId = transport.TransportId, Transport = transport, HandoverCode = "HO-" + Guid.NewGuid().ToString("N"),
            SequenceNo = sequence, HandoverType = type, Status = HandoverStatus.COMPLETED, FromType = HandoverPartyType.EMPLOYEE, ToType = HandoverPartyType.CUSTOMER,
            FromPartyName = "Pegasus Transport", ToPartyName = "Example Racing Club", HorseConditionNote = condition, DiscrepancyNote = discrepancy,
            SenderSignatureUrl = "https://storage.example.com/sender.png", ReceiverSignatureUrl = "https://storage.example.com/receiver.png",
            ActualHandoverAt = completedAt, CompletedAt = completedAt, CreatedAt = completedAt, UpdatedAt = completedAt
        };

        internal static Claims.Request.SubmitRequest Request() => new()
        {
            ClaimType = "HORSE_INJURY", Title = "  Pegasus King has a leg injury  ", Description = "On arrival the horse has a bleeding cut on the front left leg.",
            RequestedCompensationAmount = 30_000_000, EvidenceNote = " Vet report ", CustomerNote = "Please cover the treatment."
        };

        internal Task<Claims.Response.ClaimResponse> SubmitAsync(Claims.Request.SubmitRequest? request = null) =>
            Service.SubmitAsync(CustomerAccount.AccountId, Booking.BookingId, request ?? Request());

        /// <summary>A claim opened by a manager (so it is reviewed) and ready to be resolved with its current version.</summary>
        internal async Task<Claims.Response.ClaimResponse> OpenedAsync()
        {
            var claim = await SubmitAsync();
            return await Service.GetAsync(ManagerAccount.AccountId, claim.ClaimId);
        }

        internal Task<Claims.Response.ClaimResponse> ResolveAsync(Claims.Response.ClaimResponse claim, string type = "PARTIAL_COMPENSATION", decimal? amount = 18_000_000) =>
            Service.ResolveAsync(ManagerAccount.AccountId, claim.ClaimId, new()
            {
                LastUpdatedAt = claim.UpdatedAt, ResolutionType = type, ApprovedCompensationAmount = amount,
                ResolutionSummary = "The company covers part of the veterinary treatment.", CorrectiveAction = "Review the box padding.", InternalNote = "Agreed by phone."
            });

        internal Invoice AddInvoice(InvoiceType type, InvoiceStatus status, decimal total = 1_000) => AddInvoiceCore(type, status, total);

        private Invoice AddInvoiceCore(InvoiceType type, InvoiceStatus status, decimal total)
        {
            var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-" + Guid.NewGuid().ToString("N"), BookingId = Booking.BookingId,
                EmployeeId = Manager.EmployeeId, Type = type, Status = status, Currency = "VND", TotalAmount = total, Subtotal = total,
                IssuedAt = new DateTime(2026, 10, 14), PaidAt = status == InvoiceStatus.PAID ? new DateTime(2026, 10, 14, 12, 0, 0) : null,
                CreatedAt = new DateTime(2026, 10, 14), UpdatedAt = new DateTime(2026, 10, 14) };
            Db.Invoices.Add(invoice);
            return invoice;
        }
    }

    private static async Task<T> ThrowsAsync<T>(Task task, string errorCode) where T : Exceptions.Exceptions
    {
        var error = await Assert.ThrowsAsync<T>(() => task);
        Assert.Equal(errorCode, error.ErrorCode);
        return error;
    }

    // ---------------------------------------------------------------- CL1

    [Fact]
    public async Task Eligibility_WithinTheWindow_ReportsTheDeadlineAndTheSecondsLeft()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await f.SubmitAsync();

        var result = await f.Service.GetEligibilityAsync(f.CustomerAccount.AccountId, f.Booking.BookingId);

        Assert.True(result.CanSubmit); Assert.Null(result.BlockedReason); Assert.Equal(Now, result.ServerTime);
        Assert.Equal(f.Delivery!.HandoverRecordId, result.Handover!.HandoverRecordId); Assert.Equal("DELIVERY", result.Handover.HandoverType);
        Assert.Equal(Now.AddHours(20), result.ClaimDeadline); Assert.Equal(20 * 3600, result.RemainingSeconds);
        Assert.Equal(1, result.ClaimCount); Assert.Equal("TR", result.Transport!.TransportCode[..2]);
    }

    [Fact]
    public async Task Eligibility_WithoutASignedDelivery_IsBlockedAndHidesTheHandover()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db, deliveredHoursAgo: null);

        var result = await f.Service.GetEligibilityAsync(f.CustomerAccount.AccountId, f.Booking.BookingId);

        Assert.False(result.CanSubmit); Assert.Equal("HANDOVER_NOT_COMPLETED", result.BlockedReason);
        Assert.Null(result.Handover); Assert.Null(result.ClaimDeadline); Assert.Null(result.RemainingSeconds); Assert.Equal(0, result.ClaimCount);
    }

    [Fact]
    public async Task Eligibility_AfterTheWindow_IsBlockedWithZeroSecondsLeft_AndTheDeadlineItselfStillCounts()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db, deliveredHoursAgo: 24);

        f.Clock.Value = Now; // exactly 24 hours after the signature
        var atDeadline = await f.Service.GetEligibilityAsync(f.CustomerAccount.AccountId, f.Booking.BookingId);
        f.Clock.Value = Now.AddSeconds(1);
        var late = await f.Service.GetEligibilityAsync(f.CustomerAccount.AccountId, f.Booking.BookingId);

        Assert.True(atDeadline.CanSubmit); Assert.Equal(0, atDeadline.RemainingSeconds);
        Assert.False(late.CanSubmit); Assert.Equal("CLAIM_WINDOW_EXPIRED", late.BlockedReason); Assert.Equal(0, late.RemainingSeconds);
    }

    [Fact]
    public async Task Eligibility_AManagerSeesAnyBooking_AStrangerGetsNotFound_AndOtherRolesAreForbidden()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        Assert.True((await f.Service.GetEligibilityAsync(f.ManagerAccount.AccountId, f.Booking.BookingId)).CanSubmit);
        await ThrowsAsync<NotFoundException>(f.Service.GetEligibilityAsync(f.StrangerAccount.AccountId, f.Booking.BookingId), "BOOKING_NOT_FOUND");
        await ThrowsAsync<NotFoundException>(f.Service.GetEligibilityAsync(f.CustomerAccount.AccountId, Guid.NewGuid()), "BOOKING_NOT_FOUND");
        var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetEligibilityAsync(driver.AccountId, f.Booking.BookingId));
    }

    // ---------------------------------------------------------------- CL2

    [Fact]
    public async Task Submit_StoresAPendingClaimTellsEveryManager_AndEmailsOnlyTheCustomer()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var result = await f.SubmitAsync();

        Assert.Matches("^CLM-20261015-[0-9]{4}$", result.ClaimCode); Assert.Equal("PENDING", result.Status); Assert.Equal("HORSE_INJURY", result.ClaimType);
        Assert.Equal("Pegasus King has a leg injury", result.Title); Assert.Equal("VND", result.Currency); Assert.Equal(30_000_000, result.RequestedCompensationAmount);
        Assert.Equal("Vet report", result.EvidenceNote); Assert.Equal(Now, result.SubmittedAt); Assert.Null(result.ReviewedAt);
        Assert.Equal(["CANCEL"], result.AvailableActions); Assert.IsNotType<Claims.Response.ManagerClaimResponse>(result);
        Assert.IsNotType<Claims.Response.ManagerHandover>(result.Handover); Assert.Equal(Now.AddHours(20), result.Handover.ClaimDeadline);
        var saved = await db.Claims.SingleAsync();
        Assert.Equal(f.Customer.CustomerId, saved.CustomerId); Assert.Equal(f.Delivery!.HandoverRecordId, saved.HandoverRecordId);

        var notifications = await db.Notifications.ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, item => { Assert.Equal(NotificationType.CLAIM, item.NotificationType); Assert.Equal(NotificationPriority.HIGH, item.Priority);
            Assert.Equal("Claim", item.ReferenceType); Assert.Equal(saved.ClaimId, item.ReferenceId); Assert.Equal($"/admin/claims/{saved.ClaimId}", item.ActionUrl); });
        Assert.Equal(new[] { f.ManagerAccount.AccountId, f.OtherManagerAccount.AccountId }.Order(), (await db.AccountNotifications.Select(item => item.AccountId).ToArrayAsync()).Order());
        var email = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(await db.OutboxMessages.ToListAsync()).Payload);
        Assert.Equal((await db.Accounts.SingleAsync(item => item.AccountId == f.CustomerAccount.AccountId)).Email, email.To); Assert.Contains(saved.ClaimCode, email.Subject);
    }

    [Fact]
    public async Task Submit_ACustomerMayFileSeveralClaimsForTheSameDelivery()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var first = await f.SubmitAsync(); var second = await f.SubmitAsync();

        Assert.NotEqual(first.ClaimCode, second.ClaimCode); Assert.Equal(2, await db.Claims.CountAsync());
    }

    [Fact]
    public async Task Submit_NeedsASignedDelivery_AndTheTwentyFourHourWindow()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db, deliveredHoursAgo: null);
        await ThrowsAsync<UnprocessableEntityException>(f.SubmitAsync(), "HANDOVER_NOT_COMPLETED");

        await using var lateDb = TestSupport.CreateDb(); var late = await Fixture.CreateAsync(lateDb, deliveredHoursAgo: 24.5);
        await ThrowsAsync<UnprocessableEntityException>(late.SubmitAsync(), "CLAIM_WINDOW_EXPIRED");

        Assert.Empty(db.Claims); Assert.Empty(lateDb.Claims); Assert.Empty(lateDb.Notifications); Assert.Empty(lateDb.OutboxMessages);
    }

    [Fact]
    public async Task Submit_OnlyTheOwnerCustomerMaySubmit()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        await ThrowsAsync<NotFoundException>(f.Service.SubmitAsync(f.StrangerAccount.AccountId, f.Booking.BookingId, Fixture.Request()), "BOOKING_NOT_FOUND");
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.SubmitAsync(f.ManagerAccount.AccountId, f.Booking.BookingId, Fixture.Request()));
        Assert.Empty(db.Claims);
    }

    [Theory]
    [InlineData("type", "claimType")]
    [InlineData("shortTitle", "title")]
    [InlineData("longTitle", "title")]
    [InlineData("shortDescription", "description")]
    [InlineData("zeroAmount", "requestedCompensationAmount")]
    [InlineData("fractionalAmount", "requestedCompensationAmount")]
    [InlineData("longNote", "evidenceNote")]
    public async Task Submit_InvalidInput_ReturnsCamelCaseFieldErrorsAndSavesNothing(string problem, string field)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var request = Fixture.Request();
        switch (problem)
        {
            case "type": request.ClaimType = "99"; break;
            case "shortTitle": request.Title = "  short  "; break;
            case "longTitle": request.Title = new string('x', 201); break;
            case "shortDescription": request.Description = "too short"; break;
            case "zeroAmount": request.RequestedCompensationAmount = 0; break;
            case "fractionalAmount": request.RequestedCompensationAmount = 100.5m; break;
            case "longNote": request.EvidenceNote = new string('x', 2001); break;
        }

        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.SubmitAsync(request));

        Assert.Contains(error.Errors, item => item.Field == field); Assert.Empty(db.Claims);
    }

    [Fact]
    public async Task Submit_AClaimWithoutMoneyIsAllowed()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var request = Fixture.Request(); request.RequestedCompensationAmount = null; request.EvidenceNote = "  "; request.CustomerNote = null;

        var result = await f.SubmitAsync(request);

        Assert.Null(result.RequestedCompensationAmount); Assert.Null(result.EvidenceNote); Assert.Null(result.CustomerNote);
    }

    // ---------------------------------------------------------------- CL3

    [Fact]
    public async Task List_ACustomerSeesOnlyTheirOwnClaims_AManagerSeesAll()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await f.SubmitAsync(); await f.SubmitAsync();
        var (strangerAccount, stranger) = (f.StrangerAccount, await db.Customers.SingleAsync(item => item.AccountId == f.StrangerAccount.AccountId));
        var (otherBooking, _) = await TestSupport.AddPendingBookingAsync(db, stranger);
        var otherTransport = new Transport { TransportId = Guid.NewGuid(), BookingId = otherBooking.BookingId, Booking = otherBooking, TransportCode = "TR-" + Guid.NewGuid().ToString("N"),
            Status = TransportStatus.COMPLETED, CreatedAt = new DateTime(2026, 10, 10), UpdatedAt = new DateTime(2026, 10, 10) };
        var otherDelivery = new HandoverRecord { HandoverRecordId = Guid.NewGuid(), TransportId = otherTransport.TransportId, Transport = otherTransport, HandoverCode = "HO-" + Guid.NewGuid().ToString("N"),
            SequenceNo = 1, HandoverType = HandoverType.DELIVERY, Status = HandoverStatus.COMPLETED, FromType = HandoverPartyType.EMPLOYEE, ToType = HandoverPartyType.CUSTOMER,
            CompletedAt = NowDb.AddHours(-1), CreatedAt = new DateTime(2026, 10, 10), UpdatedAt = new DateTime(2026, 10, 10) };
        db.AddRange(otherTransport, otherDelivery);
        db.Claims.Add(new ClaimEntity { ClaimId = Guid.NewGuid(), ClaimCode = "CLM-20261015-9999", CustomerId = stranger.CustomerId, HandoverRecordId = otherDelivery.HandoverRecordId,
            ClaimType = ClaimType.DAMAGE, Status = ClaimStatus.PENDING, Title = "Another customer claim", Description = "Another customer description", SubmittedAt = NowDb,
            CreatedAt = NowDb, UpdatedAt = NowDb });
        await db.SaveChangesAsync();

        var customer = await f.Service.GetListAsync(f.CustomerAccount.AccountId, new());
        var manager = await f.Service.GetListAsync(f.ManagerAccount.AccountId, new());
        var strangerList = await f.Service.GetListAsync(strangerAccount.AccountId, new());

        Assert.Equal(2, customer.TotalItems); Assert.All(customer.Items, item => Assert.Equal(f.Booking.BookingId, item.BookingId));
        Assert.Equal(3, manager.TotalItems); Assert.Equal(1, strangerList.TotalItems);
    }

    [Fact]
    public async Task List_FiltersSortsSearchesAndPages()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var injury = await f.SubmitAsync();
        f.Clock.Value = Now.AddMinutes(5);
        var delay = Fixture.Request(); delay.ClaimType = "DELAY"; delay.RequestedCompensationAmount = 5_000_000; delay.Title = "The delivery arrived hours late";
        var late = await f.SubmitAsync(delay);
        await f.Service.CancelAsync(f.CustomerAccount.AccountId, late.ClaimId, new() { LastUpdatedAt = late.UpdatedAt, CancellationReason = "We settled this directly" });

        var byType = await f.Service.GetListAsync(f.ManagerAccount.AccountId, new() { ClaimType = "delay" });
        var byStatus = await f.Service.GetListAsync(f.ManagerAccount.AccountId, new() { Status = "PENDING,REJECTED" });
        var byBooking = await f.Service.GetListAsync(f.ManagerAccount.AccountId, new() { BookingId = f.Booking.BookingId });
        var byCode = await f.Service.GetListAsync(f.ManagerAccount.AccountId, new() { Search = injury.ClaimCode.ToLowerInvariant() });
        var byDay = await f.Service.GetListAsync(f.ManagerAccount.AccountId, new() { SubmittedFrom = new(2026, 10, 15), SubmittedTo = new(2026, 10, 15) });
        var byAmount = await f.Service.GetListAsync(f.ManagerAccount.AccountId, new() { SortBy = "requestedCompensationAmount:asc" });
        var paged = await f.Service.GetListAsync(f.ManagerAccount.AccountId, new() { PageSize = 1, PageIndex = 2 });

        Assert.Equal(late.ClaimId, Assert.Single(byType.Items).ClaimId);
        Assert.Equal(injury.ClaimId, Assert.Single(byStatus.Items).ClaimId);
        Assert.Equal(2, byBooking.TotalItems); Assert.Equal(injury.ClaimId, Assert.Single(byCode.Items).ClaimId); Assert.Equal(2, byDay.TotalItems);
        Assert.Equal(new[] { late.ClaimId, injury.ClaimId }, byAmount.Items.Select(item => item.ClaimId));
        Assert.Equal(2, paged.TotalItems); Assert.Equal(2, paged.TotalPages); Assert.Single(paged.Items); Assert.Equal(injury.ClaimId, paged.Items[0].ClaimId);
    }

    [Theory]
    [InlineData("page", "pageIndex")]
    [InlineData("size", "pageSize")]
    [InlineData("type", "claimType")]
    [InlineData("status", "status")]
    [InlineData("range", "submittedTo")]
    [InlineData("sort", "sortBy")]
    public async Task List_InvalidQuery_ReturnsFieldErrors(string problem, string field)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var request = new Claims.Request.ListRequest();
        switch (problem)
        {
            case "page": request.PageIndex = 0; break;
            case "size": request.PageSize = 101; break;
            case "type": request.ClaimType = "HORSE_INJURY,99"; break;
            case "status": request.Status = "DONE"; break;
            case "range": request.SubmittedFrom = new(2026, 10, 20); request.SubmittedTo = new(2026, 10, 10); break;
            case "sort": request.SortBy = "title:asc"; break;
        }

        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetListAsync(f.ManagerAccount.AccountId, request));

        Assert.Contains(error.Errors, item => item.Field == field);
    }

    // ---------------------------------------------------------------- CL4

    [Fact]
    public async Task Get_ACustomerGetsTheBasicViewOfTheirOwnClaimOnly()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.SubmitAsync();

        var result = await f.Service.GetAsync(f.CustomerAccount.AccountId, claim.ClaimId);

        Assert.IsNotType<Claims.Response.ManagerClaimResponse>(result); Assert.Equal(["CANCEL"], result.AvailableActions); Assert.Null(result.ReviewedAt);
        Assert.IsNotType<Claims.Response.ManagerHandover>(result.Handover);
        await ThrowsAsync<NotFoundException>(f.Service.GetAsync(f.StrangerAccount.AccountId, claim.ClaimId), "CLAIM_NOT_FOUND");
        await ThrowsAsync<NotFoundException>(f.Service.GetAsync(f.ManagerAccount.AccountId, Guid.NewGuid()), "CLAIM_NOT_FOUND");
    }

    [Fact]
    public async Task Get_AManagerSeesTheCrossCheckBlocks_AndTheFirstOpenRecordsTheReviewOnce()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.SubmitAsync();
        f.Clock.Value = Now.AddMinutes(30);

        var first = (Claims.Response.ManagerClaimResponse)await f.Service.GetAsync(f.ManagerAccount.AccountId, claim.ClaimId);
        f.Clock.Value = Now.AddMinutes(45);
        var second = await f.Service.GetAsync(f.OtherManagerAccount.AccountId, claim.ClaimId);

        Assert.Equal(Now.AddMinutes(30), first.ReviewedAt); Assert.Equal(first.ReviewedAt, first.UpdatedAt);
        Assert.Equal(first.ReviewedAt, second.ReviewedAt); Assert.Equal(first.UpdatedAt, second.UpdatedAt);
        Assert.Equal(["RESOLVE", "CANCEL"], first.AvailableActions);
        var handover = Assert.IsType<Claims.Response.ManagerHandover>(first.Handover);
        Assert.Equal("Pegasus King has a small scratch", handover.HorseConditionNote); Assert.Equal("Scratch noted at delivery", handover.DiscrepancyNote);
        Assert.EndsWith("?signed=1", handover.SenderSignatureUrl); Assert.Equal("Pegasus Transport", handover.FromPartyName);
        Assert.Equal("All horses in good condition", first.PickupHandover!.HorseConditionNote);
        Assert.Equal(new Claims.Response.HealthLogSummary(0, 0, null), first.HealthLogSummary); Assert.Empty(first.Incidents);
        Assert.Equal("NO_FINAL_INVOICE", first.Billing.Warning); Assert.Null(first.Billing.FinalInvoice); Assert.Empty(first.Billing.BookingCreditNotes);
        Assert.False(first.Billing.CreditNote.Eligible); Assert.Equal("CLAIM_NOT_APPROVED", first.Billing.CreditNote.BlockedReason);
    }

    [Fact]
    public async Task Get_AManagerSeesTheHealthLogsAndIncidentsOfTheTransport()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.SubmitAsync();
        var horse = (await TestSupport.AddPendingBookingAsync(db, f.Customer, withHorse: true)).Horse!;
        db.HorseHealthLogs.AddRange(
            new HorseHealthLog { HorseHealthLogId = Guid.NewGuid(), TransportId = f.Transport.TransportId, HorseId = horse.HorseId, EmployeeId = f.Manager.EmployeeId,
                LogType = HorseHealthLogType.ROUTINE_CHECK, CheckedAt = new DateTime(2026, 10, 14, 9, 0, 0), HealthStatus = HorseHealthStatus.NORMAL, CreatedAt = NowDb, UpdatedAt = NowDb },
            new HorseHealthLog { HorseHealthLogId = Guid.NewGuid(), TransportId = f.Transport.TransportId, HorseId = horse.HorseId, EmployeeId = f.Manager.EmployeeId,
                LogType = HorseHealthLogType.ROUTINE_CHECK, CheckedAt = new DateTime(2026, 10, 14, 15, 30, 0), HealthStatus = HorseHealthStatus.AT_RISK, CreatedAt = NowDb, UpdatedAt = NowDb });
        await db.SaveChangesAsync();

        var result = (Claims.Response.ManagerClaimResponse)await f.Service.GetAsync(f.ManagerAccount.AccountId, claim.ClaimId);

        Assert.Equal(new Claims.Response.HealthLogSummary(2, 1, new DateTimeOffset(2026, 10, 14, 15, 30, 0, TimeSpan.Zero)), result.HealthLogSummary);
    }

    // ---------------------------------------------------------------- CL5

    [Fact]
    public async Task Resolve_Approval_RecordsTheOutcome_TellsTheCustomerWithoutInternalFields_AndLeavesTheClaimOpen()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.OpenedAsync();
        f.Clock.Value = Now.AddHours(1);
        var notificationsBefore = await db.Notifications.CountAsync();

        var result = (Claims.Response.ManagerClaimResponse)await f.ResolveAsync(claim);

        Assert.Equal("APPROVED", result.Status); Assert.Null(result.ResolvedAt); Assert.Equal(Now.AddHours(1), result.UpdatedAt);
        var resolution = Assert.IsType<Claims.Response.ManagerResolution>(result.Resolution);
        Assert.Equal("PARTIAL_COMPENSATION", resolution.ResolutionType); Assert.Equal(18_000_000, resolution.ApprovedCompensationAmount); Assert.Equal("VND", resolution.Currency);
        Assert.Equal("Review the box padding.", resolution.CorrectiveAction); Assert.Equal("Agreed by phone.", resolution.InternalNote);
        Assert.Equal(["COMPLETE"], result.AvailableActions); Assert.Equal("NO_FINAL_INVOICE", result.Billing.Warning);

        var notification = Assert.Single(await db.Notifications.Skip(notificationsBefore).ToListAsync());
        Assert.Equal("Claim approved", notification.Title); Assert.Equal(NotificationPriority.NORMAL, notification.Priority); Assert.DoesNotContain("Agreed by phone", notification.Message);
        Assert.Equal(f.CustomerAccount.AccountId, (await db.AccountNotifications.SingleAsync(item => item.NotificationId == notification.NotificationId)).AccountId);
        var emails = (await db.OutboxMessages.ToListAsync()).Select(item => OutboxQueue.Deserialize<EmailPayload>(item.Payload)).ToList();
        var email = Assert.Single(emails, item => item.Subject.StartsWith("Claim approved"));
        Assert.Contains("18,000,000", email.Body); Assert.DoesNotContain("Agreed by phone", email.Body); Assert.DoesNotContain("box padding", email.Body);

        var customerView = await f.Service.GetAsync(f.CustomerAccount.AccountId, claim.ClaimId);
        Assert.Equal(typeof(Claims.Response.ResolutionBrief), customerView.Resolution!.GetType());
        Assert.Empty(customerView.AvailableActions);
    }

    [Fact]
    public async Task Resolve_Rejection_ClosesTheClaimAndSetsResolvedAt()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.SubmitAsync(); // never opened: resolving also counts as the review
        f.Clock.Value = Now.AddHours(2);

        var result = await f.ResolveAsync(claim, "NO_COMPENSATION", null);

        Assert.Equal("REJECTED", result.Status); Assert.Equal(Now.AddHours(2), result.ResolvedAt); Assert.Equal(Now.AddHours(2), result.ReviewedAt);
        Assert.Equal(0, result.Resolution!.ApprovedCompensationAmount); Assert.Empty(result.AvailableActions);
        Assert.Contains(await db.Notifications.ToListAsync(), item => item.Title == "Claim rejected");
    }

    [Theory]
    [InlineData("COMPENSATION", null)]
    [InlineData("COMPENSATION", 0)]
    [InlineData("PARTIAL_COMPENSATION", null)]
    [InlineData("REFUND", 0)]
    [InlineData("NO_COMPENSATION", 5)]
    [InlineData("SERVICE_RECOVERY", 5)]
    [InlineData("COMPENSATION", -1)]
    public async Task Resolve_TheAmountMustFitTheKindOfResolution(string type, int? amount)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.OpenedAsync();

        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.ResolveAsync(claim, type, amount));

        Assert.Contains(error.Errors, item => item.Field == "approvedCompensationAmount"); Assert.Empty(db.ClaimResolutions);
    }

    [Fact]
    public async Task Resolve_RejectsAFractionalAmount()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.OpenedAsync();

        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.ResolveAsync(claim, "COMPENSATION", 100.5m));

        Assert.Contains(error.Errors, item => item.Message == "Amount must be a whole number of VND");
    }

    [Theory]
    [InlineData("SERVICE_RECOVERY", null, 0)]
    [InlineData("OTHER", null, null)]
    [InlineData("OTHER", 0, 0)]
    [InlineData("REFUND", 7, 7)]
    public async Task Resolve_StoresTheAmountTheKindAllows(string type, int? amount, int? stored)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.OpenedAsync();

        var result = await f.ResolveAsync(claim, type, amount);

        Assert.Equal((long?)stored, result.Resolution!.ApprovedCompensationAmount); Assert.Equal("APPROVED", result.Status);
    }

    [Fact]
    public async Task Resolve_RefusesASecondOutcome_AStaleVersion_ANonPendingClaim_AndNonManagers()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.OpenedAsync();
        var stale = claim.UpdatedAt.AddSeconds(-1);
        await ThrowsAsync<ConflictException>(f.Service.ResolveAsync(f.ManagerAccount.AccountId, claim.ClaimId, new()
            { LastUpdatedAt = stale, ResolutionType = "REFUND", ApprovedCompensationAmount = 5, ResolutionSummary = "Refund of the transport fee." }), "CLAIM_MODIFIED");

        await f.ResolveAsync(claim);
        await ThrowsAsync<ConflictException>(f.ResolveAsync(claim), "CLAIM_ALREADY_RESOLVED");

        var cancelled = await f.SubmitAsync();
        await f.Service.CancelAsync(f.ManagerAccount.AccountId, cancelled.ClaimId, new() { LastUpdatedAt = cancelled.UpdatedAt, CancellationReason = "Raised twice by mistake" });
        var error = await ThrowsAsync<ConflictException>(f.ResolveAsync(cancelled), "INVALID_CLAIM_STATUS");
        Assert.Contains("CANCELLED", error.Message);

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ResolveAsync(f.CustomerAccount.AccountId, claim.ClaimId, new()));
        await ThrowsAsync<NotFoundException>(f.Service.ResolveAsync(f.ManagerAccount.AccountId, Guid.NewGuid(), new()
            { LastUpdatedAt = Now, ResolutionType = "REFUND", ApprovedCompensationAmount = 5, ResolutionSummary = "Refund of the transport fee." }), "CLAIM_NOT_FOUND");
        Assert.Single(db.ClaimResolutions);
    }

    [Fact]
    public async Task Resolve_InvalidInput_ReturnsFieldErrors()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.OpenedAsync();

        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.ResolveAsync(f.ManagerAccount.AccountId, claim.ClaimId,
            new() { ResolutionType = "SOMETHING", ResolutionSummary = "short" }));

        Assert.Equal(new[] { "lastUpdatedAt", "resolutionType", "resolutionSummary" }.Order(), error.Errors.Select(item => item.Field).Distinct().Order());
    }

    // ---------------------------------------------------------------- CL6

    [Fact]
    public async Task Cancel_ByTheCustomer_NotifiesTheManagers()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.SubmitAsync();
        var before = await db.Notifications.CountAsync();
        f.Clock.Value = Now.AddHours(1);

        var result = await f.Service.CancelAsync(f.CustomerAccount.AccountId, claim.ClaimId, new() { LastUpdatedAt = claim.UpdatedAt, CancellationReason = "  The horse recovered  " });

        Assert.Equal("CANCELLED", result.Status); Assert.Equal(Now.AddHours(1), result.CancelledAt); Assert.Equal("The horse recovered", result.CancellationReason);
        Assert.Empty(result.AvailableActions); Assert.IsNotType<Claims.Response.ManagerClaimResponse>(result);
        var notifications = (await db.Notifications.ToListAsync()).Skip(before).ToList();
        Assert.Equal(2, notifications.Count); Assert.All(notifications, item => Assert.Equal("Claim cancelled by customer", item.Title));
        Assert.Equal(new[] { f.ManagerAccount.AccountId, f.OtherManagerAccount.AccountId }.Order(),
            (await db.AccountNotifications.Where(item => notifications.Select(n => n.NotificationId).Contains(item.NotificationId)).Select(item => item.AccountId).ToArrayAsync()).Order());
    }

    [Fact]
    public async Task Cancel_ByAManager_NotifiesTheCustomer_WithTheManagerView()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.SubmitAsync();
        var before = await db.Notifications.CountAsync();

        var result = await f.Service.CancelAsync(f.ManagerAccount.AccountId, claim.ClaimId, new() { LastUpdatedAt = claim.UpdatedAt, CancellationReason = "The customer withdrew by phone" });

        Assert.IsType<Claims.Response.ManagerClaimResponse>(result);
        var notification = Assert.Single((await db.Notifications.ToListAsync()).Skip(before));
        Assert.Equal("Claim cancelled", notification.Title);
        Assert.Equal(f.CustomerAccount.AccountId, (await db.AccountNotifications.SingleAsync(item => item.NotificationId == notification.NotificationId)).AccountId);
        Assert.Equal($"/bookings/{f.Booking.BookingId}?claimId={claim.ClaimId}", notification.ActionUrl);
    }

    [Fact]
    public async Task Cancel_RefusesOtherCustomers_ShortReasons_StaleVersions_AndAClaimThatIsNoLongerPending()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var claim = await f.SubmitAsync();
        await ThrowsAsync<NotFoundException>(f.Service.CancelAsync(f.StrangerAccount.AccountId, claim.ClaimId,
            new() { LastUpdatedAt = claim.UpdatedAt, CancellationReason = "I am not the owner" }), "CLAIM_NOT_FOUND");
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.CancelAsync(f.CustomerAccount.AccountId, claim.ClaimId,
            new() { LastUpdatedAt = claim.UpdatedAt, CancellationReason = "short" }));
        Assert.Contains(error.Errors, item => item.Field == "cancellationReason");
        await ThrowsAsync<ConflictException>(f.Service.CancelAsync(f.CustomerAccount.AccountId, claim.ClaimId,
            new() { LastUpdatedAt = claim.UpdatedAt.AddSeconds(-5), CancellationReason = "A stale screen" }), "CLAIM_MODIFIED");

        var opened = await f.Service.GetAsync(f.ManagerAccount.AccountId, claim.ClaimId);
        await f.ResolveAsync(opened);
        var approved = await f.Service.GetAsync(f.ManagerAccount.AccountId, claim.ClaimId);
        await ThrowsAsync<ConflictException>(f.Service.CancelAsync(f.CustomerAccount.AccountId, claim.ClaimId,
            new() { LastUpdatedAt = approved.UpdatedAt, CancellationReason = "Too late to cancel" }), "INVALID_CLAIM_STATUS");
    }

    // ---------------------------------------------------------------- CL7

    [Fact]
    public async Task Complete_ClosesAnApprovedClaim_AndTellsTheCustomerByWebAndEmail()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var approved = await f.ResolveAsync(await f.OpenedAsync());
        var before = await db.Notifications.CountAsync();
        f.Clock.Value = Now.AddDays(1);

        var result = await f.Service.CompleteAsync(f.ManagerAccount.AccountId, approved.ClaimId, new() { LastUpdatedAt = approved.UpdatedAt });

        Assert.Equal("RESOLVED", result.Status); Assert.Equal(Now.AddDays(1), result.ResolvedAt); Assert.Empty(result.AvailableActions);
        Assert.Equal(approved.Resolution!.ClaimResolutionId, result.Resolution!.ClaimResolutionId);
        var notification = Assert.Single((await db.Notifications.ToListAsync()).Skip(before));
        Assert.Equal("Claim compensation completed", notification.Title);
        Assert.Contains((await db.OutboxMessages.ToListAsync()).Select(item => OutboxQueue.Deserialize<EmailPayload>(item.Payload)), item => item.Subject.StartsWith("Claim closed"));
    }

    [Fact]
    public async Task Complete_NeedsAnApprovedClaim_ACurrentVersion_AndAManager()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var pending = await f.OpenedAsync();
        await ThrowsAsync<ConflictException>(f.Service.CompleteAsync(f.ManagerAccount.AccountId, pending.ClaimId, new() { LastUpdatedAt = pending.UpdatedAt }), "INVALID_CLAIM_STATUS");

        var approved = await f.ResolveAsync(pending);
        await ThrowsAsync<ConflictException>(f.Service.CompleteAsync(f.ManagerAccount.AccountId, approved.ClaimId, new() { LastUpdatedAt = approved.UpdatedAt.AddSeconds(-1) }), "CLAIM_MODIFIED");
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CompleteAsync(f.CustomerAccount.AccountId, approved.ClaimId, new() { LastUpdatedAt = approved.UpdatedAt }));
        await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.CompleteAsync(f.ManagerAccount.AccountId, approved.ClaimId, new()));
        await f.Service.CompleteAsync(f.ManagerAccount.AccountId, approved.ClaimId, new() { LastUpdatedAt = approved.UpdatedAt });
        await ThrowsAsync<ConflictException>(f.Service.CompleteAsync(f.ManagerAccount.AccountId, approved.ClaimId, new() { LastUpdatedAt = approved.UpdatedAt }), "INVALID_CLAIM_STATUS");
    }

    // ---------------------------------------------------------------- R22 billing

    [Theory]
    [InlineData(null, "PARTIAL_COMPENSATION", 18_000_000, "NO_FINAL_INVOICE", false, "FINAL_NOT_PAID")]
    [InlineData(InvoiceStatus.ISSUED, "PARTIAL_COMPENSATION", 18_000_000, "FINAL_NOT_PAID", false, "FINAL_NOT_PAID")]
    [InlineData(InvoiceStatus.PAID, "PARTIAL_COMPENSATION", 18_000_000, null, true, null)]
    [InlineData(InvoiceStatus.PAID, "SERVICE_RECOVERY", null, null, false, "NO_COMPENSATION_APPROVED")]
    [InlineData(InvoiceStatus.PAID, "OTHER", null, null, false, "NO_COMPENSATION_APPROVED")]
    public async Task Billing_ShowsTheFinalInvoiceAndWhetherACreditNoteMayBeCreated(InvoiceStatus? finalStatus, string type, int? amount, string? warning, bool eligible, string? blocked)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        if (finalStatus is { } status) f.AddInvoice(InvoiceType.FINAL, status, 151_800_000);
        f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.CANCELLED); // a cancelled final never counts
        await db.SaveChangesAsync();
        var claim = await f.OpenedAsync();

        var result = (Claims.Response.ManagerClaimResponse)await f.ResolveAsync(claim, type, amount);

        Assert.Equal(warning, result.Billing.Warning); Assert.Equal(eligible, result.Billing.CreditNote.Eligible); Assert.Equal(blocked, result.Billing.CreditNote.BlockedReason);
        Assert.Equal(finalStatus is null ? null : (long?)151_800_000, result.Billing.FinalInvoice?.TotalAmount);
        Assert.Equal(eligible, result.AvailableActions.Contains("CREATE_CREDIT_NOTE"));
        if (eligible)
        {
            Assert.Equal(new Claims.Response.SuggestedItem("OTHER", $"Compensation for claim {result.ClaimCode}", 1, 18_000_000), result.Billing.CreditNote.SuggestedItem);
            Assert.Equal(18_000_000, result.Billing.CreditNote.ApprovedAmount);
        }
        else Assert.Null(result.Billing.CreditNote.SuggestedItem);
    }

    [Fact]
    public async Task Billing_ListsTheCreditNotesOfTheBookingThatAreNotCancelled()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.PAID); var credit = f.AddInvoice(InvoiceType.CREDIT_NOTE, InvoiceStatus.ISSUED, 18_000_000); f.AddInvoice(InvoiceType.CREDIT_NOTE, InvoiceStatus.CANCELLED);
        await db.SaveChangesAsync();
        var claim = await f.OpenedAsync();

        var result = (Claims.Response.ManagerClaimResponse)await f.Service.GetAsync(f.ManagerAccount.AccountId, claim.ClaimId);

        var note = Assert.Single(result.Billing.BookingCreditNotes);
        Assert.Equal(credit.InvoiceId, note.InvoiceId); Assert.Equal(18_000_000, note.TotalAmount); Assert.Equal("ISSUED", note.Status);
    }
}
