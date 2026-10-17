namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    /// <summary>How an issued invoice is presented: quotations and credit notes are not payable by the customer.</summary>
    public enum InvoiceMailKind { Invoice, Quotation, CreditNote }

    public static string InvoiceIssuedBody(
        string customerName, string invoiceCode, string invoiceType, string bookingCode,
        InvoiceMailKind kind, decimal totalAmount, string currency, DateOnly? dueDate, string? actionUrl)
    {
        var title = kind switch
        {
            InvoiceMailKind.Quotation => "Quotation issued",
            InvoiceMailKind.CreditNote => "Credit note issued",
            _ => "Invoice issued"
        };
        var intro = kind switch
        {
            InvoiceMailKind.Quotation => "A quotation has been prepared for your booking. It is only an estimate. No payment is required.",
            InvoiceMailKind.CreditNote => "A credit note has been issued for your booking.",
            _ => "A new invoice has been issued for your booking. Please pay it before the due date."
        };
        var amountLabel = kind switch
        {
            InvoiceMailKind.Quotation => "Estimated total",
            InvoiceMailKind.CreditNote => "Payout or credit amount",
            _ => "Amount due"
        };

        return Header(title, $"{title}: {invoiceCode}") +
            $"""
                  {Title(title, invoiceCode)}
                  {Greeting(customerName)}
                  {Paragraph(intro)}
                  {(kind == InvoiceMailKind.Invoice ? WarningPill("Payment required") : InfoPill(kind == InvoiceMailKind.Quotation ? "No payment required" : "Credit note"))}
                  {Rows(
                      Row("Invoice code", Accent(invoiceCode)),
                      Row("Type", Encode(invoiceType)),
                      Row("Booking", Encode(bookingCode)),
                      Row(amountLabel, Accent(FmtMoney(totalAmount, currency))),
                      kind == InvoiceMailKind.Invoice ? Row("Due date", Accent(FmtDate(dueDate))) : null)}
                  {Button(actionUrl, kind == InvoiceMailKind.Invoice ? "View and pay" : "View details")}
            """ +
            Footer();
    }

    public static string InvoiceCancelledBody(
        string customerName, string invoiceCode, string invoiceType, string reason, string? actionUrl) =>
        Header("Invoice cancelled", $"Invoice {invoiceCode} has been cancelled") +
        $"""
              {Title("Invoice cancelled", invoiceCode)}
              {Greeting(customerName)}
              {Paragraph("The following invoice has been cancelled. You do not need to pay it.")}
              {ErrorPill("Cancelled")}
              {Rows(
                  Row("Invoice code", Accent(invoiceCode)),
                  Row("Type", Encode(invoiceType)))}
              {LabeledParagraph("Reason", reason)}
              {Button(actionUrl, "View details")}
              {Muted("Please contact support if you need more information.")}
        """ +
        Footer();

    /// <summary>Payment recorded for an invoice, or a payout recorded for a credit note.</summary>
    public static string PaymentConfirmedBody(
        string customerName, string invoiceCode, string receiptCode, decimal amount, string currency,
        bool isPayout, string? actionUrl) =>
        Header(isPayout ? "Payout confirmed" : "Payment confirmed", $"{(isPayout ? "Payout" : "Payment")} for invoice {invoiceCode} confirmed") +
        $"""
              {Title(isPayout ? "Payout confirmed" : "Payment confirmed", invoiceCode)}
              {Greeting(customerName)}
              {Paragraph(isPayout
                  ? "Your payout has been confirmed and the invoice is now settled."
                  : "Thank you. Your payment has been confirmed and the invoice is now paid.")}
              {SuccessPill("Paid")}
              {Rows(
                  Row("Invoice code", Accent(invoiceCode)),
                  Row("Receipt", Accent(receiptCode)),
                  Row("Amount", Accent(FmtMoney(amount, currency))))}
              {Button(actionUrl, "View invoice")}
        """ +
        Footer();

    /// <summary>Internal alert for a Logistics Manager: gateway money that cannot be matched to an open invoice.</summary>
    public static string PaymentReconciliationBody(
        string managerName, string reason, string receiptCode, string receiptStatus, string gatewayReference,
        decimal amount, string currency, string invoiceCode, string invoiceStatus, string? actionUrl) =>
        Header("Payment needs reconciliation", $"Check receipt {receiptCode}") +
        $"""
              {Title("Payment needs reconciliation", receiptCode)}
              {Greeting(managerName)}
              {Paragraph(Encode(reason) + ".")}
              {ErrorPill("Action required")}
              {Rows(
                  Row("Receipt", Accent(receiptCode) + " (" + Encode(receiptStatus) + ")"),
                  Row("Gateway reference", Encode(gatewayReference)),
                  Row("Amount", Accent(FmtMoney(amount, currency))),
                  Row("Invoice", Accent(invoiceCode) + " (" + Encode(invoiceStatus) + ")"))}
              {Paragraph("Check the bank statement, then refund or reconcile the payment manually.")}
              {Button(actionUrl, "Open invoice")}
        """ +
        Footer();

    /// <summary>The invoice passed its due date without payment.</summary>
    public static string InvoiceOverdueBody(
        string customerName, string invoiceCode, string bookingCode, decimal amount, string currency, DateOnly? dueDate, string? actionUrl) =>
        Notice("Invoice overdue", $"Invoice {invoiceCode} is past its due date", invoiceCode, customerName,
            "This invoice is past its due date. Please pay it as soon as possible.", Tone.Error, "Overdue",
            [Row("Invoice code", Accent(invoiceCode)), Row("Booking code", Encode(bookingCode)),
             Row("Amount due", Accent(FmtMoney(amount, currency))), Row("Due date", Accent(FmtDate(dueDate)))],
            null, "Note", actionUrl, "Pay invoice");
}
