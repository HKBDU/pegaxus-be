using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MailService;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.AuditService;

public sealed partial class Service
{
    private const string Queued = "QUEUED", Processing = "PROCESSING", Ready = "READY", Failed = "FAILED", Expired = "EXPIRED";

    /// <summary>The download stays valid this long after the package is ready (UC-77); the job leaves the cache at the same moment.</summary>
    private static readonly TimeSpan PackageLifetime = TimeSpan.FromHours(24);
    /// <summary>A job nobody picked up (or whose server restarted) for this long is reported as FAILED so the user can ask again.</summary>
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromHours(2);
    /// <summary>A package above this size is mailed as a link instead of offered on screen.</summary>
    private const long EmailThresholdBytes = 500L * 1024 * 1024;

    internal sealed class ExportJob
    {
        public Guid ExportId { get; set; }
        public Guid TransportId { get; set; }
        public string TransportCode { get; set; } = "";
        public Guid RequestedByAccountId { get; set; }
        public string Format { get; set; } = "";
        public string Mode { get; set; } = "";
        public string Status { get; set; } = Queued;
        public string? Delivery { get; set; }
        public string? StorageUrl { get; set; }
        public string? FileName { get; set; }
        public long? SizeBytes { get; set; }
        public string? Sha256 { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    private static string JobKey(Guid exportId) => $"audit-export:{exportId:N}";
    private static string ActiveKey(Guid transportId, Guid accountId, string format, string mode) => $"audit-export-active:{transportId:N}:{accountId:N}:{format}:{mode}";

    private async Task<ExportJob?> LoadJobAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var json = await cache.GetStringAsync(JobKey(exportId), cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<ExportJob>(json);
    }

    private Task SaveJobAsync(ExportJob job, TimeSpan lifetime, CancellationToken cancellationToken) =>
        cache.SetStringAsync(JobKey(job.ExportId), JsonSerializer.Serialize(job), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, cancellationToken);

    private bool IsStuck(ExportJob job) => job.Status is Queued or Processing && clock.GetUtcNow() - job.RequestedAt > StuckAfter;

    // ------------------------------------------------------------------ AU3

    public async Task<Response.ExportQueued> RequestExportAsync(Guid accountId, Guid transportId, Request.ExportRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, ExportValidator);
        var transport = await db.Transports.AsNoTracking().Where(item => item.TransportId == transportId).Select(item => new { item.TransportCode, item.Status })
            .SingleOrDefaultAsync(cancellationToken) ?? throw TransportMissing();
        if (transport.Status != TransportStatus.COMPLETED)
            throw new ConflictException("Only completed transports can be exported as an audit package", "INVALID_TRANSPORT_STATUS");

        var format = request.Format!.Trim().ToUpperInvariant(); var mode = request.Mode!.Trim().ToUpperInvariant();
        // Idempotent: asking again for the same package while one is still being made returns that job.
        // shortcut: the cache has no atomic "set if absent", so two requests in the same instant may both create a job; the loser's job is simply built too.
        var activeKey = ActiveKey(transportId, accountId, format, mode);
        if (Guid.TryParse(await cache.GetStringAsync(activeKey, cancellationToken), out var activeId) && await LoadJobAsync(activeId, cancellationToken) is { } active &&
            active.Status is Queued or Processing && !IsStuck(active))
            return new(active.ExportId, active.TransportId, active.Format, active.Mode, active.Status, active.RequestedAt);

        var job = new ExportJob { ExportId = Guid.NewGuid(), TransportId = transportId, TransportCode = transport.TransportCode, RequestedByAccountId = accountId, Format = format, Mode = mode,
            Status = Queued, RequestedAt = clock.GetUtcNow() };
        await SaveJobAsync(job, PendingLifetime, cancellationToken);
        await cache.SetStringAsync(activeKey, job.ExportId.ToString(), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StuckAfter }, cancellationToken);
        queue.Enqueue(job.ExportId);
        return new(job.ExportId, job.TransportId, job.Format, job.Mode, job.Status, job.RequestedAt);
    }

    // ------------------------------------------------------------------ AU4

    public async Task<Response.ExportStatus> GetExportAsync(Guid accountId, Guid exportId, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        var job = await LoadJobAsync(exportId, cancellationToken) ?? throw new NotFoundException("Export job not found or expired", "EXPORT_NOT_FOUND");
        var now = clock.GetUtcNow();
        if (IsStuck(job)) (job.Status, job.ErrorMessage) = (Failed, "The export was interrupted. Please request it again.");

        string? url = null; DateTimeOffset? urlExpiresAt = null;
        if (job.Status == Ready)
        {
            var expiresAt = job.CompletedAt!.Value + PackageLifetime;
            if (now >= expiresAt) job.Status = Expired;
            else if (job.Delivery == "DIRECT")
            {
                // A fresh link every time, valid until the package itself expires.
                url = (await storage.CreateExportUrlAsync(job.StorageUrl!, job.FileName!, expiresAt, cancellationToken)).Url;
                urlExpiresAt = expiresAt;
            }
        }
        var pending = job.Status is Queued or Processing or Failed;
        return new(job.ExportId, job.TransportId, job.TransportCode, job.Format, job.Mode, job.Status, job.Status == Ready ? job.Delivery : null, url, urlExpiresAt,
            pending || job.Status == Expired ? null : job.FileName, pending ? null : job.SizeBytes, pending ? null : job.Sha256, job.ErrorMessage, job.RequestedAt,
            pending ? null : job.CompletedAt);
    }

    // ------------------------------------------------------------------ the worker's side

    public async Task ProcessExportAsync(Guid exportId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobAsync(exportId, cancellationToken);
        if (job is null || job.Status != Queued) return;
        job.Status = Processing;
        await SaveJobAsync(job, PendingLifetime, cancellationToken);
        var activeKey = ActiveKey(job.TransportId, job.RequestedByAccountId, job.Format, job.Mode);
        try
        {
            var package = await BuildPackageAsync(job, cancellationToken);
            await using var content = package.Content;
            content.Position = 0;
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken)).ToLowerInvariant();
            var stored = await storage.UploadExportAsync(package.FileName, package.ContentType, content, cancellationToken);
            var completedAt = clock.GetUtcNow();
            (job.Status, job.StorageUrl, job.FileName, job.SizeBytes, job.Sha256, job.CompletedAt) = (Ready, stored.StorageUrl, stored.FileName, stored.SizeBytes, sha, completedAt);
            job.Delivery = stored.SizeBytes > EmailThresholdBytes ? "EMAIL" : "DIRECT";
            if (job.Delivery == "EMAIL") await MailPackageLinkAsync(job, completedAt + PackageLifetime, cancellationToken);
            await SaveJobAsync(job, PackageLifetime, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Audit export {ExportId} for transport {TransportId} failed", job.ExportId, job.TransportId);
            (job.Status, job.ErrorMessage, job.CompletedAt) = (Failed, "The audit package could not be built. Please try again.", clock.GetUtcNow());
            await SaveJobAsync(job, PackageLifetime, CancellationToken.None);
        }
        finally
        {
            await cache.RemoveAsync(activeKey, CancellationToken.None);
        }
    }

    /// <summary>UC-77 EXC: a package above 500 MB reaches the manager as a link in an email and as a notification, not on screen.</summary>
    private async Task MailPackageLinkAsync(ExportJob job, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var manager = await db.Employees.AsNoTracking().Active().Where(item => item.AccountId == job.RequestedByAccountId)
            .Select(item => new { item.FullName, item.Account.Email }).SingleOrDefaultAsync(cancellationToken);
        if (manager is null) return;
        var link = (await storage.CreateExportUrlAsync(job.StorageUrl!, job.FileName!, expiresAt, cancellationToken)).Url;
        var now = Truncate(clock.GetUtcNow());
        notifications.Queue(new NotificationCreateRequest
        {
            AccountId = job.RequestedByAccountId, TransportId = job.TransportId, Type = NotificationType.SYSTEM, Priority = NotificationPriority.NORMAL,
            Title = "Audit package ready", Message = $"The audit package of transport {job.TransportCode} is ready. The link was sent to your email.",
            ReferenceType = "Transport", ReferenceId = job.TransportId, ActionUrl = NotificationLinks.AuditArchive(job.TransportId), CreatedAt = now
        });
        OutboxQueue.AddEmail(db, new()
        {
            To = manager.Email, Subject = $"Audit package ready — {job.TransportCode}",
            Body = EmailBodies.AuditPackageReadyBody(manager.FullName, job.TransportCode, job.FileName!, job.SizeBytes ?? 0, link, expiresAt.UtcDateTime)
        }, now);
        await db.SaveChangesAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
    }
}
