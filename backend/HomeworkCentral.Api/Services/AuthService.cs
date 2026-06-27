using System.Collections;
using System.Security.Cryptography;
using System.Text;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeworkCentral.Api.Services;

public class AuthService(AppDbContext db, IJwtService jwt, IHttpContextAccessor http) : IAuthService
{
    private const int AccessTokenMinutes = 15;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
    {
        var normalizedEmail = req.Email.ToLowerInvariant();

        var now = DateTime.UtcNow;
        var user = new User
        {
            UserId = Guid.NewGuid(),
            Email = normalizedEmail,
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            // Unique constraint violation (23505)
            if (pg.SqlState == "23505")
            {
                var detail = pg.Detail ?? string.Empty;
                if (detail.Contains("Email"))
                    throw new InvalidOperationException("An account with that email already exists.");
                if (detail.Contains("Username"))
                    throw new InvalidOperationException("That username is already taken.");
            }
            throw;
        }

        return await BuildAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var normalizedEmail = req.Email.ToLowerInvariant();

        var user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await BuildAuthResponseAsync(user);
    }

    public async Task<AuthResponse> RefreshAsync(string rawToken)
    {
        var tokenHash = HashToken(rawToken);

        // Atomically revoke: only one concurrent caller wins the update
        var revoked = await db.RefreshTokens
            .Where(rt => rt.TokenHash == tokenHash && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.IsRevoked, true));

        if (revoked == 0)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var stored = await db.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
            .FirstAsync(rt => rt.TokenHash == tokenHash);

        return await BuildAuthResponseAsync(stored.User);
    }

    public async Task RevokeRefreshTokenAsync(string rawToken)
    {
        var tokenHash = HashToken(rawToken);
        await db.RefreshTokens
            .Where(rt => rt.TokenHash == tokenHash && !rt.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.IsRevoked, true));
    }

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId)
    {
        var user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        return user is null ? null : BuildUserDto(user);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(User user)
    {
        if (!db.Entry(user).Collection(u => u.UserRoles).IsLoaded)
        {
            await db.Entry(user).Collection(u => u.UserRoles)
                .Query().Include(ur => ur.Role).LoadAsync();
        }

        var (rawToken, refreshExpires) = jwt.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            TokenHash = HashToken(rawToken),
            ExpiresAt = refreshExpires,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        SetRefreshCookie(rawToken, refreshExpires);

        var dto = BuildUserDto(user);
        var accessToken = jwt.GenerateAccessToken(user, dto.Roles, dto.PermissionMask);

        return new AuthResponse
        {
            AccessToken = accessToken,
            ExpiresIn = AccessTokenMinutes * 60,
            User = dto,
        };
    }

    private static UserDto BuildUserDto(User user)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();

        var combined = new BitArray(256);
        foreach (var ur in user.UserRoles)
        {
            combined = combined.Or(ur.Role.PermissionMask);
        }

        var bytes = new byte[32];
        combined.CopyTo(bytes, 0);
        var permMask = Convert.ToBase64String(bytes);

        return new UserDto
        {
            UserId = user.UserId,
            Email = user.Email,
            Username = user.Username,
            Roles = roles,
            PermissionMask = permMask,
        };
    }

    private void SetRefreshCookie(string rawToken, DateTime expires)
    {
        var response = http.HttpContext?.Response;
        if (response is null) return;

        response.Cookies.Append("refresh_token", rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/api/auth",
        });
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
