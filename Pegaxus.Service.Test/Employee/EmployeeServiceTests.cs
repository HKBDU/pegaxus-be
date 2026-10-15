using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.Exceptions;

using EmployeeRequest = Pegaxus.Service.EmployeeService.Request;
using EmployeeServiceImpl = Pegaxus.Service.EmployeeService.Service;

namespace Pegaxus.Service.Test;

public class EmployeeServiceTests
{
    [Fact]
    public async Task Disable_RemainsVisibleAndCanBeReactivated()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = new EmployeeServiceImpl(db, new PasswordHasher<Account>());

        var disabled = await service.UpdateStatusAsync(employee.EmployeeId, new() { Status = "INACTIVE" }, manager.AccountId);
        Assert.False(disabled.IsDeleted);
        Assert.Equal("INACTIVE", (await service.GetAsync(employee.EmployeeId)).AccountStatus);
        Assert.Contains((await service.GetListAsync(new() { AccountStatus = "INACTIVE" })).Items, item => item.EmployeeId == employee.EmployeeId);

        await service.UpdateStatusAsync(employee.EmployeeId, new() { Status = "ACTIVE" }, manager.AccountId);
        Assert.Equal("ACTIVE", (await service.GetAsync(employee.EmployeeId)).AccountStatus);
    }

    [Fact]
    public async Task Suspend_IsDeleteAndCannotExposeOrReactivateDeletedEmployee()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = new EmployeeServiceImpl(db, new PasswordHasher<Account>());

        var deleted = await service.UpdateStatusAsync(employee.EmployeeId, new() { Status = "SUSPENDED" }, manager.AccountId);
        Assert.True(deleted.IsDeleted);
        Assert.True(employee.Account.IsDeleted);
        Assert.DoesNotContain((await service.GetListAsync(new())).Items, item => item.EmployeeId == employee.EmployeeId);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(employee.EmployeeId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateStatusAsync(employee.EmployeeId, new() { Status = "ACTIVE" }, manager.AccountId));
    }

    [Fact]
    public async Task Export_CreatesXlsxWorkbook()
    {
        await using var db = TestSupport.CreateDb();
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.FullName = "=2+2";
        await db.SaveChangesAsync();
        var service = new EmployeeServiceImpl(db, new PasswordHasher<Account>());

        var file = await service.ExportAsync(new EmployeeRequest.EmployeeListRequest());
        using var workbook = new XLWorkbook(new MemoryStream(file));
        var sheet = workbook.Worksheet("Employees");

        Assert.Equal("Employee Code", sheet.Cell(1, 1).GetString());
        Assert.Equal("=2+2", sheet.Cell(2, 2).GetString());
    }

    [Fact]
    public async Task UpdateStatus_RejectsOwnAccount()
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = new EmployeeServiceImpl(db, new PasswordHasher<Account>());

        var exception = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateStatusAsync(
            employee.EmployeeId,
            new EmployeeRequest.UpdateEmployeeStatusRequest { Status = "INACTIVE" },
            account.AccountId));

        Assert.Equal("CANNOT_UPDATE_OWN_ACCOUNT", exception.ErrorCode);
    }
}
