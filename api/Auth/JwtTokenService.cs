using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ComicStudio.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace ComicStudio.Api.Auth;

public class JwtTokenService(SigningKeyProvider signingKey, IConfiguration configuration)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(AppUser user)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(signingKey.Key),
            SecurityAlgorithms.HmacSha256
        );

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "comic-studio",
            audience: configuration["Jwt:Audience"] ?? "comic-studio",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
