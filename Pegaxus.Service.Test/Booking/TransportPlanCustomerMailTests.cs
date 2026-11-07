using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

using Plans = Pegaxus.Service.TransportPlanService;

namespace Pegaxus.Service.Test;

public class TransportPlanCustomerMailTests
{
    private static async Task<(Account Manager, Account Customer, Booking Booking, Employee Contact)> ApprovedBookingAsync(AppDbContext db)
    {
        var (customerAccount, customer) = await TestSupport.AddCustomerAsync(db);
        var (manager, contact) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        booking.Status = BookingStatus.APPROVED;
        booking.OriginCountry = booking.DestinationCountry = "FR";
        booking.AssignedEmployeeId = contact!.EmployeeId;
        booking.Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-MAIL", Booking = booking };
        db.Transports.Add(booking.Transport);
        await db.SaveChangesAsync();
        return (manager, customerAccount, booking, contact);
    }

    [Fact]
    public async Task Update_EmailsTheCustomerOnlyWhenTheTripMoves_AndNamesTheBookingAndContact()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, customer, booking, contact) = await ApprovedBookingAsync(db);
        var service = new Plans.Service(db, TestSupport.CreateNotifications(db));
        var start = DateTimeOffset.UtcNow.AddDays(40);
        var created = await service.CreateAsync(manager.AccountId, booking.BookingId, new() { PlannedStartAt = start, PlannedEndAt = start.AddDays(1) });
        var plan = await db.TransportPlans.SingleAsync();
        var emailsBefore = await db.OutboxMessages.CountAsync();

        // An internal note change: the in-app notification stays, no email.
        await service.UpdateAsync(manager.AccountId, created.TransportPlanId, new() { LastUpdatedAt = Pegaxus.Service.Common.DbTime.AsUtc(plan.UpdatedAt), PlanningNote = "Internal note" });
        Assert.Equal(emailsBefore, await db.OutboxMessages.CountAsync());
        Assert.Contains(await db.Notifications.ToListAsync(), item => item.Title == "Transport Plan updated");

        // A new start date: one email to the customer, titled with the booking code, naming the contact.
        plan = await db.TransportPlans.SingleAsync();
        await service.UpdateAsync(manager.AccountId, created.TransportPlanId, new()
        {
            LastUpdatedAt = Pegaxus.Service.Common.DbTime.AsUtc(plan.UpdatedAt), PlannedStartAt = start.AddDays(3), PlannedEndAt = start.AddDays(4), UpdateReason = "Truck change"
        });
        var mails = (await db.OutboxMessages.ToListAsync()).Skip(emailsBefore).ToList();
        var mail = Assert.Single(mails);
        Assert.Contains(customer.Email, mail.Payload);
        Assert.Contains(booking.BookingCode, mail.Payload);
        Assert.DoesNotContain(plan.PlanCode, mail.Payload);
        Assert.Contains(contact.FullName, mail.Payload);
    }
}
