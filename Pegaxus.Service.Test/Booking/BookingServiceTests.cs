using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using BookingRequest = Pegaxus.Service.BookingService.Request;
using BookingServiceImpl = Pegaxus.Service.BookingService.Service;
using TransportPlanServiceImpl = Pegaxus.Service.TransportPlanService.Service;
using TransportPlanRequest = Pegaxus.Service.TransportPlanService.Request;

namespace Pegaxus.Service.Test;

public class BookingServiceTests
{
    [Fact]
    public async Task Create_PersistsBookingHorseAndHistory()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var horse = new Horse
        {
            HorseId = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Customer = customer,
            HorseName = "Silver Arrow",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Horses.Add(horse);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.CreateAsync(account.AccountId, new BookingRequest.CreateBookingRequest
        {
            OriginAddress = "Paris",
            OriginCountry = "France",
            DestinationAddress = "Berlin",
            DestinationCountry = "Germany",
            RequestedDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(21),
            Horses = [new BookingRequest.BookingHorseRequest { HorseId = horse.HorseId }]
        });

        Assert.Equal(result.BookingId, (await db.Bookings.SingleAsync()).BookingId);
        Assert.Equal(horse.HorseId, (await db.BookingHorses.SingleAsync()).HorseId);
        Assert.Equal(BookingStatus.PENDING, (await db.BookingStatusHistories.SingleAsync()).NewStatus);
    }

    [Fact]
    public async Task Update_ChangesOnlyProvidedFields()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);
        await service.ChangeStatusAsync(account.AccountId, booking.BookingId, new BookingRequest.ChangeStatusRequest
        {
            Status = "DRAFT"
        });

        var result = await service.UpdateAsync(account.AccountId, booking.BookingId, new BookingRequest.UpdateBookingRequest
        {
            LastUpdatedAt = booking.UpdatedAt,
            OriginAddress = "New origin"
        });

        Assert.Equal("New origin", booking.OriginAddress);
        Assert.Equal("Old destination", booking.DestinationAddress);
        Assert.Equal(result.UpdatedAt.UtcDateTime, DateTime.SpecifyKind(booking.UpdatedAt, DateTimeKind.Utc));
        Assert.Contains(
            "originAddress",
            (await db.BookingStatusHistories.OrderBy(history => history.ChangedAt).LastAsync()).Reason);
    }

    [Fact]
    public async Task Update_RejectsStaleVersion()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);
        await service.ChangeStatusAsync(account.AccountId, booking.BookingId, new BookingRequest.ChangeStatusRequest
        {
            Status = "DRAFT"
        });

        var exception = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(
            account.AccountId,
            booking.BookingId,
            new BookingRequest.UpdateBookingRequest
            {
                LastUpdatedAt = booking.UpdatedAt.AddSeconds(-1),
                OriginAddress = "New origin"
            }));

        Assert.Equal("BOOKING_MODIFIED", exception.ErrorCode);
    }

    [Fact]
    public async Task Cancel_SetsStatusReasonAndHistory()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);

        await service.CancelAsync(
            account.AccountId,
            booking.BookingId,
            new BookingRequest.CancelBookingRequest { Reason = "Event cancelled" });

        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal("Event cancelled", booking.CancellationReason);
        Assert.Equal(BookingStatus.CANCELLED, (await db.BookingStatusHistories.SingleAsync()).NewStatus);
    }

    [Fact]
    public async Task Approve_CreatesTransportNotificationAndEmail()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        var service = CreateService(db, out var mailService);

        var result = await service.ApproveAsync(managerAccount.AccountId, booking.BookingId);

        Assert.Equal(BookingStatus.APPROVED, booking.Status);
        Assert.Equal(TransportStatus.CREATED, result.Transport.Status);
        Assert.Equal(result.Transport.TransportId, (await db.Transports.SingleAsync()).TransportId);
        Assert.Equal(result.Transport.TransportId, (await db.Notifications.SingleAsync()).TransportId);
        Assert.Equal(1, mailService.Calls);
    }

    [Fact]
    public async Task Approve_RejectsBookingWithoutHorse()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);

        var exception = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            service.ApproveAsync(managerAccount.AccountId, booking.BookingId));

        Assert.Equal("BOOKING_HAS_NO_HORSE", exception.ErrorCode);
    }

    [Fact]
    public async Task GetApprovedList_ReturnsApprovedBookingAndAppliesContractFilters()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        booking.Status = BookingStatus.APPROVED;
        booking.ApprovedAt = now;
        booking.SpecialRequirements = "Temperature monitoring";
        var transport = new Transport
        {
            TransportId = Guid.NewGuid(),
            TransportCode = "TR-001",
            BookingId = booking.BookingId,
            Booking = booking,
            CreatedAt = now,
            UpdatedAt = now
        };
        var transportPlan = new TransportPlan
        {
            TransportPlanId = Guid.NewGuid(),
            TransportId = transport.TransportId,
            Transport = transport,
            PlanCode = "TP-001",
            Status = TransportPlanStatus.APPROVED,
            PlannedStartAt = now.AddDays(1),
            PlannedEndAt = now.AddDays(2),
            EstimatedDistanceKm = 920.5m,
            EstimatedDurationMinutes = 1260,
            PlanningNote = "Temperature monitoring",
            CreatedAt = now,
            UpdatedAt = now
        };
        var routePlan = new RoutePlan
        {
            RoutePlanId = Guid.NewGuid(),
            TransportPlanId = transportPlan.TransportPlanId,
            TransportPlan = transportPlan,
            RoutePlanCode = "RP-001",
            Status = RoutePlanStatus.DRAFT,
            CreatedAt = now,
            UpdatedAt = now
        };
        routePlan.RoutePlanRoutes.Add(new RoutePlanRoute
        {
            RoutePlanRouteId = Guid.NewGuid(),
            RoutePlanId = routePlan.RoutePlanId,
            RoutePlan = routePlan,
            RouteId = Guid.NewGuid(),
            RouteType = RouteType.PRIMARY,
            SequenceNo = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        transportPlan.RoutePlans.Add(routePlan);
        transport.TransportPlans.Add(transportPlan);
        booking.Transport = transport;
        db.Transports.Add(transport);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetApprovedListAsync(
            managerAccount.AccountId,
            new BookingRequest.ApprovedBookingListRequest
            {
                PageSize = 20,
                IsInternational = true,
                TransportPlanStatus = "APPROVED",
                RoutePlanStatus = "DRAFT",
                HasComplianceDossier = false,
                SortBy = "requestedDepartureDate:asc"
            });

        var item = Assert.Single(result.Items);
        Assert.Equal(BookingStatus.APPROVED.ToString(), item.Status);
        Assert.True(item.IsInternational);
        Assert.Equal(transport.TransportId, item.TransportId);
        Assert.Equal("TP-001", item.TransportPlan!.PlanCode);
        Assert.Equal("RP-001", item.RoutePlan!.RoutePlanCode);
        Assert.Equal("Temperature monitoring", item.SpecialRequirements);
    }

    [Fact]
    public async Task GetApprovedList_RejectsInvalidSort()
    {
        await using var db = TestSupport.CreateDb();
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateService(db).GetApprovedListAsync(
                managerAccount.AccountId,
                new BookingRequest.ApprovedBookingListRequest { SortBy = "status:asc" }));

        Assert.Equal("INVALID_SORT_PARAMETER", exception.ErrorCode);
        Assert.Equal("The sort parameter is invalid", exception.Message);
    }

    [Fact]
    public async Task GetApproved_ReturnsBookingDetailAndNullPlanningArtifactsWhenPlanDoesNotExist()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        booking.Status = BookingStatus.APPROVED;
        booking.ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetApprovedAsync(managerAccount.AccountId, booking.BookingId);

        Assert.Equal(booking.BookingId, result.BookingId);
        Assert.Equal(BookingStatus.APPROVED.ToString(), result.Status);
        Assert.Equal(customer.FullName, result.Customer.FullName);
        Assert.Equal(horse!.HorseId, Assert.Single(result.Horses).HorseId);
        Assert.Null(result.TransportPlan);
        Assert.Null(result.ComplianceDossier);
        Assert.Empty(result.RoutePlans);
    }

    [Fact]
    public async Task GetApproved_RejectsNonApprovedBooking()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(db).GetApprovedAsync(managerAccount.AccountId, booking.BookingId));

        Assert.Equal("INVALID_BOOKING_STATUS", exception.ErrorCode);
        Assert.Contains("Pending", exception.Message);
    }

    [Fact]
    public async Task AssignmentApi_UsesEmployeeTransportPlanForAssignListAndRevoke()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        booking.Status = BookingStatus.APPROVED;
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var transport = new Transport
        {
            TransportId = Guid.NewGuid(),
            TransportCode = "TR-ASSIGN",
            BookingId = booking.BookingId,
            Booking = booking,
            CreatedAt = now,
            UpdatedAt = now
        };
        var plan = new TransportPlan
        {
            TransportPlanId = Guid.NewGuid(),
            TransportId = transport.TransportId,
            Transport = transport,
            PlanCode = "TP-ASSIGN",
            Status = TransportPlanStatus.APPROVED,
            CreatedAt = now,
            UpdatedAt = now
        };
        transport.TransportPlans.Add(plan);
        booking.Transport = transport;

        var employeeAccount = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = "specialist@example.com",
            PasswordHash = "hash",
            AccountType = AccountType.EMPLOYEE,
            Status = AccountStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        var specialist = new Employee
        {
            EmployeeId = Guid.NewGuid(),
            AccountId = employeeAccount.AccountId,
            Account = employeeAccount,
            EmployeeCode = "TS-001",
            FullName = "Transport Specialist",
            Role = EmployeeRole.TRANSPORT_SPECIALIST,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        employeeAccount.EmployeeAccount = specialist;
        db.AddRange(transport, employeeAccount, specialist);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var assigned = await service.AssignEmployeeAsync(
            managerAccount.AccountId,
            booking.BookingId,
            new BookingRequest.AssignBookingEmployeeRequest
            {
                EmployeeId = specialist.EmployeeId,
                Role = EmployeeRole.TRANSPORT_SPECIALIST.ToString(),
                Note = "Compliance owner"
            });

        Assert.Equal(plan.TransportPlanId, assigned.TransportPlanId);
        Assert.Equal(specialist.EmployeeId, assigned.EmployeeId);
        Assert.Single(await db.EmployeeTransportPlans.ToListAsync());

        var listed = await service.GetAssignmentsAsync(managerAccount.AccountId, booking.BookingId);
        Assert.Equal(specialist.EmployeeId, Assert.Single(listed).EmployeeId);

        await service.RevokeAssignmentAsync(managerAccount.AccountId, booking.BookingId, specialist.EmployeeId);
        Assert.Empty(await db.EmployeeTransportPlans.ToListAsync());
    }

    [Fact]
    public async Task CreateTransportPlan_CreatesDraftAndMovesTransportToPlanning()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        var service = CreateService(db);
        await service.ApproveAsync(managerAccount.AccountId, booking.BookingId);
        var transportPlanService = CreateTransportPlanService(db);

        var start = DateTimeOffset.UtcNow.AddDays(1);
        var result = await transportPlanService.CreateAsync(
            managerAccount.AccountId,
            booking.BookingId,
            new TransportPlanRequest.CreateRequest
            {
                PlannedStartAt = start,
                PlannedEndAt = start.AddDays(2),
                EstimatedDistanceKm = 920.5m,
                EstimatedDurationMinutes = 1260,
                PlanningNote = "Temperature monitoring"
            });

        Assert.StartsWith("TP-", result.PlanCode);
        Assert.Equal(TransportPlanStatus.DRAFT.ToString(), result.Status);
        Assert.Equal(TransportStatus.PLANNING.ToString(), result.Transport.Status);
        Assert.Empty(result.AssignedEmployees);
        Assert.Single(await db.TransportPlans.ToListAsync());
    }

    [Fact]
    public async Task CreateTransportPlan_RejectsDuplicateAndInvalidSchedule()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        var service = CreateService(db);
        await service.ApproveAsync(managerAccount.AccountId, booking.BookingId);
        var transportPlanService = CreateTransportPlanService(db);

        var start = DateTimeOffset.UtcNow.AddDays(1);
        var request = new TransportPlanRequest.CreateRequest
        {
            PlannedStartAt = start,
            PlannedEndAt = start.AddDays(1)
        };
        await transportPlanService.CreateAsync(managerAccount.AccountId, booking.BookingId, request);

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            transportPlanService.CreateAsync(managerAccount.AccountId, booking.BookingId, request));
        Assert.Equal("TRANSPORT_PLAN_EXISTS", duplicate.ErrorCode);

        await using var secondDb = TestSupport.CreateDb();
        var (_, secondCustomer) = await TestSupport.AddCustomerAsync(secondDb);
        var (secondManager, _) = await TestSupport.AddLogisticsManagerAsync(secondDb);
        var (secondBooking, _) = await TestSupport.AddPendingBookingAsync(secondDb, secondCustomer, withHorse: true);
        await CreateService(secondDb).ApproveAsync(secondManager.AccountId, secondBooking.BookingId);
        var invalid = await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateTransportPlanService(secondDb).CreateAsync(
                secondManager.AccountId,
                secondBooking.BookingId,
                new TransportPlanRequest.CreateRequest
                {
                    PlannedStartAt = start,
                    PlannedEndAt = start
                }));
        Assert.Equal("VALIDATION_ERROR", invalid.ErrorCode);
    }

    [Fact]
    public async Task Submit_RejectsBookingWithoutHorse()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);
        await service.ChangeStatusAsync(account.AccountId, booking.BookingId, new BookingRequest.ChangeStatusRequest { Status = "DRAFT" });

        var exception = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            service.ChangeStatusAsync(account.AccountId, booking.BookingId, new BookingRequest.ChangeStatusRequest { Status = "PENDING" }));

        Assert.Equal("BOOKING_HAS_NO_HORSE", exception.ErrorCode);
    }

    [Fact]
    public async Task Reject_SetsReasonNotificationAndEmail()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db, out var mailService);

        await service.RejectAsync(
            managerAccount.AccountId,
            booking.BookingId,
            new BookingRequest.RejectBookingRequest { Reason = "No suitable transport" });

        Assert.Equal(BookingStatus.REJECTED, booking.Status);
        Assert.Equal("No suitable transport", booking.RejectionReason);
        Assert.Null((await db.Notifications.SingleAsync()).TransportId);
        Assert.Equal(1, mailService.Calls);
    }

    private static BookingServiceImpl CreateService(AppDbContext db) =>
        new(db, TestSupport.CreateMailService());

    private static TransportPlanServiceImpl CreateTransportPlanService(AppDbContext db) =>
        new(db);

    private static BookingServiceImpl CreateService(AppDbContext db, out TestSupport.RecordingMailService mailService)
    {
        mailService = TestSupport.CreateMailService();
        return new BookingServiceImpl(db, mailService);
    }
}
