using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pegaxus.Repository.Enum;

using static Pegaxus.Service.Test.HandoverTests;

namespace Pegaxus.Service.Test;

public partial class HandoverHttpTests
{
    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 200)]
    [InlineData(EmployeeRole.DRIVER, 200)]
    [InlineData(EmployeeRole.ESCORT, 200)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403)]
    public async Task Http_CompleteAcceptsMultipartAndReturnsFlatRecordWithEffects(EmployeeRole role, int status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var actor = role switch { EmployeeRole.LOGISTICS_MANAGER => f.Manager, EmployeeRole.FLEET_ROUTE_COORDINATOR => f.Coordinator,
            EmployeeRole.DRIVER => f.Driver, EmployeeRole.ESCORT => f.Escort, _ => await PersonnelAssignmentTests.EmployeeAsync(db, role) };
        await using var host = await StartAsync(db, f); using var client = Client(host, actor.AccountId, role);
        var url = $"/api/v1/handover-records/{f.Pickup.HandoverRecordId}/complete";
        using var form = CompletionForm(f); using var response = await client.PostAsync(url, form);
        Assert.Equal(status, (int)response.StatusCode);
        if (status != 200) { Assert.Empty(f.Media.Uploaded); return; }
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal("Handover completed successfully", body.GetProperty("message").GetString());
        var record = body.GetProperty("value"); Assert.Equal(f.Pickup.HandoverRecordId, record.GetProperty("handoverRecordId").GetGuid());
        Assert.Equal("COMPLETED", record.GetProperty("status").GetString()); Assert.Equal(0, record.GetProperty("availableActions").GetArrayLength());
        Assert.Equal("IN_TRANSIT", record.GetProperty("transport").GetProperty("status").GetString());
        Assert.Equal("ACTIVE", record.GetProperty("effects").GetProperty("routePlanStatus").GetString());
        Assert.False(record.GetProperty("effects").TryGetProperty("claimWindowEndsAt", out _));
        Assert.StartsWith("https://storage.example.com/", record.GetProperty("senderSignatureUrl").GetString());
        Assert.StartsWith("https://storage.example.com/", record.GetProperty("receiverSignatureUrl").GetString());
        using var retry = CompletionForm(f); using var terminal = await client.PostAsync(url, retry);
        Assert.Equal("INVALID_HANDOVER_STATUS", (await ErrorAsync(terminal, HttpStatusCode.Conflict)).GetProperty("errorCode").GetString());
        Assert.Equal(2, f.Media.Uploaded.Count);
    }

    [Fact]
    public async Task Http_CompleteValidatesFilesNotesVersionsAndActualAssignments()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f); using var manager = Client(host, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var url = $"/api/v1/handover-records/{f.Pickup.HandoverRecordId}/complete";
        using var missingSignature = CompletionForm(f, receiver: false);
        Assert.Equal("SIGNATURE_REQUIRED", (await ErrorAsync(await manager.PostAsync(url, missingSignature), HttpStatusCode.BadRequest)).GetProperty("errorCode").GetString());
        using var missingNote = CompletionForm(f, note: "   ");
        Assert.Equal("VALIDATION_ERROR", (await ErrorAsync(await manager.PostAsync(url, missingNote), HttpStatusCode.BadRequest)).GetProperty("errorCode").GetString());
        using var stale = CompletionForm(f, version: f.Clock.GetUtcNow().AddDays(-1));
        Assert.Equal("HANDOVER_MODIFIED", (await ErrorAsync(await manager.PostAsync(url, stale), HttpStatusCode.Conflict)).GetProperty("errorCode").GetString());
        using var customer = Client(host, f.CustomerAccountId); using var customerForm = CompletionForm(f);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync(url, customerForm)).StatusCode);
        using var wrongSegment = Client(host, f.Receiver.AccountId, EmployeeRole.LOGISTICS_MANAGER); using var wrongForm = CompletionForm(f);
        Assert.Equal("FORBIDDEN", (await ErrorAsync(await wrongSegment.PostAsync(url, wrongForm), HttpStatusCode.Forbidden)).GetProperty("errorCode").GetString());
        using var missingRecordForm = CompletionForm(f);
        Assert.Equal("HANDOVER_RECORD_NOT_FOUND", (await ErrorAsync(await manager.PostAsync($"/api/v1/handover-records/{Guid.NewGuid()}/complete", missingRecordForm), HttpStatusCode.NotFound)).GetProperty("errorCode").GetString());
        Assert.Empty(f.Media.Uploaded); Assert.Empty(db.Notifications);
    }

    private static MultipartFormDataContent CompletionForm(Fixture fixture, bool receiver = true, string note = "Healthy horses", DateTimeOffset? version = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent((version ?? Pegaxus.Service.Common.DbTime.AsUtc(fixture.Pickup.UpdatedAt)).ToString("O")), "lastUpdatedAt" },
            { new StringContent(note), "horseConditionNote" }, { new StringContent("Passports checked"), "documentHandoverNote" },
            { new StringContent("Equipment checked"), "equipmentHandoverNote" }
        };
        foreach (var name in receiver ? new[] { "senderSignature", "receiverSignature" } : ["senderSignature"])
        {
            var content = new ByteArrayContent(HandoverCompletionTests.PngBytes); content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content, name, name + ".png");
        }
        return form;
    }
}
