using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Location: Địa điểm master trên bản đồ. 
/// Một địa điểm chỉ tạo một lần và có thể được nhiều Route tái sử dụng, 
/// ví dụ Thành phố Hồ Chí Minh.
/// </summary>
public partial class Location
{
    public Guid LocationId { get; set; }

    public string LocationName { get; set; } = null!;

    public LocationType? LocationType { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? StateProvince { get; set; }

    public string Country { get; set; } = null!;

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? LocationNote { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<IncidentReport> IncidentReports { get; set; } = new List<IncidentReport>();

    public virtual ICollection<Route> RouteFromLocations { get; set; } = new List<Route>();

    public virtual ICollection<Route> RouteToLocations { get; set; } = new List<Route>();
}
