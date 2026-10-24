using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.HandoverService;
using Pegaxus.Service.MediaService;

using Audit = Pegaxus.Service.AuditService;
using Handover = Pegaxus.Service.HandoverService;

namespace Pegaxus.Service.Test;

/// <summary>Seed data and fakes shared by the audit archive, audit view and audit export tests.</summary>
internal static class AuditSupport
{
    internal static DateTime D(string value) => ReportTests.D(value);

    /// <summary>One in-memory store behind both storage interfaces: a file is a url, and a missing url fails like a lost object.</summary>
    internal sealed class Storage : IMediaService, IDocumentStorageService
    {
        internal Dictionary<string, byte[]> Files { get; } = [];
        internal Dictionary<string, byte[]> Exports { get; } = [];
        internal List<string> Signed { get; } = [];

        public Task<PreviewFile> DownloadPreviewAsync(string url, CancellationToken cancellationToken = default) =>
            Files.TryGetValue(url, out var bytes) ? Task.FromResult(new PreviewFile(bytes, "application/octet-stream")) : throw new IOException("Object not found: " + url);

        internal bool FailUpload { get; set; }

        public Task<StoredDocumentFile> UploadExportAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken = default)
        {
            if (FailUpload) throw new IOException("The bucket is unreachable");
            using var copy = new MemoryStream();
            content.Position = 0; content.CopyTo(copy);
            var url = $"s3://test-bucket/exports/{Guid.NewGuid():N}/{fileName}";
            Exports[url] = copy.ToArray();
            return Task.FromResult(new StoredDocumentFile(url, fileName, contentType, ReportedSize ?? copy.Length));
        }

        /// <summary>Set to make the next uploads report a different size, to test the 500 MB mail rule without a real 500 MB file.</summary>
        internal long? ReportedSize { get; set; }

        public Task<SignedDocumentUrl> CreateExportUrlAsync(string storageUrl, string fileName, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            Signed.Add(storageUrl);
            return Task.FromResult(new SignedDocumentUrl($"https://signed.example.com/{fileName}?exp={expiresAt.ToUnixTimeSeconds()}", expiresAt));
        }

        public Task<SignedDocumentUrl> CreateReadUrlAsync(string storageUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ValidateContentAsync(PreviewFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredDocumentFile> UploadAsync(Guid versionId, int sortOrder, IFormFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storageUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
        public Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConfirmedUploadResult> ConfirmAvatarAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Only the e-POD is used by the export; asking for one of a handover in <see cref="Missing"/> fails.</summary>
    internal sealed class Handovers : IHandoverService
    {
        internal HashSet<Guid> Missing { get; } = [];
        public Task<Handover.Response.EpodFile> GetEpodAsync(Guid handoverRecordId, string? format, Guid accountId, CancellationToken cancellationToken = default) =>
            Missing.Contains(handoverRecordId) ? throw new InvalidOperationException("e-POD failed") : Task.FromResult(new Handover.Response.EpodFile("%PDF-epod"u8.ToArray(), "epod.pdf"));
        public Task<Handover.Response.DetailResponse> CreateAsync(Guid transportId, Handover.Request.CreateRequest request, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Handover.Response.ListResponse> GetListAsync(Guid transportId, Handover.Request.ListRequest request, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Handover.Response.DetailResponse> GetAsync(Guid handoverRecordId, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Handover.Response.DetailResponse> StartAsync(Guid handoverRecordId, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Handover.Response.DetailResponse> CancelAsync(Guid handoverRecordId, Handover.Request.CancelRequest request, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Handover.Response.DetailResponse> CompleteAsync(Guid handoverRecordId, Handover.Request.CompleteRequest request, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class Fixture
    {
        private int counter;

        internal required ReportTests.Fixture R { get; init; }
        internal required FinancialReportTests.FixedClock Clock { get; init; }
        internal required IDistributedCache Cache { get; init; }
        internal required Storage Files { get; init; }
        internal required Handovers EpodMaker { get; init; }
        internal required Audit.AuditExportQueue Queue { get; init; }
        internal required Transport Transport { get; init; }
        internal required Account OtherCustomerAccount { get; init; }
        internal required Employee Driver { get; init; }
        internal required Guid PickupId { get; init; }
        internal required Guid BorderId { get; init; }
        internal required Guid DeliveryId { get; init; }

        internal AppDbContext Db => R.Context;
        internal Guid ManagerId => R.ManagerAccount.AccountId;
        internal Guid CustomerId => R.CustomerAccount.AccountId;

        internal Audit.Service Service => For(Db);

        internal Audit.Service For(AppDbContext context) => new(context, Clock, Cache, Files, Files, EpodMaker, TestSupport.CreateNotifications(context), Queue, NullLogger<Audit.Service>.Instance);

        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var r = await ReportTests.Fixture.CreateAsync(db);
            var (otherAccount, other) = await TestSupport.AddCustomerAsync(db);
            var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
            r.Horse.HorseName = "Pegasus King"; r.Horse.MicrochipNumber = "985120012345678"; r.Horse.PassportNumber = "FR-2020-0001"; r.Horse.WeightKg = 520.5m; r.Horse.HeightCm = 165;
            var transport = r.AddTransport("2026-10-14 16:05", "2026-10-14 14:00", "2026-10-14 16:05", "2026-10-11 06:30");
            db.BookingHorses.Add(new BookingHorse { BookingId = transport.BookingId, Booking = transport.Booking, HorseId = r.Horse.HorseId, Horse = r.Horse, CreatedAt = D("2026-09-01 00:00") });
            await db.SaveChangesAsync();

            var fixture = new Fixture
            {
                R = r, Clock = new(new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.Zero)), Cache = TestSupport.CreateCache(), Files = new(), EpodMaker = new(), Queue = new(),
                Transport = transport, OtherCustomerAccount = otherAccount, Driver = driver,
                PickupId = Guid.NewGuid(), BorderId = Guid.NewGuid(), DeliveryId = Guid.NewGuid()
            };
            fixture.Seed(other);
            await db.SaveChangesAsync();
            return fixture;
        }

        private void Seed(Customer other)
        {
            var db = Db; var created = D("2026-09-01 00:00");
            var plan = db.TransportPlans.Local.Single(item => item.TransportId == Transport.TransportId);

            // Routing: an internal leg driven by an employee, a leg bought from a partner, and a cancelled alternative plan.
            var provider = R.AddProvider("Eurotunnel");
            var primary = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = plan.TransportPlanId, RoutePlanCode = "RP-MAIN", Status = RoutePlanStatus.COMPLETED, TotalDistanceKm = 1200,
                EstimatedDurationMinutes = 4320, CreatedAt = created, UpdatedAt = created };
            var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = plan.TransportPlanId, PrimaryRoutePlanId = primary.RoutePlanId, RoutePlanCode = "RP-BACKUP",
                Status = RoutePlanStatus.CANCELLED, CreatedAt = created.AddDays(1), UpdatedAt = created.AddDays(1) };
            var own = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = primary.RoutePlanId, Route = R.Route, SequenceNo = 1, RouteType = RouteType.PRIMARY, DriverEmployeeId = Driver.EmployeeId,
                PlannedDepartureAt = D("2026-10-11 06:00"), PlannedArrivalAt = D("2026-10-12 06:00"), ActualDepartureAt = D("2026-10-11 06:30"), ActualArrivalAt = D("2026-10-12 07:00"),
                CreatedAt = created, UpdatedAt = created };
            var bought = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = primary.RoutePlanId, Route = R.Route, SequenceNo = 2, RouteType = RouteType.PRIMARY,
                TransportProviderId = provider.TransportProviderId, OutsourcingCostAmount = 4_000_000, OutsourcingCurrency = "VND", PlannedDepartureAt = D("2026-10-12 08:00"),
                PlannedArrivalAt = D("2026-10-14 15:00"), ActualDepartureAt = D("2026-10-12 08:15"), ActualArrivalAt = D("2026-10-14 16:05"), CreatedAt = created, UpdatedAt = created };
            db.AddRange(primary, backup, own, bought, new RoutePlanRouteVehicle { RoutePlanRouteId = own.RoutePlanRouteId, VehicleId = R.Vehicle.VehicleId, AssignedAt = created });

            // Handovers: pickup and delivery are visible to the customer, the border one is not.
            db.AddRange(NewHandover(PickupId, 1, HandoverType.PICKUP, "2026-10-11 06:30"), NewHandover(BorderId, 2, HandoverType.BORDER_HANDOVER, "2026-10-12 12:00"),
                NewHandover(DeliveryId, 3, HandoverType.DELIVERY, "2026-10-14 16:05"));

            // Incidents: a minor horse one, a serious vehicle one with an approved emergency cost, and a cancelled one.
            R.AddIncident(Transport, "2026-10-13 09:00", IncidentCategory.HORSE_HEALTH, IncidentSeverity.LOW, horseType: HorseIncidentType.FEVER);
            var serious = R.AddIncident(Transport, "2026-10-12 10:00", IncidentCategory.VEHICLE, IncidentSeverity.HIGH, vehicleType: VehicleIncidentType.BREAKDOWN, repair: true);
            R.AddCost(serious, EmergencyCostCategory.VEHICLE_REPAIR, 3_500_000);
            R.AddIncident(Transport, "2026-10-13 12:00", IncidentCategory.VEHICLE, IncidentSeverity.CRITICAL, IncidentStatus.CANCELLED);

            // Claims: one live (against the delivery) and one cancelled.
            db.AddRange(Claim("CLM-LIVE", ClaimStatus.RESOLVED, "2026-10-15 09:00"), Claim("CLM-GONE", ClaimStatus.CANCELLED, "2026-10-15 10:00"));

            // Money: an internal quotation, the final invoice with its receipt, a draft nobody may see, and the saved report.
            Invoice(InvoiceType.QUOTATION, InvoiceStatus.ISSUED, 1_000_000);
            var final = Invoice(InvoiceType.FINAL, InvoiceStatus.PAID, 5_000_000);
            Invoice(InvoiceType.FINAL, InvoiceStatus.DRAFT, 9_000_000);
            db.Receipts.Add(new Receipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-1", InvoiceId = final.InvoiceId, Amount = 5_000_000, Currency = "VND", PaymentMethod = PaymentMethod.CASH,
                Status = ReceiptStatus.CONFIRMED, PaidAt = D("2026-10-20 09:00"), ConfirmedAt = D("2026-10-20 09:05"), CreatedAt = created, UpdatedAt = created });
            db.FinancialReports.Add(new FinancialReport { FinancialReportId = Guid.NewGuid(), ReportCode = "FR-1", TransportId = Transport.TransportId, Currency = "VND", TotalInvoicedAmount = 5_000_000,
                TotalPaidAmount = 5_000_000, TotalEmergencyCost = 3_500_000, OutstandingAmount = 0, SummaryNote = "Healthy margin", GeneratedAt = D("2026-10-21 09:00"), CreatedAt = created, UpdatedAt = created });

            // Health logs: the veterinarian's own note is for managers only.
            db.AddRange(HealthLog("2026-10-12 08:00", HorseHealthStatus.NORMAL, null), HealthLog("2026-10-13 08:00", HorseHealthStatus.AT_RISK, "Slight fever, monitor"));

            SeedCompliance();
        }

        private HandoverRecord NewHandover(Guid id, int sequence, HandoverType type, string completedAt) => new()
        {
            HandoverRecordId = id, TransportId = Transport.TransportId, Transport = Transport, HandoverCode = $"HO-{sequence}", SequenceNo = sequence, HandoverType = type, Status = HandoverStatus.COMPLETED,
            FromType = HandoverPartyType.EMPLOYEE, ToType = HandoverPartyType.CUSTOMER, CompletedAt = D(completedAt), CreatedAt = D("2026-09-01 00:00"), UpdatedAt = D("2026-09-01 00:00")
        };

        private Claim Claim(string code, ClaimStatus status, string submittedAt) => new()
        {
            ClaimId = Guid.NewGuid(), ClaimCode = code, CustomerId = R.Customer.CustomerId, HandoverRecordId = DeliveryId, ClaimType = ClaimType.DAMAGE, Status = status, Title = "Scratch on the flank",
            Description = "A scratch was found at delivery", EvidenceNote = "Photos sent by email", SubmittedAt = D(submittedAt), CreatedAt = D(submittedAt), UpdatedAt = D(submittedAt)
        };

        private Invoice Invoice(InvoiceType type, InvoiceStatus status, decimal total)
        {
            var at = D("2026-10-16 09:00").AddMinutes(++counter);
            var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = $"INV-{counter:0000}", BookingId = Transport.BookingId, EmployeeId = R.Manager.EmployeeId, Type = type, Status = status,
                Currency = "VND", Subtotal = total, TotalAmount = total, IssuedAt = status == InvoiceStatus.DRAFT ? null : at, CreatedAt = at, UpdatedAt = at };
            Db.Invoices.Add(invoice);
            return invoice;
        }

        private HorseHealthLog HealthLog(string checkedAt, HorseHealthStatus status, string? vetNote) => new()
        {
            HorseHealthLogId = Guid.NewGuid(), TransportId = Transport.TransportId, HorseId = R.Horse.HorseId, EmployeeId = R.Manager.EmployeeId, LogType = HorseHealthLogType.ROUTINE_CHECK,
            CheckedAt = D(checkedAt), HealthStatus = status, TemperatureCelsius = 38.1m, HeartRateBpm = 40, VeterinarianRequired = vetNote is not null, VeterinarianNote = vetNote,
            PhotoUrls = ["photo-1", "photo-2"], GeneralNote = "Calm", CreatedAt = D(checkedAt), UpdatedAt = D(checkedAt)
        };

        /// <summary>The MAIN dossier: an approved certificate (two versions, two files), a pending one, both proofs and two audit records.</summary>
        private void SeedCompliance()
        {
            var db = Db; var created = D("2026-09-02 00:00"); var customer = CustomerId; var manager = ManagerId;
            var category = Enum.GetValues<ComplianceRequirementCategory>().First();
            var dossier = new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-MAIN", TransportId = Transport.TransportId, DossierType = DossierType.MAIN,
                Status = ComplianceDossierStatus.APPROVED, OpenedAt = created, ApprovedAt = D("2026-10-05 10:00"), CreatedAt = created, UpdatedAt = created };
            DossierRequirement Requirement(string code, string name, DossierRequirementStatus status) => new()
            {
                DossierRequirementId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId, IsMandatory = true, Status = status, CreatedAt = created.AddMinutes(code.Length), UpdatedAt = created,
                ComplianceRequirement = new ComplianceRequirement { ComplianceRequirementId = Guid.NewGuid(), RequirementCode = code, RequirementName = name, Category = category, CreatedAt = created, UpdatedAt = created }
            };
            var approvedRequirement = Requirement("EHC", "EHC Certificate", DossierRequirementStatus.SATISFIED);
            var pendingRequirement = Requirement("COGGINS", "Coggins Test", DossierRequirementStatus.PROVIDED);
            ComplianceDocument Document(DossierRequirement requirement, string code, string name, ComplianceDocumentStatus status) => new()
            {
                ComplianceDocumentId = Guid.NewGuid(), DossierRequirementId = requirement.DossierRequirementId, DocumentCode = code, DocumentName = name, Status = status, DocumentNumber = code + "-100",
                IssuingAuthority = "Official Vet", CreatedByAccountId = customer, UpdatedByAccountId = customer, CreatedAt = created, UpdatedAt = created
            };
            var approved = Document(approvedRequirement, "CD-EHC", "EHC Certificate", ComplianceDocumentStatus.APPROVED);
            var pending = Document(pendingRequirement, "CD-COG", "Coggins Test", ComplianceDocumentStatus.PENDING);
            DocumentVersion Version(ComplianceDocument document, int number) => new()
            {
                DocumentVersionId = Guid.NewGuid(), ComplianceDocumentId = document.ComplianceDocumentId, VersionNo = number, FileName = $"{document.DocumentCode}-{number}.pdf", FileUrl = $"s3://test-bucket/documents/{document.DocumentCode}/{number}.pdf",
                MimeType = "application/pdf", UploadedByAccountId = customer, UploadedAt = created.AddHours(number), CreatedAt = created
            };
            var first = Version(approved, 1); var second = Version(approved, 2); var coggins = Version(pending, 1);
            db.AddRange(dossier, approvedRequirement, pendingRequirement, approved, pending, first, second, coggins);
            db.AddRange(
                new DocumentReview { DocumentReviewId = Guid.NewGuid(), DocumentVersionId = first.DocumentVersionId, ReviewedByAccountId = manager, Result = DocumentReviewResult.REJECTED, ReviewComment = "Blurry scan", ReviewedAt = created.AddHours(2), CreatedAt = created },
                new DocumentReview { DocumentReviewId = Guid.NewGuid(), DocumentVersionId = second.DocumentVersionId, ReviewedByAccountId = manager, Result = DocumentReviewResult.APPROVED, ReviewComment = "OK", ReviewedAt = created.AddHours(4), CreatedAt = created },
                new DocumentAttachment { DocumentAttachmentId = Guid.NewGuid(), DocumentVersionId = second.DocumentVersionId, FileName = "page-1.pdf", FileUrl = "s3://test-bucket/documents/CD-EHC/2-a.pdf", SortOrder = 0, CreatedAt = created },
                new DocumentAttachment { DocumentAttachmentId = Guid.NewGuid(), DocumentVersionId = second.DocumentVersionId, FileName = "page-2.pdf", FileUrl = "https://res.cloudinary.com/test/raw/authenticated/ehc-2b", SortOrder = 1, CreatedAt = created });
            Files.Files["s3://test-bucket/documents/CD-EHC/2-a.pdf"] = "%PDF-ehc-page-1"u8.ToArray();
            Files.Files["https://res.cloudinary.com/test/raw/authenticated/ehc-2b"] = "%PDF-ehc-page-2"u8.ToArray();
            Files.Files["s3://test-bucket/documents/CD-COG/1.pdf"] = "%PDF-coggins"u8.ToArray();

            var quarantine = new QuarantineProof { QuarantineProofId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId, Status = QuarantineProofStatus.PendingVerification,
                SubmittedByEmployeeId = R.Manager.EmployeeId, BcpName = "Calais BCP", OfficialVeterinarianName = "Dr. Martin", VerificationNote = "Checked at the border post", SubmittedAt = created, CreatedAt = created, UpdatedAt = created };
            var customs = new CustomsClearanceProof { CustomsClearanceProofId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId, SubmittedByEmployeeId = R.Manager.EmployeeId,
                CustomsPortName = "Dover", MrnNumber = "MRN-1", SubmittedAt = created, CreatedAt = created, UpdatedAt = created };
            db.AddRange(quarantine, customs,
                new ProofAttachment { ProofAttachmentId = Guid.NewGuid(), QuarantineProofId = quarantine.QuarantineProofId, FileName = "bcp.pdf", FileUrl = "https://res.cloudinary.com/test/raw/authenticated/bcp", SortOrder = 0, CreatedAt = created },
                new ProofAttachment { ProofAttachmentId = Guid.NewGuid(), CustomsClearanceProofId = customs.CustomsClearanceProofId, FileName = "customs.pdf", FileUrl = "https://res.cloudinary.com/test/raw/authenticated/customs", SortOrder = 0, CreatedAt = created });
            Files.Files["https://res.cloudinary.com/test/raw/authenticated/bcp"] = "%PDF-bcp"u8.ToArray();
            // The customs file is left out of storage on purpose: a lost object must be listed as skipped, not fail the package.

            db.AddRange(
                new AuditDossier { AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId, ActionType = "HANDOVER_COMPLETED", TargetEntity = "HandoverRecord", Summary = "Delivery handover completed",
                    ActorAccountId = manager, Visibility = AuditVisibility.Public, CreatedAt = D("2026-10-14 16:06") },
                new AuditDossier { AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId, ActionType = "DOCUMENT_REVIEWED", TargetEntity = "DocumentVersion", Summary = "Internal review notes",
                    ActorAccountId = manager, Visibility = AuditVisibility.Internal, CreatedAt = D("2026-10-05 09:00") });
        }
    }
}
