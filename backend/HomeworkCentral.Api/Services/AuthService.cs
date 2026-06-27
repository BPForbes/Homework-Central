using System.Collections;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

public class AuthService(AppDbContext db, IJwtService jwt, IHttpContextAccessor http) : IAuthService
{
    private const int AccessTokenMinutes = 15;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
    {
        if (await db.Users.AnyAsync(u => u.Email == req.Email))
            throw new InvalidOperationException("An account with that email already exists.");

        if (await db.Users.AnyAsync(u => u.Username == req.Username))
            throw new InvalidOperationException("That username is already taken.");

        var now = DateTime.UtcNow;
        var user = new User
        {
            UserId = Guid.NewGuid(),
            Email = req.Email,
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return await BuildAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await BuildAuthResponseAsync(user);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken)
    {
        var stored = await db.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (stored is null || stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        stored.IsRevoked = true;
        await db.SaveChangesAsync();

        return await BuildAuthResponseAsync(stored.User);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken);
        if (stored is null) return;
        stored.IsRevoked = true;
        await db.SaveChangesAsync();
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

        var (refreshToken, refreshExpires) = jwt.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            Token = refreshToken,
            ExpiresAt = refreshExpires,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        SetRefreshCookie(refreshToken, refreshExpires);

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

    private void SetRefreshCookie(string token, DateTime expires)
    {
        var response = http.HttpContext?.Response;
        if (response is null) return;

        response.Cookies.Append("refresh_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/api/auth",
        });
    }
}
