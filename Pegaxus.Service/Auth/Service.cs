using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

using JwtService = Pegaxus.Service.JwtService;
using Claim = System.Security.Claims.Claim;
using AppException = Pegaxus.Service.Exceptions.Exceptions;
using AccountEntity = Pegaxus.Repository.Entities.Account;

namespace Pegaxus.Service.Auth;

public partial class Service : IService
{
    private const string EmailVerification = "EMAIL_VERIFICATION";

    private const string PasswordReset = "PASSWORD_RESET";

    private const string GoogleLoginCodePrefix = "auth:google:code:";

    private readonly AppDbContext _db;

    private readonly IPasswordHasher<AccountEntity> _passwordHasher;

    private readonly IDistributedCache _cache;

    private readonly MailService.IService _mailService;

    private readonly JwtService.IJwtService _jwtService;

    private readonly IHttpContextAccessor _httpContextAccessor;

    private static readonly TimeSpan LoginAttemptWindow = TimeSpan.FromMinutes(15);

    private const int MaxLoginFailures = 5;

    public Service(
        AppDbContext db,
        IPasswordHasher<AccountEntity> passwordHasher,
        IDistributedCache cache,
        MailService.IService mailService,
        JwtService.IJwtService jwtService,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _mailService = mailService;
        _jwtService = jwtService;
        _httpContextAccessor = httpContextAccessor;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool MatchesHash(string value, string expectedHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)),
                Convert.FromHexString(expectedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
