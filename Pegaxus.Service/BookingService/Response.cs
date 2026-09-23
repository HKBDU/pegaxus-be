using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.BookingService;

public class Response
{
    public class CreateBookingResponse
    {
        public Guid BookingId { get; set; }
        public required string BookingCode { get; set; }
    }

    public class UpdateBookingResponse
    {
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public class ApproveBookingResponse
    {
        public BookingStatus Status { get; set; }
        public required TransportResponse Transport { get; set; }
    }

    public class TransportResponse
    {
        public Guid TransportId { get; set; }
        public required string TransportCode { get; set; }
        public TransportStatus Status { get; set; }
    }

}
