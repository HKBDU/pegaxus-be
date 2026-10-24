namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    public static string TransportDeliveredBody(string customerName, string transportCode, DateTime completedAt, string? actionUrl, bool hasDiscrepancy = false) =>
        Header("Transport completed", "Your horses have been delivered") +
        $"""
              {Title("Your horses have been delivered", transportCode)}
              {Greeting(customerName)}
              {Paragraph("Your transport is complete. Thank you for choosing Pegaxus.")}
              {SuccessPill("Delivered")}
              {Rows(Row("Transport code", Accent(transportCode)), Row("Completed at", Encode(FmtDateTime(completedAt))),
                  Row("Claim window ends", Encode(FmtDateTime(completedAt.AddHours(24)))))}
              {(hasDiscrepancy
                  ? Paragraph("The handover record contains on-site notes. If anything does not match the condition of your horses, please submit a claim within 24 hours of the completed handover.")
                  : Muted("You may submit a claim within 24 hours of the completed handover."))}
              {Paragraph("The signed handover record (e-POD) can be downloaded from your transport page.")}
              {Button(actionUrl, "View transport and download e-POD")}
        """ + Footer();
}
