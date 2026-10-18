using System.Data;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;
using static Pegaxus.Service.Common.Validation;

namespace Pegaxus.Service.TransportProviderService;

public sealed class Service(AppDbContext db, IValidator<Request.ListRequest> listValidator,
    IValidator<Request.CreateRequest> createValidator, IValidator<Request.UpdateRequest> updateValidator,
    IValidator<Request.StatusRequest> statusValidator, NotificationService.INotificationService notifications, ILogger<Service> logger) : ITransportProviderService
{
    public async Task<Response.ListResponse> GetListAsync(Guid accountId, Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        var role = await RequireEmployeeAsync(accountId, false, cancellationToken);
        Validate(request, listValidator);
        var query = db.TransportProviders.AsNoTracking().Where(item => !item.IsDeleted);
        var status = role == EmployeeRole.FLEET_ROUTE_COORDINATOR ? "ACTIVE" : request.Status?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = Enum.Parse<ProviderStatus>(status);
            query = query.Where(item => item.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(request.SupportedMode))
        {
            var mode = "," + request.SupportedMode.Trim().ToUpperInvariant() + ",";
            query = query.Where(item => ("," + (item.SupportedModes ?? "").ToUpper() + ",").Contains(mode));
        }
        if (CountryCodes.Normalize(request.Country) is { } country)
            query = query.Where(item => item.Country == country);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(item => item.ProviderCode.ToUpper().Contains(search) || item.ProviderName.ToUpper().Contains(search) ||
                (item.TaxCode != null && item.TaxCode.ToUpper().Contains(search)));
        }
        var total = await query.CountAsync(cancellationToken);
        var sorted = request.SortBy.Trim().ToLowerInvariant() switch
        {
            "providername:desc" => query.OrderByDescending(item => item.ProviderName),
            "providercode:asc" => query.OrderBy(item => item.ProviderCode),
            "providercode:desc" => query.OrderByDescending(item => item.ProviderCode),
            "createdat:asc" => query.OrderBy(item => item.CreatedAt),
            "createdat:desc" => query.OrderByDescending(item => item.CreatedAt),
            _ => query.OrderBy(item => item.ProviderName)
        };
        var items = await sorted.ThenBy(item => item.TransportProviderId)
            .Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        return new Response.ListResponse
        {
            Items = items.Select(Map).ToArray(), PageIndex = request.PageIndex, PageSize = request.PageSize,
            TotalItems = total, TotalPages = Paging.TotalPages(total, request.PageSize)
        };
    }

    public async Task<Response.DetailResponse> GetAsync(Guid accountId, Guid id, CancellationToken cancellationToken = default)
    {
        await RequireEmployeeAsync(accountId, false, cancellationToken);
        var item = await db.TransportProviders.AsNoTracking().SingleOrDefaultAsync(item => item.TransportProviderId == id && !item.IsDeleted, cancellationToken);
        return Map(item ?? throw NotFound());
    }

    public async Task<Response.DetailResponse> CreateAsync(Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default)
    {
        await RequireEmployeeAsync(accountId, true, cancellationToken);
        Validate(request, createValidator);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var item = new TransportProvider
        {
            TransportProviderId = Guid.NewGuid(), ProviderCode = request.ProviderCode.Trim().ToUpperInvariant(),
            Status = ProviderStatus.ACTIVE, CreatedAt = UtcNowTruncated()
        };
        await ApplyAsync(item, request, cancellationToken);
        db.TransportProviders.Add(item);
        await SaveAsync(item, accountId, "CREATE", JsonSerializer.SerializeToDocument(new { }), null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Log(item, accountId, "CREATE");
        return Map(item);
    }

    public async Task<Response.WriteResponse> UpdateAsync(Guid accountId, Guid id, Request.UpdateRequest request, CancellationToken cancellationToken = default)
    {
        await RequireEmployeeAsync(accountId, true, cancellationToken);
        Validate(request, updateValidator);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var item = await GetForUpdateAsync(id, cancellationToken);
        CheckVersion(item, request.LastUpdatedAt);
        if (request.ProviderCode is not null && !string.Equals(request.ProviderCode.Trim(), item.ProviderCode, StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("ProviderCode cannot be changed.");
        var before = Snapshot(item);
        await ApplyAsync(item, request, cancellationToken);
        await SaveAsync(item, accountId, "UPDATE", before, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Log(item, accountId, "UPDATE");
        return new Response.WriteResponse { TransportProviderId = id, UpdatedAt = AsUtc(item.UpdatedAt) };
    }

    public async Task<Response.StatusResponse> UpdateStatusAsync(Guid accountId, Guid id, Request.StatusRequest request, CancellationToken cancellationToken = default)
    {
        await RequireEmployeeAsync(accountId, true, cancellationToken);
        Validate(request, statusValidator);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var item = await GetForUpdateAsync(id, cancellationToken);
        if (request.LastUpdatedAt.HasValue) CheckVersion(item, request.LastUpdatedAt.Value);
        var before = Snapshot(item);
        var status = Enum.Parse<ProviderStatus>(request.Status.Trim(), true);
        if (item.Status != status)
        {
            var recipients = await db.Employees.AsNoTracking().Active().Where(employee => employee.AccountId != accountId && employee.Account.AccountType == AccountType.EMPLOYEE && (employee.Role == EmployeeRole.LOGISTICS_MANAGER || employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR))
                .Select(employee => new { employee.AccountId, employee.Role }).Distinct().ToListAsync(cancellationToken);
            var reason = EmptyToNull(request.Reason);
            foreach (var recipient in recipients)
                notifications.Queue(new NotificationCreateRequest
                {
                    AccountId = recipient.AccountId, Type = NotificationType.SYSTEM, Title = "Transport provider status changed",
                    Message = $"{item.ProviderName} ({item.ProviderCode}) changed from {item.Status} to {status}.{(reason is null ? "" : " Reason: " + reason)}",
                    ReferenceType = nameof(TransportProvider), ReferenceId = id,
                    ActionUrl = NotificationLinks.TransportProviders(recipient.Role), CreatedAt = UtcNowTruncated()
                });
            if (status != ProviderStatus.ACTIVE)
                await MailService.MailRecipients.QueueAsync(db, recipients.Select(item => item.AccountId), UtcNowTruncated(),
                    recipient => MailService.MailRecipients.Mail(recipient, $"Transport provider status changed — {item.ProviderCode}",
                        MailService.EmailBodies.CatalogChangeBody(recipient.FullName, "Transport provider", item.ProviderCode, item.ProviderName,
                            $"changed from {item.Status} to {status}", reason, NotificationLinks.TransportProviders(recipient.Role))), cancellationToken);
        }
        item.Status = status;
        await SaveAsync(item, accountId, "STATUS_CHANGED", before, EmptyToNull(request.Reason), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        Log(item, accountId, "STATUS_CHANGED");
        return new Response.StatusResponse { TransportProviderId = id, Status = item.Status.ToString(), UpdatedAt = AsUtc(item.UpdatedAt) };
    }

    public async Task DeleteAsync(Guid accountId, Guid id, CancellationToken cancellationToken = default)
    {
        await RequireEmployeeAsync(accountId, true, cancellationToken);
        // shortcut: future route writers must validate the provider within a Serializable transaction.
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var item = await GetForUpdateAsync(id, cancellationToken);
        if (await db.RoutePlanRoutes.AnyAsync(route => route.TransportProviderId == id &&
            route.RoutePlan.Status != RoutePlanStatus.COMPLETED && route.RoutePlan.Status != RoutePlanStatus.CANCELLED, cancellationToken))
            throw new ConflictException("The provider is assigned to route segments that are not completed or cancelled", "PROVIDER_IN_USE");
        var before = Snapshot(item);
        item.IsDeleted = true;
        item.DeletedAt = UtcNowTruncated();
        item.DeletedByAccountId = accountId;
        await SaveAsync(item, accountId, "DELETE", before, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Log(item, accountId, "DELETE");
    }

    private async Task<EmployeeRole> RequireEmployeeAsync(Guid accountId, bool write, CancellationToken cancellationToken)
    {
        var role = await db.Employees.AsNoTracking().Active().Where(item => item.AccountId == accountId && item.Account.AccountType == AccountType.EMPLOYEE)
            .Select(item => (EmployeeRole?)item.Role).SingleOrDefaultAsync(cancellationToken);
        if (role != EmployeeRole.LOGISTICS_MANAGER && (write || role != EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new ForbiddenException("Only Logistics Managers may manage providers; Fleet Route Coordinators have read-only access.");
        return role!.Value;
    }

    private async Task<TransportProvider> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        // Updating the key to itself serializes catalog mutations until the transaction ends.
        await db.LockRowsAsync(db.TransportProviders.Where(item => item.TransportProviderId == id), item => item.TransportProviderId, cancellationToken);
        var query = db.TransportProviders.AsQueryable();
        var item = await query.SingleOrDefaultAsync(item => item.TransportProviderId == id && !item.IsDeleted, cancellationToken) ?? throw NotFound();
        await db.Entry(item).ReloadAsync(cancellationToken);
        if (item.IsDeleted) throw NotFound();
        return item;
    }

    private async Task ApplyAsync(TransportProvider item, Request.SaveRequest request, CancellationToken cancellationToken)
    {
        var taxCode = request.TaxCode.Trim().ToUpperInvariant();
        if (await db.TransportProviders.AnyAsync(other => !other.IsDeleted && other.TransportProviderId != item.TransportProviderId &&
            other.ProviderCode.ToUpper() == item.ProviderCode.ToUpper(), cancellationToken))
            throw Duplicate("PROVIDER_CODE_DUPLICATED");
        if (await db.TransportProviders.AnyAsync(other => !other.IsDeleted && other.TransportProviderId != item.TransportProviderId &&
            other.TaxCode != null && other.TaxCode.ToUpper() == taxCode, cancellationToken))
            throw Duplicate("TAX_CODE_DUPLICATED");
        item.ProviderName = request.ProviderName.Trim();
        item.TaxCode = taxCode;
        item.SupportedModes = string.Join(',', request.SupportedModes.Select(mode => mode.Trim().ToUpperInvariant()).Distinct().Order());
        item.ContactName = EmptyToNull(request.ContactName);
        item.Phone = EmptyToNull(request.Phone);
        item.Email = EmptyToNull(request.Email);
        item.Address = EmptyToNull(request.Address);
        item.Country = CountryCodes.Normalize(request.Country);
        item.Note = EmptyToNull(request.Note);
    }

    private async Task SaveAsync(TransportProvider item, Guid actor, string action, JsonDocument before, string? reason, CancellationToken cancellationToken)
    {
        var now = UtcNowTruncated();
        item.UpdatedAt = now > item.UpdatedAt ? now : item.UpdatedAt.AddTicks(10);
        db.CatalogAuditLogs.Add(new CatalogAuditLog
        {
            CatalogAuditLogId = Guid.NewGuid(), EntityType = nameof(TransportProvider), EntityId = item.TransportProviderId,
            ActorAccountId = actor, ActionType = action, Summary = reason, BeforeData = before, AfterData = Snapshot(item), CreatedAt = item.UpdatedAt
        });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Modified(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg &&
            pg.ConstraintName is "TransportProvider_provider_code_key" or "TransportProvider_tax_code_key")
        {
            throw Duplicate(((PostgresException)error.InnerException!).ConstraintName == "TransportProvider_tax_code_key"
                ? "TAX_CODE_DUPLICATED" : "PROVIDER_CODE_DUPLICATED");
        }
    }

    private void Log(TransportProvider item, Guid actor, string action) =>
        logger.LogInformation("Transport provider {ProviderId} {Action} by {AccountId}", item.TransportProviderId, action, actor);

    private static Response.DetailResponse Map(TransportProvider item) => new()
    {
        TransportProviderId = item.TransportProviderId, ProviderCode = item.ProviderCode, ProviderName = item.ProviderName,
        Status = item.Status.ToString(), SupportedModes = (item.SupportedModes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        TaxCode = item.TaxCode, ContactName = item.ContactName, Phone = item.Phone, Email = item.Email, Address = item.Address,
        Country = item.Country, Note = item.Note, CreatedAt = AsUtc(item.CreatedAt), UpdatedAt = AsUtc(item.UpdatedAt)
    };

    private static JsonDocument Snapshot(TransportProvider item) => JsonSerializer.SerializeToDocument(new
    {
        Provider = Map(item), item.IsDeleted, item.DeletedAt, item.DeletedByAccountId
    });
    private static void CheckVersion(TransportProvider item, DateTimeOffset expected)
    {
        if (DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc) != expected.UtcDateTime) throw Modified();
    }
    private static ConflictException Modified() => new("The provider was just modified. Please reload the page", "PROVIDER_MODIFIED");
    private static ConflictException Duplicate(string code) => new("Transport Provider with this Code / Tax ID already exists.", code);
    private static NotFoundException NotFound() => new("Transport Provider not found", "PROVIDER_NOT_FOUND");
}
