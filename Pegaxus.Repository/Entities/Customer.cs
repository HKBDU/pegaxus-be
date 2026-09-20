using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Customer
{
    public Guid CustomerId { get; set; }

    public Guid AccountId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Address { get; set; }

    public string? Country { get; set; }

    public string? IdentityNumber { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<Claim> Claims { get; set; } = new List<Claim>();

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<HandoverRecord> HandoverRecordFromCustomers { get; set; } = new List<HandoverRecord>();

    public virtual ICollection<HandoverRecord> HandoverRecordToCustomers { get; set; } = new List<HandoverRecord>();

    public virtual ICollection<Horse> Horses { get; set; } = new List<Horse>();
}
