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

    /// <summary>
    /// Customer alert: the escort recorded a horse as critical or as needing a veterinarian. Only the horse name and the
    /// condition are shown; the veterinarian's note stays internal.
    /// </summary>
    public static string HorseHealthCustomerBody(
        string customerName, string bookingCode, string transportCode, string horseName, bool critical, bool veterinarianRequired,
        string? actionUrl, string? contactName = null)
    {
        var condition = (critical, veterinarianRequired) switch
        {
            (true, true) => "is in a critical condition and needs a veterinarian",
            (true, false) => "is in a critical condition",
            _ => "needs a veterinarian"
        };
        return Notice("Your horse needs urgent attention", $"{horseName} {condition}", transportCode, customerName,
            $"{Strong(horseName)} {condition}. Our team is responding and will keep you updated.", Tone.Error, "Urgent",
            [Row("Booking code", Accent(bookingCode)), Row("Transport code", Accent(transportCode))],
            null, "Note", actionUrl, "View transport", contactName: contactName);
    }

    /// <summary>A handover was scheduled for the recipient, or cancelled.</summary>
    public static string HandoverBody(string recipientName, string handoverCode, bool cancelled, string? detail, string? actionUrl) =>
        Notice(cancelled ? "Handover cancelled" : "Handover scheduled", $"Handover {handoverCode} {(cancelled ? "was cancelled" : "is scheduled")}",
            handoverCode, recipientName,
            cancelled ? "This handover was cancelled. A new handover needs to be scheduled." : "A handover has been scheduled for you.",
            cancelled ? Tone.Warning : Tone.Info, cancelled ? "Cancelled" : "Scheduled",
            [Row("Handover code", Accent(handoverCode))], detail, "Details", actionUrl, "View handover");
}
