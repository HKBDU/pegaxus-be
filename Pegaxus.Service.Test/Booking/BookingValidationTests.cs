using Pegaxus.Service.BookingService.Validations;

using BookingRequest = Pegaxus.Service.BookingService.Request;

namespace Pegaxus.Service.Test;

public class BookingValidationTests
{
    [Fact]
    public void CreateBooking_RejectsMissingLocationsAndHorseId()
    {
        var result = new CreateBookingRequestValidator().Validate(new BookingRequest.CreateBookingRequest
        {
            Horses = [new BookingRequest.BookingHorseRequest()]
        });

        Assert.Contains(result.Errors, error => error.PropertyName == "OriginAddress");
        Assert.Contains(result.Errors, error => error.PropertyName == "OriginCountry");
        Assert.Contains(result.Errors, error => error.PropertyName == "DestinationAddress");
        Assert.Contains(result.Errors, error => error.PropertyName == "DestinationCountry");
        Assert.Contains(result.Errors, error => error.PropertyName == "Horses[0].HorseId");
    }

    [Fact]
    public void CreateBooking_RequiresAllFourCoordinates()
    {
        var result = new CreateBookingRequestValidator().Validate(new BookingRequest.CreateBookingRequest());

        foreach (var field in new[] { "OriginLatitude", "OriginLongitude", "DestinationLatitude", "DestinationLongitude" })
            Assert.Contains(result.Errors, error => error.PropertyName == field);
    }

    [Fact]
    public void UpdateBooking_RequiresCoordinatesInPairs()
    {
        var validator = new UpdateBookingRequestValidator();

        var origin = validator.Validate(new BookingRequest.UpdateBookingRequest { LastUpdatedAt = DateTimeOffset.UtcNow, OriginLatitude = 1m });
        var destination = validator.Validate(new BookingRequest.UpdateBookingRequest { LastUpdatedAt = DateTimeOffset.UtcNow, DestinationLongitude = 1m });
        var both = validator.Validate(new BookingRequest.UpdateBookingRequest
        {
            LastUpdatedAt = DateTimeOffset.UtcNow, OriginLatitude = 1m, OriginLongitude = 2m, DestinationLatitude = 3m, DestinationLongitude = 4m
        });

        Assert.Contains(origin.Errors, error => error.ErrorMessage.Contains("originLatitude and originLongitude"));
        Assert.Contains(destination.Errors, error => error.ErrorMessage.Contains("destinationLatitude and destinationLongitude"));
        Assert.True(both.IsValid);
    }

    [Fact]
    public void CreateBooking_RejectsEmptyHorseList()
    {
        var result = new CreateBookingRequestValidator().Validate(new BookingRequest.CreateBookingRequest());

        Assert.Contains(result.Errors, error => error.PropertyName == "Horses");
    }

    [Fact]
    public void UpdateBooking_RequiresVersionAndChangedField()
    {
        var result = new UpdateBookingRequestValidator().Validate(new BookingRequest.UpdateBookingRequest());

        Assert.Contains(result.Errors, error => error.PropertyName == "LastUpdatedAt");
        Assert.Contains(result.Errors, error => error.ErrorMessage == "At least one field must be updated.");
    }

    [Fact]
    public void CancelAndReject_RequireReason()
    {
        var cancel = new CancelBookingRequestValidator().Validate(new BookingRequest.CancelBookingRequest());
        var reject = new RejectBookingRequestValidator().Validate(new BookingRequest.RejectBookingRequest());

        Assert.Contains(cancel.Errors, error => error.PropertyName == "Reason");
        Assert.Contains(reject.Errors, error => error.PropertyName == "Reason");
    }

    [Fact]
    public void CreateBooking_ValidatesEuAndUkCountryRules()
    {
        var validator = new CreateBookingRequestValidator();

        // Cross-border EU to EU is valid
        var validEuResult = validator.Validate(new BookingRequest.CreateBookingRequest
        {
            OriginAddress = "123 St",
            OriginLatitude = 1m,
            OriginLongitude = 2m,
            DestinationLatitude = 3m,
            DestinationLongitude = 4m,
            OriginCountry = "France",
            DestinationAddress = "456 St",
            DestinationCountry = "Germany",
            Horses = [new BookingRequest.BookingHorseRequest { HorseId = Guid.NewGuid() }]
        });
        Assert.True(validEuResult.IsValid);

        // Same country domestic must be United Kingdom
        var validUkResult = validator.Validate(new BookingRequest.CreateBookingRequest
        {
            OriginAddress = "London",
            OriginLatitude = 1m,
            OriginLongitude = 2m,
            DestinationLatitude = 3m,
            DestinationLongitude = 4m,
            OriginCountry = "United Kingdom",
            DestinationAddress = "Manchester",
            DestinationCountry = "United Kingdom",
            Horses = [new BookingRequest.BookingHorseRequest { HorseId = Guid.NewGuid() }]
        });
        Assert.True(validUkResult.IsValid);

        // Same country EU (e.g. France to France) is rejected
        var invalidSameCountryEuResult = validator.Validate(new BookingRequest.CreateBookingRequest
        {
            OriginAddress = "Paris",
            OriginLatitude = 1m,
            OriginLongitude = 2m,
            DestinationLatitude = 3m,
            DestinationLongitude = 4m,
            OriginCountry = "France",
            DestinationAddress = "Nice",
            DestinationCountry = "France",
            Horses = [new BookingRequest.BookingHorseRequest { HorseId = Guid.NewGuid() }]
        });
        Assert.False(invalidSameCountryEuResult.IsValid);
        Assert.Contains(invalidSameCountryEuResult.Errors, error => error.PropertyName == "DestinationCountry");

        // Non-EU country (e.g. Vietnam) is rejected
        var invalidNonEuResult = validator.Validate(new BookingRequest.CreateBookingRequest
        {
            OriginAddress = "Hanoi",
            OriginLatitude = 1m,
            OriginLongitude = 2m,
            DestinationLatitude = 3m,
            DestinationLongitude = 4m,
            OriginCountry = "Vietnam",
            DestinationAddress = "Paris",
            DestinationCountry = "France",
            Horses = [new BookingRequest.BookingHorseRequest { HorseId = Guid.NewGuid() }]
        });
        Assert.False(invalidNonEuResult.IsValid);
        Assert.Contains(invalidNonEuResult.Errors, error => error.PropertyName == "OriginCountry");
    }

    [Theory]
    [InlineData("France", "United Kingdom", true)]
    [InlineData("United Kingdom", "France", true)]
    [InlineData("Ireland", "United Kingdom", true)]
    [InlineData("Germany", "United Kingdom", true)]
    [InlineData("  united kingdom ", "Italy", true)]
    [InlineData("United Kingdom", "United Kingdom", true)]
    [InlineData("France", "Germany", true)]
    [InlineData("France", "France", false)]
    [InlineData("Switzerland", "United Kingdom", false)]
    [InlineData("United Kingdom", "Norway", false)]
    [InlineData("Vietnam", "France", false)]
    public void CreateBooking_AcceptsEuAndUnitedKingdomOnEitherEndOfTheRoute(string origin, string destination, bool valid)
    {
        var result = new CreateBookingRequestValidator().Validate(new BookingRequest.CreateBookingRequest
        {
            OriginAddress = "A", OriginLatitude = 1m, OriginLongitude = 2m, OriginCountry = origin,
            DestinationAddress = "B", DestinationLatitude = 3m, DestinationLongitude = 4m, DestinationCountry = destination,
            Horses = [new BookingRequest.BookingHorseRequest { HorseId = Guid.NewGuid() }]
        });

        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData("France", "United Kingdom", "")]
    [InlineData("United Kingdom", "France", "")]
    [InlineData("France", "France", "When origin and destination countries are the same, the country must be United Kingdom.")]
    [InlineData("Switzerland", "United Kingdom", "Origin country 'Switzerland' is not an accepted EU member country or United Kingdom.")]
    [InlineData("France", "Norway", "Destination country 'Norway' is not an accepted EU member country or United Kingdom.")]
    public void ValidateRoute_AllowsEuToUkAndUkToEu_AndExplainsEveryRefusal(string origin, string destination, string message)
    {
        var (isValid, error) = Pegaxus.Service.Common.CountryUtils.ValidateRoute(origin, destination);

        Assert.Equal(message.Length == 0, isValid);
        Assert.Equal(message, error);
    }
}
