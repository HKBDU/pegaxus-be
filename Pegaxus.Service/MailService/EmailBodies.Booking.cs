namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    public static string BookingApprovedBody(
        string customerName, string bookingCode, string transportCode,
        DateOnly? departureDate, string? actionUrl) =>
        Header("Booking approved", "Your Pegaxus booking has been approved") +
        $"""
              {Title("Booking approved", bookingCode)}
              {Greeting(customerName)}
              {Paragraph("Good news! Your booking has been approved and a transport trip has been created.")}
              {SuccessPill("Approved")}
              {Rows(
                  Row("Booking code", Accent(bookingCode)),
                  Row("Transport code", Encode(transportCode)),
                  Row("Planned departure", Accent(FmtDate(departureDate))))}
              {Button(actionUrl, "View details")}
        """ +
        Footer();

    public static string BookingRejectedBody(
        string customerName, string bookingCode, string reason, string? actionUrl) =>
        Header("Booking rejected", "Your Pegaxus booking could not be approved") +
        $"""
              {Title("Booking rejected", bookingCode)}
              {Greeting(customerName)}
              {Paragraph("Unfortunately, we were unable to approve your booking request.")}
              {ErrorPill("Rejected")}
              {Rows(Row("Booking code", Accent(bookingCode)))}
              {LabeledParagraph("Reason", reason)}
              {Button(actionUrl, "Contact support")}
              {Muted("If you believe this is a mistake, please contact our support team.")}
        """ +
        Footer();
}
