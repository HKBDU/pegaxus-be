using Pegaxus.Service.Common;

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

    public sealed class ApprovedBookingListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Search { get; set; }
        public bool? IsInternational { get; set; }
        public string? TransportPlanStatus { get; set; }
        public string? RoutePlanStatus { get; set; }
        public bool? HasRoutePlan { get; set; }
        public bool? HasTransportPlan { get; set; }
        public bool? HasComplianceDossier { get; set; }
        public string? ComplianceDossierStatus { get; set; }
        public DateOnly? DepartureFrom { get; set; }
        public DateOnly? DepartureTo { get; set; }
        public string? SortBy { get; set; }
    }

    public class CreateBookingRequest
    {
        public string OriginAddress { get; set; } = string.Empty;
        /// <summary>A name or a 2-letter ISO code of an EU member country or the United Kingdom; stored and returned as the name.</summary>
        public string OriginCountry { get => _originCountry; set => _originCountry = CountryUtils.BookingCountry(value)!; }
        public string DestinationAddress { get; set; } = string.Empty;
        /// <summary>A name or a 2-letter ISO code of an EU member country or the United Kingdom; stored and returned as the name.</summary>
        public string DestinationCountry { get => _destinationCountry; set => _destinationCountry = CountryUtils.BookingCountry(value)!; }
        private string _originCountry = string.Empty;
        private string _destinationCountry = string.Empty;
        public decimal? OriginLatitude { get; set; }
        public decimal? OriginLongitude { get; set; }
        public decimal? DestinationLatitude { get; set; }
        public decimal? DestinationLongitude { get; set; }
        public DateOnly RequestedDepartureDate { get; set; }
        public DateOnly? RequestedArrivalDate { get; set; }
        public List<BookingHorseRequest> Horses { get; set; } = [];
        public string? SpecialRequirements { get; set; }
        public string? CustomerNote { get; set; }
        /// <summary>Set to book the return trip of a horse that this transport left behind.</summary>
        public Guid? ReturnOfTransportId { get; set; }
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
        /// <summary>A name or a 2-letter ISO code of an EU member country or the United Kingdom; stored and returned as the name.</summary>
        public string? OriginCountry { get => _originCountry; set => _originCountry = CountryUtils.BookingCountry(value); }
        public string? DestinationAddress { get; set; }
        /// <summary>A name or a 2-letter ISO code of an EU member country or the United Kingdom; stored and returned as the name.</summary>
        public string? DestinationCountry { get => _destinationCountry; set => _destinationCountry = CountryUtils.BookingCountry(value); }
        private string? _originCountry;
        private string? _destinationCountry;
        public decimal? OriginLatitude { get; set; }
        public decimal? OriginLongitude { get; set; }
        public decimal? DestinationLatitude { get; set; }
        public decimal? DestinationLongitude { get; set; }
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
