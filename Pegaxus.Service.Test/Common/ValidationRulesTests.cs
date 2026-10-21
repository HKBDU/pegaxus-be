using Pegaxus.Service.ComplianceRequirementService.Validator;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.HandoverService.Validator;
using Pegaxus.Service.LocationTrackingService.Validator;

using HorseRequest = Pegaxus.Service.HorseService.Request;
using HorseServiceImpl = Pegaxus.Service.HorseService.Service;
using InvoiceRequest = Pegaxus.Service.InvoiceService.Request;
using TrackingRequest = Pegaxus.Service.LocationTrackingService.Request;

namespace Pegaxus.Service.Test;

/// <summary>Boundary tests for the limits in VALIDATION_UX_AUDIT.md (section 5).</summary>
public class ValidationRulesTests
{
    private static HorseRequest.CreateHorseRequest Horse(decimal weight = 500, decimal height = 160, int ageYears = 3) => new()
    {
        HorseName = "Pegasus", Sex = "MALE", Breed = "Thoroughbred", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-ageYears)),
        Color = "Bay", CountryOfOrigin = "France", WeightKg = weight, HeightCm = height, MicrochipNumber = "123456789012345",
        PassportNumber = "ABC123456789012", IdentificationNotes = "None"
    };

    [Theory]
    [InlineData("0.0000000000000000000001", "160")]
    [InlineData("0", "160")]
    [InlineData("1500.1", "160")]
    [InlineData("500", "49.9")]
    [InlineData("500", "220.1")]
    [InlineData("500.15", "160")]
    public async Task Horse_RejectsOutOfRangeOrTooPreciseMeasurements(string weight, string height)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var request = Horse(decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(height, System.Globalization.CultureInfo.InvariantCulture));

        await Assert.ThrowsAsync<BadRequestException>(() => new HorseServiceImpl(db).CreateHorseAsync(account.AccountId, request));
        Assert.Empty(db.Horses);
    }

    [Fact]
    public async Task Horse_AcceptsBoundaryValuesAndRejectsAHorseOlderThanFortyYears()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db);

        await service.CreateHorseAsync(account.AccountId, Horse(weight: 20, height: 50, ageYears: 40));
        var tooOld = Horse(ageYears: 41);
        tooOld.MicrochipNumber = "123456789012346"; tooOld.PassportNumber = "ABC123456789013";
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateHorseAsync(account.AccountId, tooOld));
    }

    [Theory]
    [InlineData("5000", true)]
    [InlineData("5001", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("1.5", false)]
    [InlineData("99999999", false)]
    public void InvoiceQuantity_IsAWholeNumberUpToFiveThousand(string quantity, bool valid)
    {
        var request = InvoiceTests.Fixture.Request();
        request.Items = [InvoiceTests.Fixture.Item(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), 1000)];

        var result = new Pegaxus.Service.InvoiceService.Validator.CreateRequestValidator(TimeProvider.System).Validate(request);

        Assert.Equal(valid, result.Errors.All(error => !error.PropertyName.Contains("Quantity")));
    }

    [Fact]
    public void InvoiceDueDate_MustBeWithinTenYears()
    {
        var request = InvoiceTests.Fixture.Request();
        request.DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(11);

        Assert.False(new Pegaxus.Service.InvoiceService.Validator.CreateRequestValidator(TimeProvider.System).Validate(request).IsValid);
    }

    [Theory]
    [InlineData("10.1234", "20.1234", "78.5", "311.5", "12", true)]
    [InlineData("0", "0", "78.5", "311.5", "12", false)]
    [InlineData("10.12345", "20.1234", "78.5", "311.5", "12", false)]
    [InlineData("10", "20", "1000.1", "311.5", "12", false)]
    [InlineData("10", "20", "78", "360", "12", false)]
    [InlineData("10", "20", "78", "359.9", "12", true)]
    [InlineData("10", "20", "78", "100", "10000.1", false)]
    public void Tracking_EnforcesCoordinateSpeedHeadingAndAccuracyLimits(string latitude, string longitude, string speed, string heading, string accuracy, bool valid)
    {
        static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var request = new TrackingRequest.LocationRequest
        {
            Latitude = D(latitude), Longitude = D(longitude), SpeedKmh = D(speed), HeadingDegrees = D(heading), AccuracyMeters = D(accuracy),
            RecordedAt = DateTimeOffset.UtcNow, RoutePlanRouteId = Guid.NewGuid()
        };

        Assert.Equal(valid, new LocationRequestValidator(TimeProvider.System).Validate(request).IsValid);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(3651, false)]
    [InlineData(0, false)]
    public void ComplianceRequirement_ValidityPeriodIsCappedAtTenYears(int days, bool valid)
    {
        var result = new SaveRequestValidator().Validate(new Pegaxus.Service.ComplianceRequirementService.Request.SaveRequest { ValidityPeriodDays = days });

        Assert.Equal(valid, result.Errors.All(error => error.PropertyName != "ValidityPeriodDays"));
    }

    [Fact]
    public void ComplianceRequirement_LimitsFreeTextColumns()
    {
        var result = new SaveRequestValidator().Validate(new Pegaxus.Service.ComplianceRequirementService.Request.SaveRequest
        { Description = new string('a', 2001), LegalBasis = new string('b', 1001) });

        Assert.Contains(result.Errors, error => error.PropertyName == "Description");
        Assert.Contains(result.Errors, error => error.PropertyName == "LegalBasis");
    }

    [Fact]
    public void VehicleExpiryDates_AndEquipmentNotes_AreBounded()
    {
        var request = VehicleServiceTests.ValidRequest();
        request.RegistrationExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(11);
        request.SpecialEquipment = new string('x', 2001);

        var errors = new Pegaxus.Service.VehicleService.Validator.CreateRequestValidator(TimeProvider.System).Validate(request).Errors;

        Assert.Contains(errors, error => error.PropertyName == "RegistrationExpiryDate");
        Assert.Contains(errors, error => error.PropertyName == "SpecialEquipment");
    }

    [Fact]
    public void HandoverCompletion_LimitsNotesAndPhones()
    {
        var request = new Pegaxus.Service.HandoverService.Request.CompleteRequest
        {
            LastUpdatedAt = DateTimeOffset.UtcNow, HorseConditionNote = new string('a', 2001), DocumentHandoverNote = "ok", EquipmentHandoverNote = "ok",
            FromContactPhone = "12345"
        };

        var errors = new CompleteRequestValidator().Validate(request).Errors;

        Assert.Contains(errors, error => error.PropertyName == "HorseConditionNote");
        Assert.Contains(errors, error => error.PropertyName == "FromContactPhone");
    }

    [Fact]
    public async Task AuthoritySubmission_RejectsOverlongFieldsAndFutureTimestamps()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var proof = DocumentUploadTests.File("proof.pdf", "application/pdf");

        var tooLong = await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthoritySubmissionAsync(specialist, dossierId, new()
        { AuthorityName = new string('a', 201), SubmissionReference = "REF-1", SubmittedAt = DateTimeOffset.UtcNow, ProofFile = proof }));
        Assert.Contains("200", tooLong.Message);
        await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthoritySubmissionAsync(specialist, dossierId, new()
        { AuthorityName = "Authority", SubmissionReference = "REF-2", SubmittedAt = DateTimeOffset.UtcNow.AddDays(1), ProofFile = proof }));
        Assert.Empty(db.AuthoritySubmissions);
    }

    [Fact]
    public async Task AuthorityDecision_RequiresExpiryOnOrAfterIssueAndShortReferences()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var submission = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, new()
        { AuthorityName = "Authority", SubmissionReference = "REF-3", SubmittedAt = DateTimeOffset.UtcNow, ProofFile = DocumentUploadTests.File("proof.pdf", "application/pdf") });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthorityDecisionAsync(specialist, submission.AuthoritySubmissionId, new()
        { Decision = "APPROVED", PermitNumber = "P-1", PermitIssuedDate = today, PermitExpiryDate = today.AddDays(-1), DecisionFile = DocumentUploadTests.File("d.pdf", "application/pdf") }));
        await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthorityDecisionAsync(specialist, submission.AuthoritySubmissionId, new()
        { Decision = "APPROVED", PermitNumber = new string('p', 101), DecisionFile = DocumentUploadTests.File("d.pdf", "application/pdf") }));
        await Assert.ThrowsAsync<BadRequestException>(() => service.RecordAuthorityDecisionAsync(specialist, submission.AuthoritySubmissionId, new()
        { Decision = "APPROVED", PermitNumber = "P-1", PermitExpiryDate = today.AddYears(11), DecisionFile = DocumentUploadTests.File("d.pdf", "application/pdf") }));
    }

    [Theory]
    [InlineData("not-an-email", "+33123456789", "INVALID_EMAIL")]
    [InlineData("ok@example.com", "0900000000", "INVALID_PHONE")]
    public async Task EmployeeCreate_ValidatesEmailAndPhone(string email, string phone, string code)
    {
        await using var db = TestSupport.CreateDb();
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = new Pegaxus.Service.EmployeeService.Service(db, new Microsoft.AspNetCore.Identity.PasswordHasher<Pegaxus.Repository.Entities.Account>());

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(
            new() { Role = "DRIVER", Email = email, Phone = phone, FullName = "Valid Name" }, manager.AccountId));

        Assert.Equal(code, error.ErrorCode);
    }

    [Theory]
    [InlineData(15, false)]
    [InlineData(16, true)]
    [InlineData(121, false)]
    [InlineData(120, true)]
    public async Task AccountBirthDate_RequiresAgeBetweenSixteenAndOneTwenty(int ageYears, bool valid)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new Pegaxus.Service.AccountService.Service(db, new NoMedia(), new Microsoft.AspNetCore.Identity.PasswordHasher<Pegaxus.Repository.Entities.Account>());
        var request = new Pegaxus.Service.AccountService.Request.UpdateProfileRequest { DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-ageYears) };

        if (valid) await service.UpdateProfileAsync(account.AccountId, request);
        else await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateProfileAsync(account.AccountId, request));
    }

    private sealed class NoMedia : Pegaxus.Service.MediaService.IMediaService
    {
        public Task<string> UploadPrivateImageAsync(Guid imageId, Pegaxus.Service.MediaService.PreviewFile image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pegaxus.Service.MediaService.SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
        public Task<Pegaxus.Service.MediaService.ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pegaxus.Service.MediaService.ConfirmedUploadResult> ConfirmAvatarAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pegaxus.Service.DocumentStorageService.SignedDocumentUrl> CreateReadUrlAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pegaxus.Service.MediaService.PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
