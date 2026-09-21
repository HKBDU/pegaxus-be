using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Pegaxus.Service.JwtService;

public class JwtServices : IJwtService
{

    private readonly JwtOptions _jwtOption = new();

    public JwtServices(IConfiguration configuration)
    {
        configuration.GetSection(nameof(JwtOptions)).Bind(_jwtOption);
        // Ánh xạ dữ liệu từ AppSettings vào Object JwtOptions
    }

    public string GenerateAccessToken(IEnumerable<Claim> claims)
    {
        var secretKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOption.SecretKey));
        //tạo 1 key để mã hóa token, sử dụng secretkey từ JwtOptions
        var signingCredentials = new SigningCredentials(secretKey, SecurityAlgorithms.HmacSha256);
        //Tạo 1 đối tượng SigningCredentials để xác định thuật toán mã hóa và key sử dụng để ký tokem
        var tokenOptions = new JwtSecurityToken(
            issuer: _jwtOption.Issuer,//Cái token này được kí - tạo ra bởi ai, tổ chức nào
            audience: _jwtOption.Audience,// Cái token này dành cho ai, tổ chức nào
            claims: claims,// Những thông tin mà bạn muốn lưu trữ trong token
                           // thường là thông tin về người dùng như ID, email, vai trò, v.v.
                           //nằm trong payload
            expires: DateTime.Now.AddMinutes(_jwtOption.ExpireMinutes),
            signingCredentials: signingCredentials
            );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokenOptions);
        //sau đó gọi  JwtSecurityTokenHandler
        //
        return tokenString;
    }
    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public ClaimsPrincipal ValidateToken(string token)
    {
        throw new NotImplementedException();
    }
}