namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    /// <summary>The horses were picked up or delivered.</summary>
    public static string TransportMilestoneBody(
        string customerName, string bookingCode, string transportCode, bool delivered, DateTime? at, string? actionUrl) =>
        Notice(delivered ? "Horses delivered" : "Horses picked up",
            delivered ? $"Your horses for booking {bookingCode} were delivered" : $"Your horses for booking {bookingCode} were picked up",
            transportCode, customerName,
            delivered ? "Your horses have been delivered and the transport has ended." : "Your horses have been picked up and the transport has started.",
            Tone.Success, delivered ? "Delivered" : "In transit",
            [Row("Booking code", Accent(bookingCode)), Row("Transport code", Encode(transportCode)), Row(delivered ? "Delivered" : "Picked up", Accent(FmtDateTime(at)))],
            null, "Note", actionUrl, "View transport");

    /// <summary>Urgent alert: a horse reported in a critical condition during the trip.</summary>
    public static string HorseHealthAlertBody(string staffName, string transportCode, string summary, string? actionUrl) =>
        Notice("Critical horse health alert", $"Critical health alert on transport {transportCode}", transportCode, staffName,
            Encode(summary), Tone.Error, "Urgent", [Row("Transport code", Accent(transportCode))], null, "Note", actionUrl, "Open health log");

    /// <summary>A handover was scheduled for the recipient, or cancelled.</summary>
    public static string HandoverBody(string recipientName, string handoverCode, bool cancelled, string? detail, string? actionUrl) =>
        Notice(cancelled ? "Handover cancelled" : "Handover scheduled", $"Handover {handoverCode} {(cancelled ? "was cancelled" : "is scheduled")}",
            handoverCode, recipientName,
            cancelled ? "This handover was cancelled. A new handover needs to be scheduled." : "A handover has been scheduled for you.",
            cancelled ? Tone.Warning : Tone.Info, cancelled ? "Cancelled" : "Scheduled",
            [Row("Handover code", Accent(handoverCode))], detail, "Details", actionUrl, "View handover");
}
