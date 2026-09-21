namespace Pegaxus.Service.Auth;

public sealed record RegisterResponse(
    Guid AccountId,
    Guid CustomerId,
    string Email,
    bool EmailVerified);
