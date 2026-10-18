using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service
{
    public async Task<Response.DetailResponse> CreateAsync(Guid transportId, Request.CreateRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.Validate(request, createValidator);
        if (transportId == Guid.Empty) throw new BadRequestException("Transport ID must be a non-empty UUID.", "VALIDATION_ERROR");
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
                var actor = await ActorAsync(accountId, cancellationToken);
                if (!actor.IsManager && !actor.IsCrew || !request.StartNow && !actor.IsManager) throw Forbidden();
                await HeaderAsync(transportId, cancellationToken);
                await db.LockRowsAsync(db.Transports.Where(item => item.TransportId == transportId), item => item.TransportId, cancellationToken);
                var (transport, segments) = await LockJourneyAsync(transportId, cancellationToken);
                var type = Enum.Parse<HandoverType>(request.HandoverType);
                if (type == HandoverType.CARRIER_HANDOVER || type == HandoverType.OTHER && (request.FromType == "PROVIDER" || request.ToType == "PROVIDER"))
                    throw new UnprocessableEntityException("Provider handovers are not supported by the current database schema", "HANDOVER_TYPE_NOT_SUPPORTED");
                if (transport.Status != (type == HandoverType.PICKUP ? TransportStatus.READY : TransportStatus.IN_TRANSIT))
                    throw new ConflictException("The transport status does not allow creating this handover", "INVALID_TRANSPORT_STATUS");
                var ordered = EffectiveSegments(segments.Where(segment => EligibleStatuses.Contains(segment.RoutePlan.Status))
                    .Select(segment => new SegmentData(segment.RoutePlanRouteId, segment.RoutePlanId, segment.RoutePlan.Status, segment.RoutePlan.UpdatedAt,
                        segment.SequenceNo, segment.RouteType, segment.SupersededAt, segment.ActualDepartureAt, "", "")).ToArray(), transport.Status);
                var effective = ordered.Select(entry => segments.Single(segment => segment.RoutePlanRouteId == entry.Id)).ToArray();
                var customerId = await db.Bookings.Where(booking => booking.BookingId == transport.BookingId).Select(booking => booking.CustomerId).SingleAsync(cancellationToken);
                var from = segments.SingleOrDefault(segment => segment.RoutePlanRouteId == request.FromRoutePlanRouteId);
                var to = segments.SingleOrDefault(segment => segment.RoutePlanRouteId == request.ToRoutePlanRouteId);
                var now = Truncate(clock.GetUtcNow());
                var item = new HandoverRecord
                {
                    HandoverRecordId = Guid.NewGuid(), TransportId = transportId, HandoverType = type,
                    FromRoutePlanRouteId = request.FromRoutePlanRouteId, ToRoutePlanRouteId = request.ToRoutePlanRouteId,
                    Status = request.StartNow ? HandoverStatus.IN_PROGRESS : HandoverStatus.PLANNED,
                    PlannedHandoverAt = request.PlannedHandoverAt.HasValue ? Truncate(request.PlannedHandoverAt.Value) : now,
                    FromType = type == HandoverType.OTHER ? Enum.Parse<HandoverPartyType>(request.FromType!) : type == HandoverType.PICKUP ? HandoverPartyType.CUSTOMER : HandoverPartyType.EMPLOYEE,
                    ToType = type == HandoverType.OTHER ? Enum.Parse<HandoverPartyType>(request.ToType!) : type == HandoverType.DELIVERY ? HandoverPartyType.CUSTOMER : HandoverPartyType.EMPLOYEE,
                    FromCustomerId = type == HandoverType.OTHER ? request.FromCustomerId : type == HandoverType.PICKUP ? customerId : null,
                    ToCustomerId = type == HandoverType.OTHER ? request.ToCustomerId : type == HandoverType.DELIVERY ? customerId : null,
                    FromEmployeeId = type == HandoverType.OTHER ? request.FromEmployeeId : from?.DriverEmployeeId,
                    ToEmployeeId = type == HandoverType.OTHER ? request.ToEmployeeId : to?.DriverEmployeeId,
                    CreatedAt = now, UpdatedAt = now
                };
                ValidateBoundary(item, segments, effective, customerId);
                if (!await CanOperateAsync(item, actor, cancellationToken)) throw Forbidden();
                var duplicate = await db.HandoverRecords.AsNoTracking().Where(record => record.TransportId == transportId && record.HandoverType == type &&
                    record.FromRoutePlanRouteId == item.FromRoutePlanRouteId && record.ToRoutePlanRouteId == item.ToRoutePlanRouteId && record.Status != HandoverStatus.CANCELLED)
                    .Select(record => (Guid?)record.HandoverRecordId).FirstOrDefaultAsync(cancellationToken);
                if (duplicate.HasValue) throw new ConflictException("A handover already exists for this boundary", "HANDOVER_ALREADY_EXISTS", new { HandoverRecordId = duplicate.Value });
                item.SequenceNo = checked((await db.HandoverRecords.Where(record => record.TransportId == transportId).MaxAsync(record => (int?)record.SequenceNo, cancellationToken) ?? 0) + 1);
                var prefix = $"HO-{now:yyyyMMdd}-";
                var codes = await db.HandoverRecords.AsNoTracking().Where(record => record.HandoverCode.StartsWith(prefix)).Select(record => record.HandoverCode).ToArrayAsync(cancellationToken);
                var next = codes.Select(code => int.TryParse(code[prefix.Length..], out var number) ? number : 0).DefaultIfEmpty().Max() + 1;
                if (next > 9999) throw new ConflictException("The daily handover code range is exhausted", "HANDOVER_CODE_EXHAUSTED");
                item.HandoverCode = prefix + next.ToString("D4");
                await FillCreationSnapshotsAsync(item, request, from, to, cancellationToken);
                db.HandoverRecords.Add(item);
                if (!request.StartNow)
                {
                    var recipients = await db.Employees.AsNoTracking().Active().Where(employee =>
                        employee.EmployeeId == item.FromEmployeeId || employee.EmployeeId == item.ToEmployeeId)
                        .Select(employee => employee.AccountId).Distinct().ToArrayAsync(cancellationToken);
                    foreach (var recipient in recipients)
                        notifications.Queue(new()
                        {
                            AccountId = recipient, TransportId = transportId, Type = NotificationType.HANDOVER, Priority = NotificationPriority.NORMAL,
                            Title = "Handover scheduled", Message = $"Handover {item.HandoverCode} has been scheduled",
                            ReferenceType = "HandoverRecord", ReferenceId = item.HandoverRecordId,
                            ActionUrl = $"/crew/transports/{transportId}/handovers/{item.HandoverRecordId}", CreatedAt = now
                        });
                }
                var result = await DetailAsync(item, await HeaderAsync(transportId, cancellationToken), actor, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                await notifications.FlushAsync(cancellationToken);
                return result;
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                if (attempt < 19) { db.ChangeTracker.Clear(); continue; }
                throw new ConflictException("The record changed. Please retry the request", "CONCURRENT_UPDATE");
            }
        }
    }

    private async Task FillCreationSnapshotsAsync(HandoverRecord item, Request.CreateRequest request, RoutePlanRoute? from, RoutePlanRoute? to, CancellationToken cancellationToken)
    {
        async Task<(string Name, string? Phone)> PartyAsync(Guid? customerId, Guid? employeeId)
        {
            if (customerId.HasValue)
            {
                var customer = await db.Customers.AsNoTracking().Where(customer => customer.CustomerId == customerId && !customer.IsDeleted &&
                    !customer.Account.IsDeleted && customer.Account.Status == AccountStatus.ACTIVE).Select(customer => new { customer.FullName, customer.Phone }).SingleOrDefaultAsync(cancellationToken);
                if (customer is null) throw new UnprocessableEntityException("The handover party is unavailable", "INVALID_SEGMENT_PAIR");
                return (customer.FullName, customer.Phone);
            }
            var employee = await db.Employees.AsNoTracking().Active().Where(employee => employee.EmployeeId == employeeId)
                .Select(employee => new { employee.FullName, employee.Phone }).SingleOrDefaultAsync(cancellationToken);
            if (employee is null) throw new UnprocessableEntityException("The handover party is unavailable", "INVALID_SEGMENT_PAIR");
            return (employee.FullName, employee.Phone);
        }
        var sender = await PartyAsync(item.FromCustomerId, item.FromEmployeeId);
        var receiver = await PartyAsync(item.ToCustomerId, item.ToEmployeeId);
        var locationId = item.HandoverType == HandoverType.PICKUP && to is not null
            ? await db.Routes.Where(route => route.RouteId == to.RouteId).Select(route => (Guid?)route.FromLocationId).SingleAsync(cancellationToken)
            : from is not null ? await db.Routes.Where(route => route.RouteId == from.RouteId).Select(route => (Guid?)route.ToLocationId).SingleAsync(cancellationToken) : null;
        var location = await db.Locations.AsNoTracking().Where(location => location.LocationId == locationId)
            .Select(location => new { location.LocationName, location.Address, location.Country }).SingleOrDefaultAsync(cancellationToken);
        item.LocationName = request.LocationName is null ? location?.LocationName : Text.EmptyToNull(request.LocationName);
        item.Address = request.Address is null ? location?.Address : Text.EmptyToNull(request.Address);
        item.Country = request.Country is null ? location?.Country : Text.EmptyToNull(request.Country);
        item.FromPartyName = request.FromPartyName is null ? sender.Name : Text.EmptyToNull(request.FromPartyName);
        item.FromContactName = request.FromContactName is null ? sender.Name : Text.EmptyToNull(request.FromContactName);
        item.FromContactPhone = request.FromContactPhone is null ? sender.Phone : Text.EmptyToNull(request.FromContactPhone);
        item.ToPartyName = request.ToPartyName is null ? receiver.Name : Text.EmptyToNull(request.ToPartyName);
        item.ToContactName = request.ToContactName is null ? receiver.Name : Text.EmptyToNull(request.ToContactName);
        item.ToContactPhone = request.ToContactPhone is null ? receiver.Phone : Text.EmptyToNull(request.ToContactPhone);
    }
}
