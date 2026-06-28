using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace HomeworkCentral.Api.Services;

public class JwtService(IConfiguration config) : IJwtService
{
    private readonly string _secret = config["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
    private readonly string _issuer = config["Jwt:Issuer"] ?? "HomeworkCentral";
    private readonly string _audience = config["Jwt:Audience"] ?? "HomeworkCentralUsers";
    private readonly int _accessTokenMinutes = int.Parse(config["Jwt:AccessTokenMinutes"] ?? "15");
    private readonly int _refreshTokenDays = int.Parse(config["Jwt:RefreshTokenDays"] ?? "7");

    public string GenerateAccessToken(User user, IEnumerable<string> roles, EffectiveMaskDto masks)
    {
        List<Claim> claims = new()
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("username", user.Username),
            new("perm", masks.ModerationMask),
            new("role_mask", masks.RoleMask),
            new("feature_mask", masks.FeatureMask),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_secret));
        SigningCredentials creds = new(key, SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string token, DateTime expires) GenerateRefreshToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(64);
        string token = Convert.ToBase64String(bytes);
        DateTime expires = DateTime.UtcNow.AddDays(_refreshTokenDays);
        return (token, expires);
    }

    public Guid? ValidateAccessToken(string token)
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_secret));
        JwtSecurityTokenHandler handler = new();
        try
        {
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out SecurityToken validatedToken);

            JwtSecurityToken jwt = (JwtSecurityToken)validatedToken;
            string sub = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
            return Guid.Parse(sub);
        }
        catch
        {
            return null;
        }
    }
}
