using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.MailService;

/// <param name="Role">The employee role, or <c>null</c> for a customer.</param>
public sealed record MailRecipient(Guid AccountId, string Email, string FullName, EmployeeRole? Role);

public static class MailRecipients
{
    /// <summary>
    /// Queues one email per active account in the outbox, in the caller's transaction. <paramref name="build"/> receives the
    /// recipient (name, role) so the body and the link can fit them; returning <c>null</c> skips that recipient.
    /// </summary>
    public static async Task QueueAsync(AppDbContext db, IEnumerable<Guid> accountIds, DateTime now,
        Func<MailRecipient, MailContent?> build, CancellationToken cancellationToken = default)
    {
        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var accounts = await db.Accounts.AsNoTracking()
            .Where(account => ids.Contains(account.AccountId) && !account.IsDeleted && account.Status == AccountStatus.ACTIVE &&
                (account.EmployeeAccount == null || (account.EmployeeAccount.IsActive && !account.EmployeeAccount.IsDeleted)))
            .Select(account => new
            {
                account.AccountId, account.Email,
                CustomerName = account.CustomerAccount == null ? null : account.CustomerAccount.FullName,
                EmployeeName = account.EmployeeAccount == null ? null : account.EmployeeAccount.FullName,
                Role = account.EmployeeAccount == null ? (EmployeeRole?)null : account.EmployeeAccount.Role
            })
            .ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            var mail = build(new MailRecipient(account.AccountId, account.Email, account.EmployeeName ?? account.CustomerName ?? account.Email, account.Role));
            if (mail is not null) OutboxQueue.AddEmail(db, mail, now);
        }
    }

    public static MailContent Mail(MailRecipient recipient, string subject, string body) =>
        new() { To = recipient.Email, Subject = subject, Body = body };
}
