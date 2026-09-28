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
