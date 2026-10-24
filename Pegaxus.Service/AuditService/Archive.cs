using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.AuditService;

public sealed partial class Service
{
    public async Task<Response.ArchiveResponse> GetArchiveAsync(Guid accountId, Request.ArchiveRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, ArchiveValidator);

        TransportStatus[] statuses = request.IncludeCancelled ? [TransportStatus.COMPLETED, TransportStatus.CANCELLED] : [TransportStatus.COMPLETED];
        var query = db.Transports.AsNoTracking().Where(item => statuses.Contains(item.Status));
        // A cancelled trip has no completion time, so the date range and the sort use the moment it ended either way.
        if (request.CompletedFrom is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            query = query.Where(item => (item.CompletedAt ?? item.CancelledAt) >= start);
        }
        if (request.CompletedTo is { } to)
        {
            var until = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(item => (item.CompletedAt ?? item.CancelledAt) < until);
        }
        if (!string.IsNullOrWhiteSpace(request.OriginCountry))
        {
            var origin = request.OriginCountry.Trim().ToLower();
            query = query.Where(item => item.Booking.OriginCountry.ToLower() == origin);
        }
        if (!string.IsNullOrWhiteSpace(request.DestinationCountry))
        {
            var destination = request.DestinationCountry.Trim().ToLower();
            query = query.Where(item => item.Booking.DestinationCountry.ToLower() == destination);
        }
        // Only "true" narrows the list: a trip with at least one live claim.
        if (request.HasClaim == true)
            query = query.Where(item => db.Claims.Any(claim => claim.HandoverRecord.TransportId == item.TransportId && claim.Status != ClaimStatus.CANCELLED));
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(item => item.TransportCode.ToUpper().Contains(search) || item.Booking.BookingCode.ToUpper().Contains(search) ||
                item.Booking.Customer.FullName.ToUpper().Contains(search) ||
                item.Booking.BookingHorses.Any(link => link.Horse.HorseName.ToUpper().Contains(search) ||
                    link.Horse.MicrochipNumber != null && link.Horse.MicrochipNumber.ToUpper().Contains(search) ||
                    link.Horse.PassportNumber != null && link.Horse.PassportNumber.ToUpper().Contains(search)));
        }

        var total = await query.CountAsync(cancellationToken);
        var sorted = request.SortBy.Trim().ToLowerInvariant() switch
        {
            "completedat:asc" => query.OrderBy(item => item.CompletedAt ?? item.CancelledAt),
            "transportcode:asc" => query.OrderBy(item => item.TransportCode),
            "transportcode:desc" => query.OrderByDescending(item => item.TransportCode),
            _ => query.OrderByDescending(item => item.CompletedAt ?? item.CancelledAt)
        };
        var rows = await sorted.ThenBy(item => item.TransportId).Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize)
            .Select(item => new
            {
                item.TransportId, item.TransportCode, item.Status, item.BookingId, item.Booking.BookingCode, CustomerName = item.Booking.Customer.FullName,
                item.Booking.OriginCountry, item.Booking.DestinationCountry, item.CompletedAt
            }).ToListAsync(cancellationToken);

        var transportIds = rows.Select(item => item.TransportId).ToArray();
        var nullableIds = transportIds.Select(id => (Guid?)id).ToArray();
        var bookingIds = rows.Select(item => item.BookingId).ToArray();
        var horses = (await db.BookingHorses.AsNoTracking().Where(link => bookingIds.Contains(link.BookingId)).OrderBy(link => link.Horse.HorseName)
                .Select(link => new { link.BookingId, link.Horse.HorseId, link.Horse.HorseName, link.Horse.MicrochipNumber, link.Horse.PassportNumber }).ToListAsync(cancellationToken))
            .ToLookup(item => item.BookingId);
        var claims = await db.Claims.AsNoTracking().Where(claim => claim.Status != ClaimStatus.CANCELLED && transportIds.Contains(claim.HandoverRecord.TransportId))
            .GroupBy(claim => claim.HandoverRecord.TransportId).Select(group => new { TransportId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.TransportId, item => item.Count, cancellationToken);
        var incidents = await db.IncidentReports.AsNoTracking().Where(incident => incident.Status != IncidentStatus.CANCELLED && nullableIds.Contains(incident.TransportId))
            .GroupBy(incident => incident.TransportId).Select(group => new { TransportId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var incidentCounts = incidents.Where(item => item.TransportId != null).ToDictionary(item => item.TransportId!.Value, item => item.Count);

        return new(rows.Select(item => new Response.ArchiveItem(item.TransportId, item.TransportCode, item.Status.ToString(), item.BookingId, item.BookingCode, item.CustomerName,
                item.OriginCountry, item.DestinationCountry,
                horses[item.BookingId].Select(horse => new Response.ArchiveHorse(horse.HorseId, horse.HorseName, horse.MicrochipNumber, horse.PassportNumber)).ToList(),
                AsUtc(item.CompletedAt), claims.GetValueOrDefault(item.TransportId), incidentCounts.GetValueOrDefault(item.TransportId))).ToList(),
            request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
    }
}
