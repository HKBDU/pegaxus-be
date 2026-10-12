using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Pegaxus.Service;

const string SmtpHost     = "smtp.gmail.com";
const int    SmtpPort     = 587;
const string SmtpUser     = "pegaxus.admin@gmail.com";
const string SmtpPass     = "qccksgnwhakilmqu";
const string FromName     = "Pegaxus";
const string ActionUrl    = "https://fap.fpt.edu.vn/";

var recipients = new[]
{
    "huyvog6226@gmail.com",
    "hoanguyen91011@gmail.com",
    "duongngo2209@gmail.com",
    "quocbao.nguyen16102006@gmail.com"
};

var emails = new[]
{
    // 1. OTP Verification
    ("1. Verify your email — Pegaxus",
     EmailBodies.OtpBody(true, "Pegaxus Member", "849201", 10)),

    // 2. Booking approved
    ("2. Booking approved — BK-2024-001",
     EmailBodies.BookingApprovedBody(
        "Pegaxus Member", "BK-2024-001", "TP-2024-001",
        new DateOnly(2024, 12, 25), ActionUrl)),

    // 3. Booking rejected
    ("3. Booking rejected — BK-2024-002",
     EmailBodies.BookingRejectedBody(
        "Pegaxus Member", "BK-2024-002",
        "Transport service for this route is temporarily suspended due to severe weather conditions.",
        ActionUrl)),

    // 4. Document re-upload required
    ("4. Document re-upload required — REQ-2024-089",
     EmailBodies.DocumentReuploadBody(
        "REQ-2024-089", "Health Certificate (Coggins Test)", "Pegasus Spirit",
        "EXPIRED",
        "The veterinary certificate expired on October 10, 2026. Please provide a copy valid for at least 30 days.",
        new DateOnly(2024, 12, 20), ActionUrl)),

    // 5. Missing documents reminder
    ("5. Missing documents reminder — DOS-2024-042",
     EmailBodies.MissingDocumentsBody(
        "DOS-2024-042", "BK-2024-001", new DateOnly(2024, 12, 22),
        "Your compliance dossier is missing 2 essential documents required for quarantine clearance.",
        ActionUrl,
        new[]
        {
            new EmailBodies.MissingItem("Vaccination Record (Flu/Tetanus)", "Pegasus Spirit", new DateOnly(2024, 12, 21)),
            new EmailBodies.MissingItem("Owner Transfer Permit", null, new DateOnly(2024, 12, 22))
        })),

    // 6. Route assignment
    ("6. Route assignment — RP-2024-55",
     EmailBodies.RouteAssignmentBody(
        "Pegaxus Team Member", "RP-2024-55", false, null,
        ActionUrl,
        new[]
        {
            new EmailBodies.RouteSegment(1, new DateTime(2024, 12, 25, 8, 0, 0), new DateTime(2024, 12, 25, 12, 30, 0), "DRV-001", "ESC-007", "Pegaxus Express"),
            new EmailBodies.RouteSegment(2, new DateTime(2024, 12, 25, 14, 0, 0), new DateTime(2024, 12, 25, 18, 0, 0), "DRV-002", "ESC-007", "Pegaxus Express")
        }))
};

Console.WriteLine($"Starting to send emails to {recipients.Length} recipients ({emails.Length} emails each)...");

using var smtp = new SmtpClient();
await smtp.ConnectAsync(SmtpHost, SmtpPort, SecureSocketOptions.StartTls);
await smtp.AuthenticateAsync(SmtpUser, SmtpPass);

int totalSent = 0;
foreach (var to in recipients)
{
    Console.WriteLine($"\n--> Sending to: {to}");
    int i = 0;
    foreach (var (subject, body) in emails)
    {
        i++;
        totalSent++;
        var msg = new MimeMessage();
        msg.Sender = new MailboxAddress(FromName, SmtpUser);
        msg.From.Add(new MailboxAddress(FromName, SmtpUser));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = body }.ToMessageBody();

        await smtp.SendAsync(msg);
        Console.WriteLine($"    [{i}/{emails.Length}] Sent: {subject}");
        await Task.Delay(300);
    }
}

await smtp.DisconnectAsync(true);
Console.WriteLine($"\nAll done! Total {totalSent} emails sent successfully.");
