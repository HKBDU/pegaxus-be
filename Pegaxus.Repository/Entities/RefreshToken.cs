using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// RefreshToken: Token dùng để cấp lại Access Token cho một Account. Một Account có thể có nhiều RefreshToken cho nhiều thiết bị/phiên đăng nhập. Database chỉ lưu token_hash, không lưu raw token.
/// </summary>
public partial class RefreshToken
{
    public long RefreshTokenId { get; set; }

    public long AccountId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public long? ReplacedByTokenId { get; set; }

    public string? CreatedByIp { get; set; }

    public string? RevokedByIp { get; set; }

    public string? DeviceInfo { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<RefreshToken> InverseReplacedByToken { get; set; } = new List<RefreshToken>();

    public virtual RefreshToken? ReplacedByToken { get; set; }
}
