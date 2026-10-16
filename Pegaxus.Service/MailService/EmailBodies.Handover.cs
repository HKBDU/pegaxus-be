namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    public static string TransportDeliveredBody(string customerName, string transportCode, DateTime completedAt, string? actionUrl) =>
        Header("Transport completed", "Your horses have been delivered") +
        $"""
              {Title("Your horses have been delivered", transportCode)}
              {Greeting(customerName)}
              {Paragraph("Your transport is complete. Thank you for choosing Pegaxus.")}
              {SuccessPill("Delivered")}
              {Rows(Row("Transport code", Accent(transportCode)), Row("Completed at", Encode(FmtDateTime(completedAt))),
                  Row("Claim window ends", Encode(FmtDateTime(completedAt.AddHours(24)))))}
              {Muted("You may submit a claim within 24 hours of the completed handover.")}
              {Button(actionUrl, "View transport")}
        """ + Footer();
}
