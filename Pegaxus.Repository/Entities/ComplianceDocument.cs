using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// ComplianceDocument: Giấy tờ thực tế được cung cấp để đáp ứng một DossierRequirement. 
/// Đây là document logic, không phải từng file upload.
/// </summary>
public partial class ComplianceDocument
{
    public Guid ComplianceDocumentId { get; set; }

    public Guid DossierRequirementId { get; set; }

    public string DocumentCode { get; set; } = null!;

    public string DocumentName { get; set; } = null!;

    public ComplianceDocumentStatus Status { get; set; } = ComplianceDocumentStatus.PROVIDED;

    public string? DocumentNumber { get; set; }

    public string? IssuingAuthority { get; set; }

    public DateOnly? IssuedDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<DocumentVersion> DocumentVersions { get; set; } = new List<DocumentVersion>();

    public virtual DossierRequirement DossierRequirement { get; set; } = null!;
}
