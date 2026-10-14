using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Controllers;
using Pegaxus.API.Models;
using Pegaxus.Service.BookingService.Validations;
using BookingRequest = Pegaxus.Service.BookingService.Request;

namespace Pegaxus.Service.Test;

public class BookingControllerValidationTests
{
    [Fact]
    public async Task Update_WithOnlyVersion_ReturnsValidationErrorForRootRule()
    {
        var controller = new BookingController(null!);
        var request = new BookingRequest.UpdateBookingRequest { LastUpdatedAt = DateTimeOffset.UtcNow };

        var result = await controller.Update(Guid.NewGuid(), request, new UpdateBookingRequestValidator(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<ApiResponse>(badRequest.Value);
        Assert.Equal("VALIDATION_ERROR", body.ErrorCode);
        var errors = JsonSerializer.SerializeToElement(body.Errors).EnumerateArray().ToArray();
        var error = Assert.Single(errors);
        Assert.Equal("$", error.GetProperty("field").GetString());
        Assert.Equal("At least one field must be updated.", error.GetProperty("message").GetString());
    }
}
