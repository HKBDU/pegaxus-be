using Pegaxus.Service.Exceptions;
using HorseServiceImpl = Pegaxus.Service.HorseService.Service;
using HorseRequest = Pegaxus.Service.HorseService.Request;
using Xunit;

namespace Pegaxus.Service.Test.HorseTests;

public class HorseValidationTests
{
    [Fact]
    public async Task CreateHorse_ValidatesMicrochipAndPassportFormats()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db);

        // Invalid microchip format (not 15 digits)
        var invalidMicrochipRequest = new HorseRequest.CreateHorseRequest
        {
            HorseName = "Pegasus",
            Sex = "MALE",
            Breed = "Thoroughbred",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-3)),
            Color = "Bay",
            CountryOfOrigin = "France",
            WeightKg = 500,
            HeightCm = 160,
            MicrochipNumber = "12345", // Only 5 digits
            PassportNumber = "ABC123456789012",
            IdentificationNotes = "None"
        };

        var microchipException = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateHorseAsync(account.AccountId, invalidMicrochipRequest));
        Assert.Contains("Microchip Number must be a valid 15-digit ISO 11784/11785 microchip number", microchipException.Message);

        // Invalid passport format (not 15 alphanumeric characters)
        var invalidPassportRequest = new HorseRequest.CreateHorseRequest
        {
            HorseName = "Pegasus",
            Sex = "MALE",
            Breed = "Thoroughbred",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-3)),
            Color = "Bay",
            CountryOfOrigin = "France",
            WeightKg = 500,
            HeightCm = 160,
            MicrochipNumber = "123456789012345",
            PassportNumber = "SHORT_PASSPORT", // Invalid length
            IdentificationNotes = "None"
        };

        var passportException = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateHorseAsync(account.AccountId, invalidPassportRequest));
        Assert.Contains("Passport Number must be a valid 15-character EU passport/UELN number", passportException.Message);

        // Valid ISO 15-digit microchip & 15-char passport
        var validRequest = new HorseRequest.CreateHorseRequest
        {
            HorseName = "Pegasus",
            Sex = "MALE",
            Breed = "Thoroughbred",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-3)),
            Color = "Bay",
            CountryOfOrigin = "France",
            WeightKg = 500,
            HeightCm = 160,
            MicrochipNumber = "985123456789012",
            PassportNumber = "276098100123456",
            IdentificationNotes = "None"
        };

        var created = await service.CreateHorseAsync(account.AccountId, validRequest);
        Assert.NotEqual(Guid.Empty, created.HorseId);
    }
}
