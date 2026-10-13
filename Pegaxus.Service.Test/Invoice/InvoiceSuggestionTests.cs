using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.JwtService;

using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class InvoiceSuggestionTests
{
    [Fact]
    public async Task Final_PreviewsEveryPaidDepositForThisBooking_WithoutWritingOrTracking()
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee, booking, transport) = await SeedAsync(db);
        AddInvoice(db, booking, employee, InvoiceType.DEPOSIT, InvoiceStatus.PAID, 30_000_000, "INV-DEPOSIT-1");
        AddInvoice(db, booking, employee, InvoiceType.DEPOSIT, InvoiceStatus.PAID, 1_000_000.25m, "INV-DEPOSIT-2");
        AddInvoice(db, booking, employee, InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, 99_000_000, "INV-UNPAID");
        var (_, otherCustomer) = await TestSupport.AddCustomerAsync(db);
        var (otherBooking, _) = await TestSupport.AddPendingBookingAsync(db, otherCustomer);
        AddInvoice(db, otherBooking, employee, InvoiceType.DEPOSIT, InvoiceStatus.PAID, 99_000_000, "INV-OTHER-BOOKING");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Service(db).GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = " final " });

        Assert.Equal(booking.BookingId, result.BookingId);
        Assert.Equal(booking.BookingCode, result.BookingCode);
        Assert.Equal("APPROVED", result.BookingStatus);
        Assert.Equal(transport.TransportId, result.TransportId);
        Assert.Equal("COMPLETED", result.TransportStatus);
        Assert.Equal("FINAL", result.Type);
        Assert.Equal("VND", result.Currency);
        Assert.True(result.IsPaymentRequired);
        Assert.Equal(new[] { -30_000_000m, -1_000_000.25m }, result.Items.Select(item => item.Total));
        Assert.All(result.Items, item =>
        {
            Assert.Equal("OTHER", item.ItemType);
            Assert.Equal(1m, item.Quantity);
            Assert.Equal(item.Price, item.Total);
            Assert.True(item.IsDepositDeduction);
            Assert.Null(item.InvoiceItemId);
            Assert.Null(item.ItemDate);
            Assert.Null(item.Horse);
            Assert.Null(item.EmergencyCostRequestId);
            Assert.Contains("not refunded in cash", item.Note);
        });
        Assert.Contains("INV-DEPOSIT-1", result.Items[0].Description);
        Assert.Contains("INV-DEPOSIT-2", result.Items[1].Description);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(4, await db.Invoices.CountAsync());
        Assert.Empty(await db.InvoiceItems.ToListAsync());
        Assert.Empty(await db.InvoiceStatusHistories.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("999")]
    [InlineData("FINAL, DEPOSIT")]
    [InlineData("UNKNOWN")]
    public async Task InvalidType_ReturnsFieldValidationError(string? type)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _, booking, _) = await SeedAsync(db);
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => Service(db)
            .GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = type! }));
        Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
        Assert.Equal("type", Assert.Single(error.Errors).Field);
    }

    [Fact]
    public async Task Final_DoesNotCombinePaidDepositsInDifferentCurrencies()
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee, booking, _) = await SeedAsync(db);
        AddInvoice(db, booking, employee, InvoiceType.DEPOSIT, InvoiceStatus.PAID, code: "INV-VND");
        AddInvoice(db, booking, employee, InvoiceType.DEPOSIT, InvoiceStatus.PAID, code: "INV-EUR");
        db.Invoices.Local.Single(invoice => invoice.InvoiceCode == "INV-EUR").Currency = "EUR";
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Service(db)
            .GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = "FINAL" }));
        Assert.Equal("CURRENCY_MISMATCH", error.ErrorCode);
    }

    [Theory]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task OtherEmployeeRoles_AreForbiddenBeforeBookingLookup(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee, _, _) = await SeedAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db)
            .GetSuggestionAsync(account.AccountId, Guid.NewGuid(), new() { Type = "FINAL" }));
    }

    [Fact]
    public async Task CustomerDeletedAccountAndInactiveManager_AreForbidden()
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee, booking, _) = await SeedAsync(db);
        var (customerAccount, _) = await TestSupport.AddCustomerAsync(db);
        var service = Service(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetSuggestionAsync(customerAccount.AccountId, booking.BookingId, new() { Type = "FINAL" }));
        account.IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = "FINAL" }));
        account.IsDeleted = false;
        employee.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = "FINAL" }));
    }

    [Theory]
    [InlineData(BookingStatus.DRAFT)]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.REJECTED)]
    [InlineData(BookingStatus.CANCELLED)]
    public async Task UnapprovedBooking_ReturnsConflict(BookingStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _, booking, _) = await SeedAsync(db);
        booking.Status = status;
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service(db)
            .GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = "QUOTATION" }));
        Assert.Equal("INVALID_BOOKING_STATUS", error.ErrorCode);
    }

    [Theory]
    [InlineData("DEPOSIT", TransportStatus.CREATED, true)]
    [InlineData("DEPOSIT", TransportStatus.PLANNING, true)]
    [InlineData("DEPOSIT", TransportStatus.READY, true)]
    [InlineData("DEPOSIT", TransportStatus.IN_TRANSIT, false)]
    [InlineData("DEPOSIT", TransportStatus.COMPLETED, false)]
    [InlineData("DEPOSIT", TransportStatus.CANCELLED, false)]
    [InlineData("FINAL", TransportStatus.CREATED, false)]
    [InlineData("FINAL", TransportStatus.PLANNING, false)]
    [InlineData("FINAL", TransportStatus.READY, false)]
    [InlineData("FINAL", TransportStatus.IN_TRANSIT, false)]
    [InlineData("FINAL", TransportStatus.COMPLETED, true)]
    [InlineData("FINAL", TransportStatus.CANCELLED, false)]
    [InlineData("QUOTATION", TransportStatus.CREATED, true)]
    [InlineData("QUOTATION", TransportStatus.CANCELLED, false)]
    [InlineData("SUPPLEMENTARY", TransportStatus.IN_TRANSIT, true)]
    [InlineData("SUPPLEMENTARY", TransportStatus.CANCELLED, false)]
    [InlineData("CREDIT_NOTE", TransportStatus.COMPLETED, true)]
    [InlineData("CREDIT_NOTE", TransportStatus.CANCELLED, false)]
    public async Task InvoiceType_RequiresItsTransportStatus(string type, TransportStatus status, bool allowed)
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee, booking, transport) = await SeedAsync(db);
        transport.Status = status;
        if (type == "CREDIT_NOTE")
            AddInvoice(db, booking, employee, InvoiceType.FINAL, InvoiceStatus.PAID);
        await db.SaveChangesAsync();
        if (!allowed)
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => Service(db)
                .GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = type }));
            Assert.Equal("INVALID_TRANSPORT_STATUS", error.ErrorCode);
            return;
        }
        var result = await Service(db).GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = type });
        Assert.Empty(result.Items);
        Assert.Null(result.Currency);
        Assert.Equal(type != "QUOTATION", result.IsPaymentRequired);
    }

    [Fact]
    public async Task MissingBookingAndMissingTransport_ReturnDistinctErrors()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _, booking, transport) = await SeedAsync(db);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => Service(db)
            .GetSuggestionAsync(account.AccountId, Guid.NewGuid(), new() { Type = "FINAL" }));
        Assert.Equal("BOOKING_NOT_FOUND", missing.ErrorCode);
        db.Transports.Remove(transport);
        await db.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => Service(db)
            .GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = "QUOTATION" }));
        Assert.Equal("INVALID_TRANSPORT_STATUS", conflict.ErrorCode);
    }

    public static IEnumerable<object[]> DuplicateCases() =>
        from type in Enum.GetValues<InvoiceType>()
        from status in new[] { InvoiceStatus.DRAFT, InvoiceStatus.ISSUED, InvoiceStatus.OVERDUE }
        select new object[] { type, status };

    [Theory]
    [MemberData(nameof(DuplicateCases))]
    public async Task UnresolvedInvoiceOfEveryType_BlocksAnother(InvoiceType type, InvoiceStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee, booking, transport) = await SeedAsync(db);
        if (type == InvoiceType.DEPOSIT) transport.Status = TransportStatus.READY;
        AddInvoice(db, booking, employee, type, status);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service(db)
            .GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = type.ToString() }));
        Assert.Equal(type == InvoiceType.FINAL ? "FINAL_INVOICE_ALREADY_EXISTS" : "INVOICE_ALREADY_EXISTS", error.ErrorCode);
    }

    [Theory]
    [InlineData(InvoiceStatus.PAID, false)]
    [InlineData(InvoiceStatus.PARTIALLY_PAID, false)]
    [InlineData(InvoiceStatus.CANCELLED, true)]
    public async Task Final_CanOnlyBeRecreatedAfterCancellation(InvoiceStatus status, bool allowed)
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee, booking, _) = await SeedAsync(db);
        AddInvoice(db, booking, employee, InvoiceType.FINAL, status);
        await db.SaveChangesAsync();
        if (allowed)
            Assert.Empty((await Service(db).GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = "FINAL" })).Items);
        else
            Assert.Equal("FINAL_INVOICE_ALREADY_EXISTS", (await Assert.ThrowsAsync<ConflictException>(() => Service(db)
                .GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = "FINAL" }))).ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(InvoiceStatus.DRAFT)]
    [InlineData(InvoiceStatus.ISSUED)]
    [InlineData(InvoiceStatus.OVERDUE)]
    [InlineData(InvoiceStatus.CANCELLED)]
    public async Task CreditNote_RequiresAPaidFinal(InvoiceStatus? status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee, booking, _) = await SeedAsync(db);
        if (status.HasValue) AddInvoice(db, booking, employee, InvoiceType.FINAL, status.Value);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Service(db)
            .GetSuggestionAsync(account.AccountId, booking.BookingId, new() { Type = "CREDIT_NOTE" }));
        Assert.Equal("UNPAID_FINAL_INVOICE", error.ErrorCode);
    }

    [Fact]
    public async Task Http_EnforcesJwtRolesAndReturnsTheContractEnvelope()
    {
        const string secret = "invoice-tests-only-secret-key-with-more-than-32-characters";
        await using var db = TestSupport.CreateDb();
        var (account, employee, booking, _) = await SeedAsync(db);
        AddInvoice(db, booking, employee, InvoiceType.DEPOSIT, InvoiceStatus.PAID);
        await db.SaveChangesAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BookingController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = secret, ["JwtOptions:Issuer"] = "invoice-test",
            ["JwtOptions:Audience"] = "invoice-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db);
        builder.Services.AddControllers().AddApplicationPart(typeof(BookingController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddScoped<Pegaxus.Service.BookingService.IService>(_ => new Pegaxus.Service.BookingService.Service(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db)));
        builder.Services.AddScoped<InvoiceService.IService, InvoiceService.Service>();
        builder.Services.AddScoped<Pegaxus.Service.PaymentGatewayService.IService, InvoiceOnlinePaymentTests.Gateway>();
        builder.Services.AddScoped<IValidator<InvoiceService.Request.SuggestionRequest>, SuggestionRequestValidator>();
        builder.Services.AddSingleton(InvoiceTests.Fixture.Clock);
        builder.Services.AddScoped<IValidator<InvoiceService.Request.CreateRequest>, CreateRequestValidator>();
        builder.Services.AddScoped<IValidator<InvoiceService.Request.UpdateRequest>, UpdateRequestValidator>();
        builder.Services.AddScoped<IValidator<InvoiceService.Request.ListRequest>, ListRequestValidator>();
        builder.Services.AddScoped<IValidator<InvoiceService.Request.BookingListRequest>, BookingListRequestValidator>();
        builder.Services.AddScoped<IValidator<InvoiceService.Request.IssueRequest>, IssueRequestValidator>();
        builder.Services.AddScoped<IValidator<InvoiceService.Request.RecordPaymentRequest>, RecordPaymentRequestValidator>();
        builder.Services.AddScoped<IValidator<InvoiceService.Request.CancelRequest>, CancelRequestValidator>();
        builder.Services.AddScoped<NotificationService.IService, NotificationService.Service>();
        builder.Services.AddSingleton<NotificationService.INotificationRealtimeService, NotificationService.NoopNotificationRealtimeService>();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var path = $"/api/v1/bookings/{booking.BookingId}/invoices/suggestion";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + "?type=FINAL")).StatusCode);
        foreach (var role in new[] { EmployeeRole.DRIVER, EmployeeRole.ESCORT, EmployeeRole.TRANSPORT_SPECIALIST, EmployeeRole.FLEET_ROUTE_COORDINATOR })
        {
            SignIn(JwtServices.GetRoleName(role));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path + "?type=FINAL")).StatusCode);
        }
        SignIn(JwtServices.GetRoleName(AccountType.CUSTOMER));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path + "?type=FINAL")).StatusCode);
        SignIn(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        foreach (var query in new[] { "", "?type=1", "?type=UNKNOWN" })
        {
            var invalid = await client.GetAsync(path + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("VALIDATION_ERROR", error.GetProperty("errorCode").GetString());
            Assert.Equal("type", error.GetProperty("errors")[0].GetProperty("field").GetString());
        }
        var response = await client.GetAsync(path + "?type=FINAL");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("Invoice precheck retrieved successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value");
        Assert.Equal(booking.BookingId, value.GetProperty("bookingId").GetGuid());
        Assert.Equal("FINAL", value.GetProperty("type").GetString());
        Assert.True(value.GetProperty("items")[0].GetProperty("isDepositDeduction").GetBoolean());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("items")[0].GetProperty("itemDate").ValueKind);
        employee.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path + "?type=FINAL")).StatusCode);
        await app.StopAsync();

        void SignIn(string role)
        {
            var token = new JwtSecurityToken("invoice-test", "invoice-test",
                [new Claim(ClaimTypes.NameIdentifier, account.AccountId.ToString()), new Claim(ClaimTypes.Role, role)],
                expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
    }

    private static InvoiceService.Service Service(AppDbContext db) => new(db,
        new InvoiceOnlinePaymentTests.Gateway(), InvoiceOnlinePaymentTests.Logger, InvoiceTests.Fixture.Clock,
        new CreateRequestValidator(InvoiceTests.Fixture.Clock), new SuggestionRequestValidator(), new UpdateRequestValidator(InvoiceTests.Fixture.Clock),
        new ListRequestValidator(), new BookingListRequestValidator(), new IssueRequestValidator(), new RecordPaymentRequestValidator(InvoiceTests.Fixture.Clock), new CancelRequestValidator(),
        new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService()));

    private static async Task<(Account, Employee, Booking, Transport)> SeedAsync(AppDbContext db)
    {
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        booking.Status = BookingStatus.APPROVED;
        var transport = new Transport
        {
            TransportId = Guid.NewGuid(), TransportCode = "TR-TEST", Booking = booking, BookingId = booking.BookingId,
            Status = TransportStatus.COMPLETED, CreatedAt = booking.CreatedAt, UpdatedAt = booking.UpdatedAt
        };
        db.Transports.Add(transport);
        await db.SaveChangesAsync();
        return (account, employee, booking, transport);
    }

    private static void AddInvoice(AppDbContext db, Booking booking, Employee employee, InvoiceType type,
        InvoiceStatus status, decimal amount = 30_000_000, string code = "INV-TEST") => db.Invoices.Add(new Invoice
        {
            InvoiceId = Guid.NewGuid(), InvoiceCode = code, Booking = booking, BookingId = booking.BookingId,
            Employee = employee, EmployeeId = employee.EmployeeId, Type = type, Status = status, Currency = "VND",
            TotalAmount = amount, CreatedAt = booking.CreatedAt.AddMinutes(db.Invoices.Local.Count), UpdatedAt = booking.UpdatedAt
        });
}
