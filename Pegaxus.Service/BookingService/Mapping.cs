using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

using BookingEntity = Pegaxus.Repository.Entities.Booking;
using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.BookingService;

public sealed partial class Service
{
    private static string GetStatusName(BookingStatus status) => status switch
    {
        BookingStatus.DRAFT => "Draft",
        BookingStatus.PENDING => "Pending",
        BookingStatus.APPROVED => "Approved",
        BookingStatus.REJECTED => "Rejected",
        BookingStatus.CANCELLED => "Cancelled",
        _ => status.ToString()
    };

    private static string GetHistoryAction(BookingStatusHistory history) => history.Type switch
    {
        BookingHistoryType.APPROVE => "APPROVED",
        BookingHistoryType.REJECT => "REJECTED",
        BookingHistoryType.CANCEL => "CANCELLED",
        BookingHistoryType.SUBMIT when history.PreviousStatus is null => "CREATED",
        BookingHistoryType.SUBMIT => "UPDATED",
        _ => history.Type.ToString()
    };

    private static string GetAccountFullName(Account account) =>
        account.CustomerAccount?.FullName ?? account.EmployeeAccount?.FullName ?? account.Email;

    private static string GetAccountRole(Account account) => account.AccountType == AccountType.CUSTOMER
        ? "CUSTOMER"
        : account.EmployeeAccount?.Role.ToString() ?? "EMPLOYEE";
}
