using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.AuditService.Validator;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.AuditService;

public sealed partial class Service
{
    private sealed record TransportRow(Guid TransportId, string TransportCode, TransportStatus Status, Guid BookingId, DateTime CreatedAt,
        DateTime? ActualDepartureAt, DateTime? ActualArrivalAt, DateTime? CompletedAt, DateTime? CancelledAt);

    public async Task<Response.AuditResponse> GetAuditAsync(Guid accountId, Guid transportId, Request.AuditRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, AuditValidator);
        // A customer reaches only the transports of their own bookings; any other transport is the same 404 as a missing one.
        var transport = await db.Transports.AsNoTracking().Where(item => item.TransportId == transportId && (actor.IsManager || item.Booking.Customer.AccountId == accountId))
            .Select(item => new TransportRow(item.TransportId, item.TransportCode, item.Status, item.BookingId, item.CreatedAt, item.ActualDepartureAt, item.ActualArrivalAt,
                item.CompletedAt, item.CancelledAt)).SingleOrDefaultAsync(cancellationToken) ?? throw TransportMissing();
        return await BuildAuditAsync(actor, transport, request.Blocks, request.Limit, cancellationToken);
    }

    private async Task<Response.AuditResponse> BuildAuditAsync(Actor actor, TransportRow transport, string? blocks, int limit, CancellationToken cancellationToken)
    {
        // The finance block is never shown to a customer (O-F6-14); asking for it is not an error, it is just left out.
        var allowed = actor.IsManager ? AuditRules.Blocks : AuditRules.Blocks.Where(block => block != "finance").ToArray();
        var requested = string.IsNullOrWhiteSpace(blocks) ? allowed
            : blocks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(block => block.ToLowerInvariant()).Intersect(allowed).ToArray();

        // Handovers, incidents and audit records also feed the timeline, so they are read whether or not the operations block was asked for.
        var handoverRows = await HandoverRowsAsync(transport.TransportId, actor, cancellationToken);
        var incidentRows = await IncidentRowsAsync(transport.TransportId, actor, cancellationToken);
        var auditRows = await AuditRowsAsync(transport.TransportId, actor, cancellationToken);

        var result = new Response.AuditResponse(actor.IsManager ? "FULL" : "CUSTOMER", clock.GetUtcNow(),
            new(transport.TransportId, transport.TransportCode, transport.Status.ToString(), AsUtc(transport.CompletedAt), AsUtc(transport.ActualDepartureAt), AsUtc(transport.ActualArrivalAt)),
            requested.Contains("booking") ? await BookingBlockAsync(transport, actor, cancellationToken) : null,
            requested.Contains("horses") ? await HorsesBlockAsync(transport, actor, cancellationToken) : null,
            requested.Contains("routing") ? await RoutingBlockAsync(transport.TransportId, actor, cancellationToken) : null,
            requested.Contains("compliance") ? await ComplianceBlockAsync(transport.TransportId, actor, cancellationToken) : null,
            requested.Contains("operations") ? OperationsBlock(handoverRows, incidentRows, auditRows) : null,
            requested.Contains("finance") ? await FinanceBlockAsync(transport, cancellationToken) : null,
            Timeline(transport, actor, handoverRows, incidentRows, auditRows, limit));
        return result;
    }
}
