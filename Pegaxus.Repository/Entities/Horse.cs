using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Horse: Hồ sơ ngựa thuộc sở hữu của Customer, 
/// lưu thông tin nhận dạng, đặc điểm cơ thể và 
/// dữ liệu phục vụ vận chuyển, sức khỏe, sự cố và tính phí.
/// </summary>
public partial class Horse
{
    public Guid HorseId { get; set; }

    public Guid CustomerId { get; set; }

    public string HorseName { get; set; } = null!;

    public HorseSex? Sex { get; set; }

    public string? PassportNumber { get; set; }

    public string? MicrochipNumber { get; set; }

    public string? Breed { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Color { get; set; }

    public string? CountryOfOrigin { get; set; }

    public decimal? WeightKg { get; set; }

    public decimal? HeightCm { get; set; }

    public string? IdentificationNotes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<BookingHorse> BookingHorses { get; set; } = new List<BookingHorse>();

    public virtual Customer Customer { get; set; } = null!;

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<HorseHealthIncidentDetail> HorseHealthIncidentDetails { get; set; } = new List<HorseHealthIncidentDetail>();

    public virtual ICollection<HorseHealthLog> HorseHealthLogs { get; set; } = new List<HorseHealthLog>();

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
}
