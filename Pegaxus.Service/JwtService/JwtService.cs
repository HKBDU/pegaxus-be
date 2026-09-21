using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Pegaxus.Service.JwtService;

public class JwtService
{
    public static string GenerateToken(IEnumerable<Claim> claims, JwtOptions options)
    {
        var secretKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SecretKey));
        // Tạo 1 Key để mã hóa token, sử dụng secretKey từ JwtOptions
        var signinCredentials = new SigningCredentials(secretKey, SecurityAlgorithms.HmacSha256);
        // Tạo 1 đối tượng SigningCredentials để xác định thuật toán mã hóa và key sử dụng để ký token

        var tokeOptions = new JwtSecurityToken(
            issuer: options.Issuer, // Cái token này được kí - tạo ra bởi ai, tổ chức nào
            audience: options.Audience, // Cái token này dành cho ai, tổ chức nào
            claims: claims, // Những thông tin mà bạn muốn lưu trữ trong token,
            // thường là thông tin về người dùng như ID, email, vai trò, v.v.
            // nằm trong payload
            expires: DateTime.Now.AddMinutes(options.ExpirationMinutes), // Token sẽ hết hạn sau bao lâu
            signingCredentials: signinCredentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokeOptions);
        // Sau đó gọi JwtSecurityTokenHandler
        // để tạo ra token dưới dạng chuỗi (string) từ các thông tin đã cung cấp ở trên
        
        return tokenString;
    }
}