namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    /// <summary>The customer's claim was filed; this is the confirmation.</summary>
    public static string ClaimReceivedBody(string customerName, string claimCode, string bookingCode, string claimTitle, string? actionUrl) =>
        Notice("Claim received", $"We received claim {claimCode}", claimCode, customerName,
            "We have received your claim. Our team will review it and let you know the outcome.", Tone.Info, "Received",
            [Row("Claim code", Accent(claimCode)), Row("Booking code", Encode(bookingCode)), Row("Subject", Encode(claimTitle))],
            null, "Note", actionUrl, "View claim");

    /// <summary>The manager decided the claim. Only the outcome and its reasoning are shown, never the internal note.</summary>
    public static string ClaimResultBody(string customerName, string claimCode, string bookingCode, bool approved, string resolutionSummary,
        long? approvedAmount, string currency, string? actionUrl) =>
        Notice(approved ? "Claim approved" : "Claim rejected", $"The outcome of claim {claimCode}", claimCode, customerName,
            approved ? "Your claim has been approved." : "We have reviewed your claim and could not approve it.",
            approved ? Tone.Success : Tone.Error, approved ? "Approved" : "Rejected",
            [Row("Claim code", Accent(claimCode)), Row("Booking code", Encode(bookingCode)),
             approvedAmount is { } amount ? Row("Approved amount", Accent(FmtMoney(amount, currency))) : null],
            resolutionSummary, "Decision", actionUrl, "View claim");

    /// <summary>The compensation was carried out and the claim is closed.</summary>
    public static string ClaimClosedBody(string customerName, string claimCode, string bookingCode, string? actionUrl) =>
        Notice("Claim closed", $"Claim {claimCode} is closed", claimCode, customerName,
            "The compensation for your claim has been completed and the claim is now closed.", Tone.Success, "Closed",
            [Row("Claim code", Accent(claimCode)), Row("Booking code", Encode(bookingCode))],
            null, "Note", actionUrl, "View claim");
}
