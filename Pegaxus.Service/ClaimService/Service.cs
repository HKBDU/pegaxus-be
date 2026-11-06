using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ClaimService;

public sealed partial class Service(AppDbContext db, TimeProvider clock, INotificationService notifications, IMediaService media,
    IValidator<Request.SubmitRequest> submitValidator, IValidator<Request.ListRequest> listValidator,
    IValidator<Request.ResolveRequest> resolveValidator, IValidator<Request.CancelRequest> cancelValidator,
    IValidator<Request.CompleteRequest> completeValidator) : IClaimService
{
    /// <summary>R19: a claim can be filed until this long after the delivery handover was signed.</summary>
    private static readonly TimeSpan ClaimWindow = TimeSpan.FromHours(24);

    private sealed record Actor(Guid AccountId, bool IsCustomer, bool IsManager);

    /// <summary>Only an active customer or an active logistics manager may touch claims (R18); anyone else is a 403.</summary>
    private async Task<Actor> ActorAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().Include(item => item.CustomerAccount).Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        var customer = account is { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.CUSTOMER, CustomerAccount: { IsDeleted: false } };
        var manager = account is { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.EMPLOYEE,
            EmployeeAccount: { IsDeleted: false, IsActive: true, Role: EmployeeRole.LOGISTICS_MANAGER } };
        if (!customer && !manager)
            throw new ForbiddenException("Only Customers and Logistics Managers are authorized to use claims.");
        return new(accountId, customer, manager);
    }

    private static DateTime NextVersion(DateTime previous, DateTime now) => now > previous ? now : Truncate(previous).AddTicks(10);

    private static NotFoundException ClaimMissing() => new("Claim not found", "CLAIM_NOT_FOUND");
    private static ConflictException Modified() => new("The claim was just modified. Please reload the page", "CLAIM_MODIFIED");
    private static ConflictException InvalidStatus(ClaimStatus status) =>
        new($"This action cannot be performed while the claim is in {status} status", "INVALID_CLAIM_STATUS");

    private static bool IsWriteConflict(Exception ex) => ex is DbUpdateConcurrencyException ||
        ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected };

    /// <summary>The latest signed delivery handover of the booking's transport, the one a claim is filed against.</summary>
    private Task<HandoverRecord?> DeliveryAsync(Guid bookingId, CancellationToken cancellationToken) =>
        db.HandoverRecords.AsNoTracking().Where(item => item.Transport.BookingId == bookingId && item.HandoverType == HandoverType.DELIVERY &&
                item.Status == HandoverStatus.COMPLETED && item.CompletedAt != null)
            .OrderByDescending(item => item.CompletedAt).ThenByDescending(item => item.HandoverRecordId).FirstOrDefaultAsync(cancellationToken);
}
