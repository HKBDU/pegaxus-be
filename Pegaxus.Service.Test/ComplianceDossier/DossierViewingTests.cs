using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

using DossierService = Pegaxus.Service.ComplianceDossierService.Service;

namespace Pegaxus.Service.Test;

public class DossierViewingTests
{
    [Fact]
    public async Task RequirementListController_ReturnsFlatPaginatedContract()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var controller = new ComplianceDossiersController(CreateService(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, account.AccountId.ToString())], "test"))
            } }
        };
        var response = Assert.IsType<OkObjectResult>(await controller.GetRequirements(dossier.ComplianceDossierId, new() { PageSize = 3 }, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var value = json.RootElement.GetProperty("value");
        Assert.Equal(3, value.GetProperty("items").GetArrayLength());
        Assert.Equal(8, value.GetProperty("totalItems").GetInt32());
        var action = typeof(ComplianceDossiersController).GetMethod(nameof(ComplianceDossiersController.GetRequirements))!;
        Assert.Equal("{dossierId:guid}/requirements", Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()).Template);
        Assert.Equal(JwtExtensions.ComplianceDossierViewerPolicy, Assert.Single(action.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>()).Policy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overview_ReturnsBookingAndCustomerMetadata(bool byBooking)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        db.ChangeTracker.Clear();
        var service = CreateService(db);
        var result = byBooking
            ? await service.GetByBookingAsync(account.AccountId, dossier.Transport.BookingId, new())
            : await service.GetDossierAsync(account.AccountId, dossier.ComplianceDossierId, new());
        Assert.Equal(account.CustomerAccount!.FullName, result.BookingSummary.CustomerName);
        Assert.Equal(account.CustomerAccount.CustomerId, result.Customer.CustomerId);
        Assert.Equal(account.CustomerAccount.FullName, result.Customer.FullName);
        Assert.Equal(dossier.Transport.Booking.RequestedDepartureDate, result.BookingSummary.RequestedDepartureDate);
        Assert.Equal(dossier.Transport.Booking.Status.ToString(), result.BookingSummary.BookingStatus);
        Assert.Equal(dossier.Transport.Booking.BookingHorses.Count, result.BookingSummary.HorseCount);
        Assert.DoesNotContain("password", JsonSerializer.Serialize(result).ToLowerInvariant());
    }

    [Fact]
    public async Task RequirementList_PaginatesBothScopes_ExcludesDeleted_ProtectsOwnership()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var requirements = dossier.DossierRequirements.ToList();
        requirements[0].IsDeleted = true;
        requirements[1].Note = "Customer correction required";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = CreateService(db);
        var all = await service.GetRequirementsAsync(account.AccountId, dossier.ComplianceDossierId, new() { PageSize = 100 });
        Assert.Equal(7, all.TotalItems);
        Assert.DoesNotContain(all.Items, item => item.DossierRequirementId == requirements[0].DossierRequirementId);
        Assert.Contains(all.Items, item => item.HorseId.HasValue);
        Assert.Contains(all.Items, item => !item.HorseId.HasValue);
        Assert.Contains(all.Items, item => item.Note == "Customer correction required");
        var page = await service.GetRequirementsAsync(account.AccountId, dossier.ComplianceDossierId, new() { PageIndex = 2, PageSize = 3 });
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(all.Items.Skip(3).Take(3).Select(item => item.DossierRequirementId), page.Items.Select(item => item.DossierRequirementId));
        var horseId = all.Items.First(item => item.HorseId.HasValue).HorseId;
        var filtered = await service.GetRequirementsAsync(account.AccountId, dossier.ComplianceDossierId, new() { HorseId = horseId, Status = "REJECTED", SortBy = "dueDate:desc" });
        Assert.All(filtered.Items, item => { Assert.Equal(horseId, item.HorseId); Assert.Equal("REJECTED", item.Status); });
        var (foreign, _) = await TestSupport.AddCustomerAsync(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetRequirementsAsync(foreign.AccountId, dossier.ComplianceDossierId, new()));
    }

    [Theory]
    [InlineData(0, 20, null, null)]
    [InlineData(1, 101, null, null)]
    [InlineData(int.MaxValue, 100, null, null)]
    [InlineData(1, 20, "999", null)]
    [InlineData(1, 20, null, "unknown:asc")]
    public async Task RequirementList_RejectsInvalidFilters(int pageIndex, int pageSize, string? status, string? sortBy)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db).GetRequirementsAsync(account.AccountId, dossier.ComplianceDossierId,
            new() { PageIndex = pageIndex, PageSize = pageSize, Status = status, SortBy = sortBy }));
    }

    [Fact]
    public async Task Customer_SeesOwnDossierWithWholeProgressAndHorseGroups()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var result = await CreateService(db).GetByBookingAsync(account.AccountId, dossier.Transport.BookingId, new());
        Assert.True(result.IsInitialized);
        Assert.Equal(8, result.RequirementCount);
        Assert.Equal(5, result.FulfilledCount);
        Assert.Equal(62.5m, result.CompletionPercent);
        Assert.Equal("IN_PREPARATION", result.Status);
        Assert.Equal(4, result.TripRequirements.Count);
        Assert.All(result.TripRequirements, item => Assert.Equal(3, item.LeadTimeDays));
        var horses = Assert.Single(result.HorseRequirements);
        Assert.Equal("Silver Arrow", horses.HorseName);
        Assert.Equal(4, horses.Requirements.Count);
        Assert.True(result.Actions.CanUpload);
        Assert.False(result.Actions.CanReview);
    }

    [Fact]
    public async Task Filters_DoNotChangeWholeDossierProgress()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var horseId = dossier.DossierRequirements.First(item => item.HorseId.HasValue).HorseId;
        var result = await CreateService(db).GetDossierAsync(account.AccountId, dossier.ComplianceDossierId,
            new() { Status = "rejected", HorseId = horseId });
        Assert.Equal(62.5m, result.CompletionPercent);
        Assert.Equal(8, result.RequirementCount);
        Assert.Empty(result.TripRequirements);
        Assert.Equal("REJECTED", Assert.Single(Assert.Single(result.HorseRequirements).Requirements).Status);
    }

    [Theory]
    [InlineData(DossierRequirementStatus.PENDING)]
    [InlineData(DossierRequirementStatus.PROVIDED)]
    [InlineData(DossierRequirementStatus.SATISFIED)]
    [InlineData(DossierRequirementStatus.REJECTED)]
    [InlineData(DossierRequirementStatus.REQUESTED)]
    [InlineData(DossierRequirementStatus.WAIVED)]
    public async Task EveryRequirementStatus_CanBeViewedAndFiltered(DossierRequirementStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var requirement = dossier.DossierRequirements.First();
        requirement.Status = status;
        await db.SaveChangesAsync();
        var result = await CreateService(db).GetDossierAsync(account.AccountId, dossier.ComplianceDossierId,
            new() { Status = status.ToString() });
        Assert.Contains(result.TripRequirements, item => item.DossierRequirementId == requirement.DossierRequirementId);
        Assert.All(result.TripRequirements.Concat(result.HorseRequirements.SelectMany(item => item.Requirements)),
            item => Assert.Equal(status.ToString(), item.Status));
    }

    [Theory]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, true)]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, false)]
    public async Task Staff_CanViewAnyBookingWithCorrectActionFlags(EmployeeRole role, bool canReview)
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier) = await SeedAsync(db);
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var result = await service.GetByBookingAsync(account.AccountId, dossier.Transport.BookingId, new());
        Assert.False(result.Actions.CanUpload);
        Assert.Equal(canReview, result.Actions.CanReview);
        var detail = await service.GetRequirementAsync(account.AccountId, dossier.ComplianceDossierId,
            dossier.DossierRequirements.First().DossierRequirementId);
        Assert.False(detail.Actions.CanUpload);
        Assert.Equal(canReview, detail.Actions.CanReview);
    }

    [Fact]
    public async Task ForeignCustomer_IsForbiddenOnEveryReadBeforeFileDownload()
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier) = await SeedAsync(db);
        var (foreign, _) = await TestSupport.AddCustomerAsync(db);
        var media = new RecordingMediaService();
        var service = CreateService(db, media);
        var requirement = dossier.DossierRequirements.First();
        var version = requirement.ComplianceDocument!.DocumentVersions.First();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetByBookingAsync(foreign.AccountId, dossier.Transport.BookingId, new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDossierAsync(foreign.AccountId, dossier.ComplianceDossierId, new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetRequirementAsync(foreign.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDocumentPreviewAsync(foreign.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDocumentPreviewAsync(foreign.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, version.DocumentAttachments.First().DocumentAttachmentId));
        Assert.Empty(media.DownloadedUrls);
    }

    [Theory]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    public async Task OtherStaffRoles_AreForbidden(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier) = await SeedAsync(db);
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDossierAsync(account.AccountId, dossier.ComplianceDossierId, new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetRequirementAsync(account.AccountId, dossier.ComplianceDossierId,
            dossier.DossierRequirements.First().DossierRequirementId));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("profile")]
    [InlineData("inactive")]
    public async Task UnavailableCustomer_CannotView(string unavailable)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        account.IsDeleted = unavailable == "account";
        account.CustomerAccount!.IsDeleted = unavailable == "profile";
        if (unavailable == "inactive") account.Status = AccountStatus.INACTIVE;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateService(db).GetDossierAsync(account.AccountId, dossier.ComplianceDossierId, new()));
    }

    [Fact]
    public async Task BookingWithoutDossier_ReturnsInitializationMessageOnlyToOwner()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var (foreign, _) = await TestSupport.AddCustomerAsync(db);
        var service = CreateService(db);
        var result = await service.GetByBookingAsync(account.AccountId, booking.BookingId, new());
        Assert.False(result.IsInitialized);
        Assert.Null(result.ComplianceDossierId);
        Assert.Equal("ComplianceDossier has not been initialized. Waiting for transport booking approval.", result.Message);
        Assert.False(result.Actions.CanUpload);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetByBookingAsync(foreign.AccountId, booking.BookingId, new()));
    }

    [Fact]
    public async Task Detail_ReturnsDocumentVersionsReviewsAndProtectedPreviewPaths()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var result = await CreateService(db).GetRequirementAsync(account.AccountId, dossier.ComplianceDossierId,
            dossier.DossierRequirements.First().DossierRequirementId);
        Assert.True(result.Actions.CanUpload);
        Assert.False(result.Actions.CanReview);
        Assert.NotNull(result.Document);
        Assert.Equal("EHC-100", result.Document.DocumentNumber);
        Assert.Equal(new[] { 2, 1 }, result.Document.Versions.Select(item => item.VersionNo));
        var version = result.Document.Versions.First();
        Assert.EndsWith($"/{version.DocumentVersionId}/preview", version.PreviewUrl);
        Assert.Single(version.Attachments);
        Assert.Contains("/attachments/", version.Attachments[0].PreviewUrl);
        Assert.Equal("REJECTED", Assert.Single(version.Reviews).Result);
        Assert.Equal("Upload a readable copy", version.Reviews[0].ReviewComment);
        Assert.DoesNotContain("res.cloudinary.com", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Preview_BindsEveryIdAndExcludesDeletedRequirements()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var requirement = dossier.DossierRequirements.First();
        var version = requirement.ComplianceDocument!.DocumentVersions.First();
        var attachment = version.DocumentAttachments.First();
        var media = new RecordingMediaService();
        var service = CreateService(db, media);
        var file = await service.GetDocumentPreviewAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, null);
        Assert.Equal("application/pdf", file.ContentType);
        await service.GetDocumentPreviewAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, attachment.DocumentAttachmentId);
        Assert.Equal(new[] { version.FileUrl, attachment.FileUrl }, media.DownloadedUrls);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDocumentPreviewAsync(account.AccountId, dossier.ComplianceDossierId,
            dossier.DossierRequirements.Last().DossierRequirementId, version.DocumentVersionId, null));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDocumentPreviewAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Guid.NewGuid()));
        requirement.IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetRequirementAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDocumentPreviewAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, null));
        Assert.Equal(2, media.DownloadedUrls.Count);
        var overview = await service.GetDossierAsync(account.AccountId, dossier.ComplianceDossierId, new());
        Assert.Equal(7, overview.RequirementCount);
        Assert.Equal(4, overview.FulfilledCount);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("999")]
    public async Task InvalidStatusFilter_IsRejected(string status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db).GetDossierAsync(account.AccountId,
            dossier.ComplianceDossierId, new() { Status = status }));
    }

    [Fact]
    public async Task PreviewController_RequiresAccountClaimAndReturnsSafeInlineHeaders()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var requirement = dossier.DossierRequirements.First();
        var version = requirement.ComplianceDocument!.DocumentVersions.First();
        var controller = new ComplianceDossiersController(CreateService(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetDocumentPreview(dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, null, default));
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, account.AccountId.ToString())], "test"));
        var result = Assert.IsType<FileContentResult>(await controller.GetDocumentPreview(dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, null, default));
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("sandbox", controller.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Equal("inline", controller.Response.Headers["Content-Disposition"].ToString());
        var cache = typeof(ComplianceDossiersController).GetMethod("GetDocumentPreview")!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), false).Cast<ResponseCacheAttribute>().Single();
        Assert.True(cache.NoStore);
    }

    [Fact]
    public async Task MissingDocumentAndEmptyRequirements_AreSafeToView()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var service = CreateService(db);
        var detail = await service.GetRequirementAsync(account.AccountId, dossier.ComplianceDossierId,
            dossier.DossierRequirements.Last().DossierRequirementId);
        Assert.Null(detail.Document);
        foreach (var requirement in dossier.DossierRequirements) requirement.IsDeleted = true;
        await db.SaveChangesAsync();
        var result = await service.GetDossierAsync(account.AccountId, dossier.ComplianceDossierId, new());
        Assert.Equal(0, result.RequirementCount);
        Assert.Equal(0m, result.CompletionPercent);
        Assert.Empty(result.TripRequirements);
        Assert.Empty(result.HorseRequirements);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    public async Task UnavailableEmployee_IsForbidden(string unavailable)
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier) = await SeedAsync(db);
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.IsActive = unavailable != "inactive";
        employee.IsDeleted = unavailable == "deleted";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateService(db).GetDossierAsync(account.AccountId, dossier.ComplianceDossierId, new()));
    }

    [Fact]
    public void ReadEndpoints_UseViewerPolicy()
    {
        foreach (var name in new[] { "GetByBooking", "GetDossier", "GetRequirement", "GetDocumentPreview" })
            Assert.Equal(JwtExtensions.ComplianceDossierViewerPolicy,
                typeof(ComplianceDossiersController).GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), false)
                    .Cast<AuthorizeAttribute>().Single().Policy);
    }

    [Fact]
    public async Task S3Preview_UsesPrivateStorageInsteadOfCloudinary()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier) = await SeedAsync(db);
        var requirement = dossier.DossierRequirements.First();
        var version = requirement.ComplianceDocument!.DocumentVersions.First();
        version.FileUrl = "s3://private-bucket/documents/version/00.pdf?versionId=original";
        var attachment = version.DocumentAttachments.First();
        attachment.FileUrl = "s3://private-bucket/documents/version/01.pdf?versionId=page";
        await db.SaveChangesAsync();
        var storage = new RecordingDocumentStorage();
        var media = new RecordingMediaService();
        var service = CreateService(db, media, storage);
        var result = await service.GetDocumentPreviewAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, null);
        Assert.Equal("application/pdf", result.ContentType);
        await service.GetDocumentPreviewAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, attachment.DocumentAttachmentId);
        Assert.Equal(new[] { version.FileUrl, attachment.FileUrl }, storage.PreviewedUrls);
        Assert.Empty(media.DownloadedUrls);
    }

    private static async Task<(Account Account, Pegaxus.Repository.Entities.ComplianceDossier Dossier)> SeedAsync(AppDbContext db)
    {
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        var dossier = new Pegaxus.Repository.Entities.ComplianceDossier
        {
            ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-VIEW", Status = ComplianceDossierStatus.IN_PREPARATION,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TP-VIEW", Booking = booking }
        };
        for (var index = 0; index < 8; index++)
            dossier.DossierRequirements.Add(new DossierRequirement
            {
                DossierRequirementId = Guid.NewGuid(), IsMandatory = true, Horse = index >= 4 ? horse : null,
                Status = index < 5 ? DossierRequirementStatus.SATISFIED : index == 5 ? DossierRequirementStatus.REJECTED : DossierRequirementStatus.PENDING,
                Instruction = "Provide a readable copy", DueDate = booking.RequestedDepartureDate.AddDays(-3),
                ComplianceRequirement = new ComplianceRequirement
                {
                    ComplianceRequirementId = Guid.NewGuid(), RequirementCode = $"CR-{index}", RequirementName = $"Requirement {index}"
                }
            });
        var requirement = dossier.DossierRequirements.First();
        requirement.ComplianceDocument = new ComplianceDocument
        {
            ComplianceDocumentId = Guid.NewGuid(), DocumentCode = "CD-VIEW", DocumentName = "EHC", DocumentNumber = "EHC-100",
            CreatedByAccountId = account.AccountId, UpdatedByAccountId = account.AccountId
        };
        for (var versionNo = 1; versionNo <= 2; versionNo++)
            requirement.ComplianceDocument.DocumentVersions.Add(new DocumentVersion
            {
                DocumentVersionId = Guid.NewGuid(), VersionNo = versionNo, FileName = "ehc.pdf", MimeType = "application/pdf",
                FileUrl = $"https://res.cloudinary.com/test-cloud/raw/upload/ehc-{versionNo}.pdf", UploadedByAccountId = account.AccountId,
                UploadedAt = DateTime.UtcNow,
                DocumentReviews = [new DocumentReview
                {
                    DocumentReviewId = Guid.NewGuid(), ReviewedByAccountId = account.AccountId,
                    Result = DocumentReviewResult.REJECTED, ReviewComment = "Upload a readable copy", ReviewedAt = DateTime.UtcNow
                }],
                DocumentAttachments = [new DocumentAttachment
                {
                    DocumentAttachmentId = Guid.NewGuid(), FileName = "attachment.pdf", MimeType = "application/pdf",
                    FileUrl = $"https://res.cloudinary.com/test-cloud/raw/upload/attachment-{versionNo}.pdf"
                }]
            });
        db.ComplianceDossiers.Add(dossier);
        await db.SaveChangesAsync();
        return (account, dossier);
    }

    private static DossierService CreateService(AppDbContext db, RecordingMediaService? media = null, RecordingDocumentStorage? storage = null) =>
        new(db, media ?? new RecordingMediaService(), new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(), null!,
            storage ?? new RecordingDocumentStorage(), Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance);

    private sealed class RecordingDocumentStorage : Pegaxus.Service.DocumentStorageService.IDocumentStorageService
    {
        public Task ValidateContentAsync(PreviewFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public List<string> PreviewedUrls { get; } = [];
        public Task<PreviewFile> DownloadPreviewAsync(string storageUrl, CancellationToken cancellationToken = default)
        {
            PreviewedUrls.Add(storageUrl);
            return Task.FromResult(new PreviewFile("%PDF-1.7"u8.ToArray(), "application/pdf"));
        }
        public Task<Pegaxus.Service.DocumentStorageService.StoredDocumentFile> UploadAsync(Guid versionId, int sortOrder, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pegaxus.Service.DocumentStorageService.SignedDocumentUrl> CreateReadUrlAsync(string storageUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storageUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingMediaService : Pegaxus.Service.MediaService.IMediaService
    {
        public List<string> DownloadedUrls { get; } = [];
        public Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default)
        {
            DownloadedUrls.Add(fileUrl);
            return Task.FromResult(new PreviewFile("%PDF-1.7"u8.ToArray(), "application/pdf"));
        }
        public Task<string> UploadImageAsync(Microsoft.AspNetCore.Http.IFormFile file) => throw new NotSupportedException();
        public Task<(string FileUrl, string PublicId)> UploadFileAsync(Microsoft.AspNetCore.Http.IFormFile file) => throw new NotSupportedException();
        public Task DeleteFileAsync(string publicId) => throw new NotSupportedException();
        public Task<string> UploadPrivateImageAsync(Guid imageId, Pegaxus.Service.MediaService.PreviewFile image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
        public Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pegaxus.Service.DocumentStorageService.SignedDocumentUrl> CreateReadUrlAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
