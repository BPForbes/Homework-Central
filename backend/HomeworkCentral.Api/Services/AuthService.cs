using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
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
        string normalizedEmail = req.Email.ToLowerInvariant();

        DateTime now = DateTime.UtcNow;
        User user = new User
        {
            UserId = Guid.NewGuid(),
            Email = normalizedEmail,
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        try
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await db.AssignDefaultRolesAsync(user, effectiveMaskService);
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            await transaction.RollbackAsync();
            if (pg.SqlState == "23505")
            {
                if (string.Equals(pg.ConstraintName, "IX_Users_Email", StringComparison.Ordinal))
                    throw new InvalidOperationException("An account with that email already exists.");
                if (string.Equals(pg.ConstraintName, "IX_Users_Username", StringComparison.Ordinal))
                    throw new InvalidOperationException("That username is already taken.");
            }
            throw;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return await BuildAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        string normalizedEmail = req.Email.ToLowerInvariant();

        User? user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.EffectiveMask!)
                .ThenInclude(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await BuildAuthResponseAsync(user);
    }

    /// <inheritdoc />
    public async Task<DevLoginOptionsResponse> GetDevLoginOptionsAsync()
    {
        HashSet<Guid> developerUserIds = await db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.Role.Name == "Developer")
            .Select(userRole => userRole.UserId)
            .ToHashSetAsync();

        Dictionary<string, User> usersByEmail = await db.Users
            .AsNoTracking()
            .ToDictionaryAsync(user => user.Email, StringComparer.OrdinalIgnoreCase);

        List<DevDeveloperOption> developers = new();

        foreach (DevAccountDefinition account in DevAccountCatalog.All)
        {
            if (!usersByEmail.TryGetValue(account.DeveloperEmail, out User? developer))
                continue;

            if (!developerUserIds.Contains(developer.UserId))
                continue;

            List<DevUserOption> personas = new();
            foreach (DevPersonaDefinition persona in account.Personas)
            {
                if (!usersByEmail.TryGetValue(persona.Email, out User? user))
                    continue;

                personas.Add(new DevUserOption
                {
                    UserId = user.UserId,
                    Username = user.Username,
                });
            }

            developers.Add(new DevDeveloperOption
            {
                UserId = developer.UserId,
                Username = developer.Username,
                Users = personas,
            });
        }

        return new DevLoginOptionsResponse
        {
            Developers = developers,
        };
    }

    /// <inheritdoc />
    public async Task<AuthResponse> DevLoginAsync(DevLoginRequest req)
    {
        User? developer = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == req.DeveloperUserId);

        if (developer is null)
            throw new UnauthorizedAccessException("Selected developer account was not found.");

        DevAccountDefinition? account = DevAccountCatalog.FindByDeveloperEmail(developer.Email);
        if (account is null || !developer.UserRoles.Any(ur => ur.Role.Name == "Developer"))
            throw new UnauthorizedAccessException("Selected account is not a developer.");

        User loginUser;
        if (req.TargetUserId is null || req.TargetUserId == Guid.Empty)
        {
            loginUser = await db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Include(u => u.EffectiveMask!)
                    .ThenInclude(m => m.SubjectExpertiseMasks)
                .FirstOrDefaultAsync(u => u.Username == DevBypass.DevAdminUsername)
                ?? throw new InvalidOperationException("DevAdmin account is not configured.");
        }
        else
        {
            loginUser = await db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Include(u => u.EffectiveMask!)
                    .ThenInclude(m => m.SubjectExpertiseMasks)
                .FirstOrDefaultAsync(u => u.UserId == req.TargetUserId)
                ?? throw new InvalidOperationException("Selected user was not found.");

            if (account is null || !DevAccountCatalog.PersonaBelongsToAccount(account, loginUser.Email))
                throw new UnauthorizedAccessException("Selected user does not belong to this developer account.");
        }

        return await BuildAuthResponseAsync(loginUser);
    }

    public async Task<AuthResponse> RefreshAsync(string rawToken)
    {
        string tokenHash = HashToken(rawToken);

        int revoked = await db.RefreshTokens
            .Where(rt => rt.TokenHash == tokenHash && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.IsRevoked, true));

        if (revoked == 0)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        RefreshToken stored = await db.RefreshTokens
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
        string tokenHash = HashToken(rawToken);
        await db.RefreshTokens
            .Where(rt => rt.TokenHash == tokenHash && !rt.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.IsRevoked, true));
    }

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId)
    {
        User? user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.EffectiveMask!)
                .ThenInclude(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user is null)
            return null;

        UserEffectiveMask effectiveMask = user.EffectiveMask
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

        UserEffectiveMask effectiveMask = user.EffectiveMask
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(user.UserId);

        (string rawToken, DateTime refreshExpires) = jwt.GenerateRefreshToken();
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

        UserDto dto = BuildUserDto(user, effectiveMask);
        string accessToken = jwt.GenerateAccessToken(user, dto.Roles, ToEffectiveMaskDto(effectiveMask));

        return new AuthResponse
        {
            AccessToken = accessToken,
            ExpiresIn = AccessTokenMinutes * 60,
            User = dto,
        };
    }

    private static UserDto BuildUserDto(User user, UserEffectiveMask effectiveMask)
    {
        List<string> roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        EffectiveMaskDto masks = ToEffectiveMaskDto(effectiveMask);

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
        Dictionary<string, string> subjectExpertiseMasks = SubjectExpertiseCatalog.AllExpertiseCategoryNames()
            .ToDictionary(
                category => category,
                category =>
                {
                    UserSubjectExpertiseMask? row = effectiveMask.SubjectExpertiseMasks
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
        HttpResponse? response = http.HttpContext?.Response;
        if (response is null) return;

        response.Cookies.Append("refresh_token", rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.HttpContext?.Request.IsHttps ?? false,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/api/auth",
        });
    }

    private static string HashToken(string token)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
