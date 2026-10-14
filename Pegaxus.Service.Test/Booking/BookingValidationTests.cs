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
            OriginCountry = "Vietnam",
            DestinationAddress = "Paris",
            DestinationCountry = "France",
            Horses = [new BookingRequest.BookingHorseRequest { HorseId = Guid.NewGuid() }]
        });
        Assert.False(invalidNonEuResult.IsValid);
        Assert.Contains(invalidNonEuResult.Errors, error => error.PropertyName == "OriginCountry");
    }
}
