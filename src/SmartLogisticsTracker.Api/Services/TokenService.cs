using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using SmartLogisticsTracker.Api.Domain.Entities;
using SmartLogisticsTracker.Api.Options;

namespace SmartLogisticsTracker.Api.Services;

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessTokenResult CreateToken(AppUser user);
}

public sealed class TokenService(IOptions<JwtOptions> jwtOptions) : ITokenService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public AccessTokenResult CreateToken(AppUser user)
    {
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_jwtOptions.AccessTokenExpireMinutes);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var tokenDescriptor = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: credentials);

        var token = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        return new AccessTokenResult(token, expiresAt);
    }
}
