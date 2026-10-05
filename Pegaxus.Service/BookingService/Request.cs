using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.BookingService;

public class Request
{
    public sealed class NegotiateBookingRequest
    {
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class AddHorseRequest
    {
        public Guid HorseId { get; set; }
        public string? SpecialHandlingNotes { get; set; }
    }

    public class UpdateHorseNotesRequest
    {
        public string? SpecialHandlingNotes { get; set; }
    }

    public class ChangeStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    public class BookingListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Search { get; set; }
        public string? Status { get; set; }
        public List<string>? SortBy { get; set; }
    }

    public class CreateBookingRequest
    {
        public string OriginAddress { get; set; } = string.Empty;
        public string OriginCountry { get; set; } = string.Empty;
        public string DestinationAddress { get; set; } = string.Empty;
        public string DestinationCountry { get; set; } = string.Empty;
        public DateOnly RequestedDepartureDate { get; set; }
        public DateOnly? RequestedArrivalDate { get; set; }
        public List<BookingHorseRequest> Horses { get; set; } = [];
        public string? SpecialRequirements { get; set; }
        public string? CustomerNote { get; set; }
    }

    public class BookingHorseRequest
    {
        public Guid HorseId { get; set; }
        public string? SpecialHandlingNotes { get; set; }
    }

    public class UpdateBookingRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string? OriginAddress { get; set; }
        public string? OriginCountry { get; set; }
        public string? DestinationAddress { get; set; }
        public string? DestinationCountry { get; set; }
        public DateOnly? RequestedDepartureDate { get; set; }
        public DateOnly? RequestedArrivalDate { get; set; }
        public string? SpecialRequirements { get; set; }
        public string? CustomerNote { get; set; }
    }

    public class CancelBookingRequest
    {
        public string Reason { get; set; } = string.Empty;
    }

    public class RejectBookingRequest
    {
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class AssignBookingEmployeeRequest
    {
        public Guid EmployeeId { get; set; }
        public string Role { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

}
