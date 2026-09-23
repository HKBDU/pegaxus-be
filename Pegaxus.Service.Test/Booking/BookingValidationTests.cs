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
}
