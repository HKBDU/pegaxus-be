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
              {Button(actionUrl, "View booking")}
              {Muted("If you believe this is a mistake, please contact our support team.")}
        """ +
        Footer();

    /// <summary>Staff alert: a customer submitted a booking for review, or cancelled one that was waiting.</summary>
    public static string BookingNeedsReviewBody(
        string staffName, string customerName, string bookingCode, string route, DateOnly? departureDate,
        bool cancelled, string? reason, string? actionUrl) =>
        Notice(
            cancelled ? "Booking cancelled" : "Booking to review",
            cancelled ? $"{customerName} cancelled booking {bookingCode}" : $"{customerName} submitted booking {bookingCode}",
            bookingCode, staffName,
            cancelled
                ? $"{Strong(customerName)} cancelled this booking request."
                : $"{Strong(customerName)} submitted a new booking request. Please review it and approve or reject it.",
            cancelled ? Tone.Info : Tone.Warning, cancelled ? "Cancelled" : "Action required",
            [Row("Booking code", Accent(bookingCode)), Row("Customer", Encode(customerName)), Row("Route", Encode(route)),
             Row("Planned departure", Accent(FmtDate(departureDate)))],
            reason, "Reason", actionUrl, cancelled ? "View booking" : "Review booking");

    /// <summary>An approved booking was cancelled or returned to draft by the hotline change. Sent to the customer and to the assigned specialists.</summary>
    public static string BookingChangedBody(
        string recipientName, string bookingCode, string route, bool cancelled, string reason, bool forStaff, string? actionUrl) =>
        Notice(
            cancelled ? "Booking cancelled" : "Booking returned to draft",
            cancelled ? $"Booking {bookingCode} was cancelled" : $"Booking {bookingCode} was returned to draft",
            bookingCode, recipientName,
            (cancelled, forStaff) switch
            {
                (true, true) => "An approved booking was cancelled after a change request. Work on its compliance dossier is suspended.",
                (true, false) => "Your approved booking has been cancelled following a change request.",
                (false, true) => "An approved booking was returned to draft so it can be changed. Work on its compliance dossier is suspended.",
                _ => "Your approved booking was returned to draft so it can be changed. Please review it and submit it again."
            },
            cancelled ? Tone.Error : Tone.Warning, cancelled ? "Cancelled" : forStaff ? "Suspended" : "Action required",
            [Row("Booking code", Accent(bookingCode)), Row("Route", Encode(route))],
            reason, "Reason", actionUrl, "View booking");
}
