using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace HomeworkCentral.Api.Services;

public class AuthService(
    AppDbContext db,
    IJwtService jwt,
    IHttpContextAccessor http,
    IEffectiveMaskService effectiveMaskService) : IAuthService
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
            await db.AssignDefaultRolesAsync(user, effectiveMaskService);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
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
            .Include(u => u.EffectiveMask!)
                .ThenInclude(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await BuildAuthResponseAsync(user);
    }

    public async Task<AuthResponse> RefreshAsync(string rawToken)
    {
        var tokenHash = HashToken(rawToken);

        var revoked = await db.RefreshTokens
            .Where(rt => rt.TokenHash == tokenHash && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.IsRevoked, true));

        if (revoked == 0)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var stored = await db.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
            .Include(rt => rt.User.EffectiveMask!)
                .ThenInclude(m => m.SubjectExpertiseMasks)
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
            .Include(u => u.EffectiveMask!)
                .ThenInclude(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user is null)
            return null;

        var effectiveMask = user.EffectiveMask
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId);

        return BuildUserDto(user, effectiveMask);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(User user)
    {
        if (!db.Entry(user).Collection(u => u.UserRoles).IsLoaded)
        {
            await db.Entry(user).Collection(u => u.UserRoles)
                .Query().Include(ur => ur.Role).LoadAsync();
        }

        if (!db.Entry(user).Reference(u => u.EffectiveMask).IsLoaded)
        {
            await db.Entry(user).Reference(u => u.EffectiveMask)
                .Query()
                .Include(m => m.SubjectExpertiseMasks)
                .LoadAsync();
        }

        var effectiveMask = user.EffectiveMask
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(user.UserId);

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

        var dto = BuildUserDto(user, effectiveMask);
        var accessToken = jwt.GenerateAccessToken(user, dto.Roles, ToEffectiveMaskDto(effectiveMask));

        return new AuthResponse
        {
            AccessToken = accessToken,
            ExpiresIn = AccessTokenMinutes * 60,
            User = dto,
        };
    }

    private static UserDto BuildUserDto(User user, UserEffectiveMask effectiveMask)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var masks = ToEffectiveMaskDto(effectiveMask);

        return new UserDto
        {
            UserId = user.UserId,
            Email = user.Email,
            Username = user.Username,
            Roles = roles,
            PermissionMask = masks.ModerationMask,
            RoleMask = masks.RoleMask,
            FeatureMask = masks.FeatureMask,
            GeneralSubjectMask = masks.GeneralSubjectMask,
            SubjectExpertiseMasks = masks.SubjectExpertiseMasks,
            StatusMask = masks.StatusMask,
        };
    }

    private static EffectiveMaskDto ToEffectiveMaskDto(UserEffectiveMask effectiveMask)
    {
        var subjectExpertiseMasks = SubjectExpertiseCatalog.AllExpertiseCategoryNames()
            .ToDictionary(
                category => category,
                category =>
                {
                    var row = effectiveMask.SubjectExpertiseMasks
                        .FirstOrDefault(m => m.Category == category);
                    return BitMask.ToBase64(row?.ExpertiseMask ?? BitMask.Create(128));
                },
                StringComparer.Ordinal);

        return new EffectiveMaskDto
        {
            RoleMask = BitMask.ToBase64(effectiveMask.EffectiveRoleMask),
            ModerationMask = BitMask.ToBase64(effectiveMask.EffectiveModerationMask),
            FeatureMask = BitMask.ToBase64(effectiveMask.EffectiveFeatureMask),
            GeneralSubjectMask = BitMask.ToBase64(effectiveMask.GeneralSubjectMask),
            SubjectExpertiseMasks = subjectExpertiseMasks,
            StatusMask = BitMask.ToBase64(effectiveMask.StatusMask),
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
