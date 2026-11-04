using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.PriceListService.Validator;

using Claim = System.Security.Claims.Claim;
using PriceListContract = Pegaxus.Service.PriceListService;

namespace Pegaxus.Service.Test;

public class PriceListHttpTests
{
    [Fact]
    public void Controller_IsLogisticsManagerOnly_AndEveryActionHasItsRateLimit()
    {
        var controller = typeof(PriceListsController);
        Assert.Equal(JwtExtensions.LogisticsManagerPolicy, controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToArray();
        Assert.Equal(10, actions.Length);
        foreach (var action in actions)
        {
            Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
            var expected = action.Name.EndsWith("Item", StringComparison.Ordinal) || action.Name.EndsWith("Items", StringComparison.Ordinal)
                ? RateLimitingExtensions.PriceListItemPolicy
                : RateLimitingExtensions.PriceListPolicy;
            Assert.Equal(expected, action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }
    }

    [Fact]
    public async Task Http_EnforcesRolesValidatesAndLimitsRequestsPerUser()
    {
        const string secret = "price-list-tests-only-secret-key-with-more-than-32-characters";
        await using var db = TestSupport.CreateDb();
        var list = await PriceListSupport.AddListAsync(db, active: false);
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (otherManager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var accountId = manager.AccountId;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(PriceListsController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = secret, ["JwtOptions:Issuer"] = "price-list-test",
            ["JwtOptions:Audience"] = "price-list-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db);
        builder.Services.AddControllers().AddApplicationPart(typeof(PriceListsController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddPriceListRateLimiting();
        builder.Services.AddScoped<PriceListContract.IPriceListService, PriceListContract.Service>();
        builder.Services.AddScoped<IValidator<PriceListContract.Request.ListRequest>, ListRequestValidator>();
        builder.Services.AddScoped<IValidator<PriceListContract.Request.CreateRequest>, CreateRequestValidator>();
        builder.Services.AddScoped<IValidator<PriceListContract.Request.UpdateRequest>, UpdateRequestValidator>();
        builder.Services.AddScoped<IValidator<PriceListContract.Request.CreateItemRequest>, CreateItemRequestValidator>();
        builder.Services.AddScoped<IValidator<PriceListContract.Request.UpdateItemRequest>, UpdateItemRequestValidator>();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/price-lists")).StatusCode);
        foreach (var role in new[] { JwtServices.GetRoleName(AccountType.CUSTOMER), JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST), JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR) })
        {
            SignIn(role, accountId);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/price-lists")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/price-lists/" + list.PriceListId + "/activate", new { })).StatusCode);
        }

        SignIn(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER), accountId);
        var invalid = await client.PostAsJsonAsync("/api/v1/price-lists", new { name = "Q1", currency = "VND", taxRate = 150, depositRate = 40 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("errorCode").GetString());
        Assert.Equal("taxRate", error.GetProperty("errors")[0].GetProperty("field").GetString());

        var created = await client.PostAsJsonAsync("/api/v1/price-lists", new { name = "Q1", currency = "VND", taxRate = 20, depositRate = 40 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.False(body.GetProperty("value").GetProperty("isActive").GetBoolean());

        var item = await client.PostAsJsonAsync($"/api/v1/price-lists/{list.PriceListId}/items",
            new { itemCode = "STRAW", itemName = "Extra straw", itemType = "OTHER", unit = "HORSE_DAY", unitPrice = 10 });
        Assert.Equal(HttpStatusCode.Created, item.StatusCode);
        var duplicate = await client.PostAsJsonAsync($"/api/v1/price-lists/{list.PriceListId}/items",
            new { itemCode = "STRAW", itemName = "Extra straw", itemType = "OTHER", unit = "HORSE_DAY", unitPrice = 10 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("DUPLICATE_ITEM_CODE", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());

        // The price list bucket allows 30 requests a minute and the two POSTs above used 2 of them, so the 31st request overall is refused.
        var used = 2;
        var rejectedAt = 0;
        for (var index = used + 1; index <= 40 && rejectedAt == 0; index++)
            if ((await client.GetAsync("/api/v1/price-lists")).StatusCode == HttpStatusCode.TooManyRequests) rejectedAt = index;
        Assert.Equal(31, rejectedAt);
        var limited = await client.GetAsync("/api/v1/price-lists");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("TOO_MANY_REQUESTS", (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());

        // Lines have their own bucket of 60 requests a minute (2 used by the two item posts), and other users are not affected.
        var itemsPath = $"/api/v1/price-lists/{list.PriceListId}/items";
        var itemRejectedAt = 0;
        for (var index = 3; index <= 70 && itemRejectedAt == 0; index++)
            if ((await client.GetAsync(itemsPath)).StatusCode == HttpStatusCode.TooManyRequests) itemRejectedAt = index;
        Assert.Equal(61, itemRejectedAt);
        SignIn(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER), otherManager.AccountId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/price-lists")).StatusCode);
        await app.StopAsync();

        void SignIn(string role, Guid account)
        {
            var token = new JwtSecurityToken("price-list-test", "price-list-test",
                [new Claim(ClaimTypes.NameIdentifier, account.ToString()), new Claim(ClaimTypes.Role, role)],
                expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
    }
}
