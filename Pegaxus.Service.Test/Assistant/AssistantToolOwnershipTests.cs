using System.Reflection;
using System.Text.Json;
using Pegaxus.Service.Assistant;
using Pegaxus.Service.Assistant.Tools;
using Pegaxus.Service.Exceptions;

using BookingRequest = Pegaxus.Service.BookingService.Request;
using BookingResponse = Pegaxus.Service.BookingService.Response;
using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;
using DossierResponse = Pegaxus.Service.ComplianceDossierService.Response;
using HealthResponse = Pegaxus.Service.HorseHealthService.Response;
using TrackingResponse = Pegaxus.Service.LocationTrackingService.Response;

namespace Pegaxus.Service.Test.Assistant;

public class AssistantToolOwnershipTests
{
    [Fact]
    public void Registry_ContainsOnlyTheFiveCustomerReadOnlyTools()
    {
        var tools = new IAssistantTool[]
        {
            Stub("customer_list_my_bookings"),
            Stub("customer_get_booking"),
            Stub("customer_get_booking_history"),
            Stub("customer_get_dossier"),
            Stub("customer_get_tracking")
        };
        var registry = new AssistantToolRegistry(tools);

        Assert.Equal(tools.Select(tool => tool.Name), registry.Tools.Select(tool => tool.Name));
        Assert.False(registry.TryGet("customer_cancel_booking", out _));
        Assert.Throws<BadRequestException>(() => registry.GetRequired("customer_cancel_booking"));
        Assert.Throws<InvalidOperationException>(() => new AssistantToolRegistry(tools.Take(4)));
    }

    [Fact]
    public async Task BookingList_UsesAuthenticatedAccountAndProjectsMinimalFields()
    {
        var accountId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var bookingId = Guid.NewGuid();
        var service = ServiceProxy<Pegaxus.Service.BookingService.IBookingService>.Create((method, args) =>
        {
            Assert.Equal("GetListAsync", method.Name);
            Assert.Equal(accountId, args[0]);
            Assert.Equal(cancellation.Token, args[2]);
            var request = Assert.IsType<BookingRequest.BookingListRequest>(args[1]);
            Assert.Equal(1, request.PageIndex);
            Assert.Equal(10, request.PageSize);
            return Task.FromResult(new BookingResponse.BookingListResponse
            {
                Items = [new BookingResponse.BookingListItemResponse
                {
                    BookingId = bookingId, BookingCode = "BK-1", Status = "APPROVED", StatusName = "Approved",
                    CustomerName = "Private customer", OriginAddress = "Private origin", OriginCountry = "France",
                    DestinationAddress = "Private destination", DestinationCountry = "Germany",
                    RequestedDepartureDate = new DateOnly(2026, 12, 1), TotalHorses = 1, CreatedAt = DateTimeOffset.UtcNow
                }],
                PageIndex = 1, PageSize = 10, TotalItems = 1, TotalPages = 1
            });
        });

        var tool = new CustomerListMyBookingsTool(service);
        var result = await tool.RunAsync(Args("{}"), new AssistantToolContext(accountId, cancellation.Token));

        var data = Assert.IsType<BookingListToolData>(result.Data);
        Assert.Single(data.Items);
        Assert.Equal(bookingId, data.Items[0].BookingId);
        Assert.DoesNotContain("Private customer", JsonSerializer.Serialize(data));
        Assert.DoesNotContain("Private origin", JsonSerializer.Serialize(data));
        Assert.Equal("booking", Assert.Single(result.References).Type);
        await Assert.ThrowsAsync<BadRequestException>(() => tool.RunAsync(
            Args("{\"pageIndex\":1,\"unapprovedFilter\":\"ignored\"}"),
            new AssistantToolContext(accountId, cancellation.Token)));
    }

    [Fact]
    public async Task BookingTools_CannotReadAnotherCustomersBookingById()
    {
        await using var db = TestSupport.CreateDb();
        var (owner, ownerCustomer) = await TestSupport.AddCustomerAsync(db);
        var (otherCustomer, _) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, ownerCustomer);
        var bookings = new Pegaxus.Service.BookingService.Service(
            db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));
        var context = new AssistantToolContext(otherCustomer.AccountId, CancellationToken.None);

        var detail = new CustomerGetBookingTool(bookings);
        var history = new CustomerGetBookingHistoryTool(bookings);
        await Assert.ThrowsAsync<NotFoundException>(() => detail.RunAsync(
            Args($"{{\"bookingId\":\"{booking.BookingId}\"}}"), context));
        await Assert.ThrowsAsync<NotFoundException>(() => history.RunAsync(
            Args($"{{\"bookingId\":\"{booking.BookingId}\"}}"), context));

        var list = await new CustomerListMyBookingsTool(bookings).RunAsync(Args("{}"), context);
        Assert.Empty(Assert.IsType<BookingListToolData>(list.Data).Items);
        Assert.NotEqual(owner.AccountId, otherCustomer.AccountId);
    }

    [Fact]
    public async Task DossierTool_ReadsEveryPageAndReturnsOnlyIncompleteRequirements()
    {
        var accountId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var pageCalls = new List<(Guid AccountId, int PageIndex, int PageSize, CancellationToken Token)>();
        var service = ServiceProxy<Pegaxus.Service.ComplianceDossierService.IComplianceDossierService>.Create((method, args) =>
        {
            if (method.Name == "GetByBookingAsync")
            {
                Assert.Equal(accountId, args[0]);
                Assert.Equal(bookingId, args[1]);
                Assert.Equal(CancellationToken.None, args[3]);
                return Task.FromResult(new DossierResponse.OverviewResponse
                {
                    IsInitialized = true, BookingId = bookingId, BookingCode = "BK-2",
                    ComplianceDossierId = dossierId, DossierCode = "DS-2", Status = "IN_PREPARATION",
                    RequirementCount = 2, FulfilledCount = 1, CompletionPercent = 50
                });
            }

            Assert.Equal("GetRequirementsAsync", method.Name);
            var request = Assert.IsType<DossierRequest.RequirementListRequest>(args[2]);
            pageCalls.Add(((Guid)args[0]!, request.PageIndex, request.PageSize, (CancellationToken)args[3]!));
            return Task.FromResult(request.PageIndex switch
            {
                1 => new DossierResponse.RequirementListResponse
                {
                    PageIndex = 1, PageSize = 100, TotalItems = 2, TotalPages = 2,
                    Items = [new DossierResponse.RequirementListItemResponse
                    {
                        DossierRequirementId = Guid.NewGuid(), RequirementName = "Health certificate",
                        Status = "PENDING", IsMandatory = true
                    }]
                },
                2 => new DossierResponse.RequirementListResponse
                {
                    PageIndex = 2, PageSize = 100, TotalItems = 2, TotalPages = 2,
                    Items = [new DossierResponse.RequirementListItemResponse
                    {
                        DossierRequirementId = Guid.NewGuid(), RequirementName = "Export permit",
                        Status = "SATISFIED", IsMandatory = true
                    }]
                },
                _ => throw new InvalidOperationException("Unexpected page request.")
            });
        });

        var tool = new CustomerGetDossierTool(service);
        var result = await tool.RunAsync(Args($"{{\"bookingId\":\"{bookingId}\"}}"), new AssistantToolContext(accountId, CancellationToken.None));

        var data = Assert.IsType<DossierToolData>(result.Data);
        Assert.True(data.RequirementsComplete);
        Assert.Equal(2, data.PagesRead);
        Assert.Equal(2, pageCalls.Count);
        Assert.All(pageCalls, call =>
        {
            Assert.Equal(accountId, call.AccountId);
            Assert.Equal(100, call.PageSize);
            Assert.Equal(CancellationToken.None, call.Token);
        });
        Assert.Equal(new[] { 1, 2 }, pageCalls.Select(call => call.PageIndex));
        Assert.Equal("Health certificate", Assert.Single(data.IncompleteRequirements).Name);
        Assert.Equal("dossier", Assert.Single(result.References).Type);
    }

    [Fact]
    public async Task TrackingTool_UsesBookingOwnershipAndReturnsGpsCurrentSegmentAndHorseHealth()
    {
        var accountId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var transportId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var bookingService = ServiceProxy<Pegaxus.Service.BookingService.IBookingService>.Create((method, args) =>
        {
            Assert.Equal("GetAsync", method.Name);
            Assert.Equal(accountId, args[0]);
            Assert.Equal(bookingId, args[1]);
            Assert.Equal(cancellation.Token, args[2]);
            return Task.FromResult(BookingDetail(bookingId, transportId));
        });
        var trackingService = ServiceProxy<Pegaxus.Service.LocationTrackingService.ILocationTrackingService>.Create((method, args) =>
        {
            Assert.Equal("GetAsync", method.Name);
            Assert.Equal(transportId, args[0]);
            Assert.Equal(accountId, args[1]);
            Assert.Equal(cancellation.Token, args[2]);
            return Task.FromResult(new TrackingResponse.Snapshot(DateTimeOffset.UtcNow, transportId, "IN_TRANSIT",
                new([new TrackingResponse.Segment(segmentId, 2, "IN_TRANSIT", "ROAD",
                    new(Guid.NewGuid(), "Paris", null, "Paris", "France", 1, 2),
                    new(Guid.NewGuid(), "Lyon", null, "Lyon", "France", 3, 4))]),
                null, null, 100, new(48.1m, 2.2m, null, 65, 10, DateTimeOffset.UtcNow, segmentId)));
        });
        var healthService = ServiceProxy<Pegaxus.Service.HorseHealthService.IHorseHealthService>.Create((method, args) =>
        {
            Assert.Equal("GetOverviewAsync", method.Name);
            Assert.Equal(transportId, args[0]);
            Assert.Equal(accountId, args[1]);
            Assert.Equal(cancellation.Token, args[2]);
            return Task.FromResult(new HealthResponse.OverviewResponse(DateTimeOffset.UtcNow, transportId, "IN_TRANSIT", null,
            [new(new(Guid.NewGuid(), "Comet", "private-chip", "private-passport"), "GOOD",
                new(Guid.NewGuid(), "ROUTINE", "GOOD", "NORMAL", DateTimeOffset.UtcNow,
                    new(Guid.NewGuid(), "E-1", "Private employee"), null, "private health note", false), 1)]));
        });

        var tool = new CustomerGetTrackingTool(bookingService, trackingService, healthService);
        var result = await tool.RunAsync(Args($"{{\"bookingId\":\"{bookingId}\"}}"), new AssistantToolContext(accountId, cancellation.Token));

        var data = Assert.IsType<TrackingToolData>(result.Data);
        Assert.True(data.TrackingAvailable);
        Assert.Equal(48.1m, data.Gps!.Latitude);
        Assert.Equal(segmentId, data.CurrentSegment!.SegmentId);
        Assert.Equal("Paris", data.CurrentSegment.From.Name);
        Assert.Equal("GOOD", Assert.Single(data.HorseHealth).HealthStatus);
        var json = JsonSerializer.Serialize(data);
        Assert.DoesNotContain("private-chip", json);
        Assert.DoesNotContain("private-passport", json);
        Assert.DoesNotContain("Private employee", json);
        Assert.DoesNotContain("private health note", json);
        Assert.Equal("tracking", Assert.Single(result.References).Type);
    }

    [Fact]
    public async Task TrackingTool_KeepsUnavailableGpsSegmentAndEtaMissing()
    {
        var accountId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var transportId = Guid.NewGuid();
        var bookingService = ServiceProxy<Pegaxus.Service.BookingService.IBookingService>.Create((method, _) =>
            method.Name == "GetAsync"
                ? Task.FromResult(BookingDetail(bookingId, transportId))
                : throw new InvalidOperationException("Unexpected booking service call."));
        var trackingService = ServiceProxy<Pegaxus.Service.LocationTrackingService.ILocationTrackingService>.Create((method, _) =>
            method.Name == "GetAsync"
                ? Task.FromResult(new TrackingResponse.Snapshot(DateTimeOffset.UtcNow, transportId, "IN_TRANSIT",
                    new([]), null, null, 0, null))
                : throw new InvalidOperationException("Unexpected tracking service call."));
        var healthService = ServiceProxy<Pegaxus.Service.HorseHealthService.IHorseHealthService>.Create((method, _) =>
            method.Name == "GetOverviewAsync"
                ? Task.FromResult(new HealthResponse.OverviewResponse(DateTimeOffset.UtcNow, transportId, "IN_TRANSIT", null, []))
                : throw new InvalidOperationException("Unexpected horse health service call."));

        var tool = new CustomerGetTrackingTool(bookingService, trackingService, healthService);
        var result = await tool.RunAsync(Args($"{{\"bookingId\":\"{bookingId}\"}}"), new AssistantToolContext(accountId, CancellationToken.None));
        var data = Assert.IsType<TrackingToolData>(result.Data);

        Assert.True(data.TrackingAvailable);
        Assert.Null(data.Gps);
        Assert.Null(data.CurrentSegment);
        Assert.Null(data.EtaAt);
        Assert.Empty(data.HorseHealth);
    }

    private static BookingResponse.BookingDetailResponse BookingDetail(Guid bookingId, Guid transportId) => new()
    {
        BookingId = bookingId,
        BookingCode = "BK-3",
        Status = "APPROVED",
        StatusName = "Approved",
        Customer = new BookingResponse.CustomerResponse { CustomerId = Guid.NewGuid(), CustomerCode = "C-1", FullName = "Customer" },
        OriginAddress = "Origin",
        OriginCountry = "France",
        DestinationAddress = "Destination",
        DestinationCountry = "Germany",
        RequestedDepartureDate = new DateOnly(2026, 12, 1),
        TotalHorses = 1,
        Horses = [],
        SubmittedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Transport = new BookingResponse.TransportDetailResponse { TransportId = transportId, TransportCode = "TR-3", Status = "IN_TRANSIT" }
    };

    private static JsonElement Args(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IAssistantTool Stub(string name) => new StubTool(name);

    private sealed class StubTool(string name) : IAssistantTool
    {
        public string Name => name;
        public string Description => name;
        public JsonElement ParametersSchema => Args("{}");
        public Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context) =>
            Task.FromResult(new AssistantToolResult(Name, new object(), []));
    }
}

public class ServiceProxy<T> : DispatchProxy where T : class
{
    public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => throw new InvalidOperationException("No test handler configured.");

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        Handler(targetMethod ?? throw new InvalidOperationException("Missing service method."), args ?? []);

    public static T Create(Func<MethodInfo, object?[], object?> handler)
    {
        var proxy = Create<T, ServiceProxy<T>>();
        ((ServiceProxy<T>)(object)proxy).Handler = handler;
        return proxy;
    }
}
