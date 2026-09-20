using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Account
{
    public Guid AccountId { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public DateTime? LastLoginAt { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    public string? AvatarUrl { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<AccountNotification> AccountNotifications { get; set; } = new List<AccountNotification>();

    public virtual ICollection<BookingStatusHistory> BookingStatusHistories { get; set; } = new List<BookingStatusHistory>();

    public virtual ICollection<ComplianceRequirement> ComplianceRequirements { get; set; } = new List<ComplianceRequirement>();

    public virtual Customer? CustomerAccount { get; set; }

    public virtual ICollection<Customer> CustomerDeletedByAccounts { get; set; } = new List<Customer>();

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<DossierTemplate> DossierTemplates { get; set; } = new List<DossierTemplate>();

    public virtual Employee? EmployeeAccount { get; set; }

    public virtual ICollection<Employee> EmployeeDeletedByAccounts { get; set; } = new List<Employee>();

    public virtual ICollection<Horse> Horses { get; set; } = new List<Horse>();

    public virtual ICollection<Account> InverseDeletedByAccount { get; set; } = new List<Account>();

    public virtual ICollection<InvoiceStatusHistory> InvoiceStatusHistories { get; set; } = new List<InvoiceStatusHistory>();

    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual ICollection<Route> Routes { get; set; } = new List<Route>();

    public virtual ICollection<TransportProvider> TransportProviders { get; set; } = new List<TransportProvider>();

    public virtual ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}
