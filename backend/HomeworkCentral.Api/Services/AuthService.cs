using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace HomeworkCentral.Api.Services;

public class AuthService(
    AppDbContext masterDb,
    ITenantDbContextFactory tenantFactory,
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

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await masterDb.Database.BeginTransactionAsync();
        try
        {
            masterDb.Users.Add(user);
            await masterDb.SaveChangesAsync();
            await masterDb.AssignDefaultRolesAsync(user, effectiveMaskService);
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

        return await BuildAuthResponseAsync(user, masterDb, tenantDatabaseName: null);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        string normalizedEmail = req.Email.ToLowerInvariant();

        User? user = await masterDb.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.EffectiveMask!)
                .ThenInclude(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await BuildAuthResponseAsync(user, masterDb, tenantDatabaseName: null);
    }

    /// <inheritdoc />
    public async Task<DevLoginOptionsResponse> GetDevLoginOptionsAsync()
    {
        HashSet<Guid> developerUserIds = await masterDb.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.Role.Name == "Developer")
            .Select(userRole => userRole.UserId)
            .ToHashSetAsync();

        Dictionary<string, User> masterUsersByEmail = await masterDb.Users
            .AsNoTracking()
            .ToDictionaryAsync(user => user.Email, StringComparer.OrdinalIgnoreCase);

        List<DevDeveloperOption> developers = new();

        foreach (DevAccountDefinition account in DevAccountCatalog.All)
        {
            if (!masterUsersByEmail.TryGetValue(account.DeveloperEmail, out User? developer))
                continue;

            if (!developerUserIds.Contains(developer.UserId))
                continue;

            List<DevUserOption> personas = new();
            foreach (DevPersonaDefinition persona in account.Personas)
            {
                string databaseName = DevAccountCatalog.GetPersonaDatabaseName(account, persona);
                AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(databaseName);
                await using (tenantDb)
                {
                    User? user = await tenantDb.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Email == persona.Email);

                    if (user is null)
                        continue;

                    personas.Add(new DevUserOption
                    {
                        UserId = user.UserId,
                        Username = user.Username,
                        TenantDatabaseName = databaseName,
                    });
                }
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
        User? developer = await masterDb.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == req.DeveloperUserId);

        if (developer is null)
            throw new UnauthorizedAccessException("Selected developer account was not found.");

        DevAccountDefinition? account = DevAccountCatalog.FindByDeveloperEmail(developer.Email);
        if (account is null || !developer.UserRoles.Any(ur => ur.Role.Name == "Developer"))
            throw new UnauthorizedAccessException("Selected account is not a developer.");

        if (req.TargetUserId is null || req.TargetUserId == Guid.Empty)
        {
            User loginUser = await masterDb.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Include(u => u.EffectiveMask!)
                    .ThenInclude(m => m.SubjectExpertiseMasks)
                .FirstOrDefaultAsync(u => u.Username == DevBypass.DevAdminUsername)
                ?? throw new InvalidOperationException("DevAdmin account is not configured.");

            return await BuildAuthResponseAsync(loginUser, masterDb, tenantDatabaseName: null);
        }

        if (string.IsNullOrWhiteSpace(req.TenantDatabaseName))
            throw new InvalidOperationException("Tenant database name is required when impersonating a persona.");

        (DevAccountDefinition Account, DevPersonaDefinition Persona)? personaMatch =
            DevAccountCatalog.FindByPersonaDatabaseName(req.TenantDatabaseName);

        if (personaMatch is null
            || !string.Equals(personaMatch.Value.Account.DeveloperEmail, developer.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Selected persona does not belong to this developer account.");
        }

        AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(req.TenantDatabaseName);
        await using (tenantDb)
        {
            User loginPersona = await tenantDb.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Include(u => u.EffectiveMask!)
                    .ThenInclude(m => m.SubjectExpertiseMasks)
                .FirstOrDefaultAsync(u => u.UserId == req.TargetUserId)
                ?? throw new InvalidOperationException("Selected user was not found.");

            if (!string.Equals(loginPersona.Email, personaMatch.Value.Persona.Email, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Selected user does not belong to this developer account.");

            return await BuildAuthResponseAsync(loginPersona, tenantDb, req.TenantDatabaseName);
        }
    }

    public async Task<AuthResponse> RefreshAsync(string rawToken)
    {
        string? tenantDatabaseName = http.HttpContext?.Request.Cookies[TenancyConstants.TenantDbClaimName];
        AppDbContext db = await ResolveDbContextAsync(tenantDatabaseName);
        bool disposeTenantDb = !ReferenceEquals(db, masterDb);

        try
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

            return await BuildAuthResponseAsync(stored.User, db, tenantDatabaseName);
        }
        finally
        {
            if (disposeTenantDb)
                await db.DisposeAsync();
        }
    }

    public async Task RevokeRefreshTokenAsync(string rawToken)
    {
        string? tenantDatabaseName = http.HttpContext?.Request.Cookies[TenancyConstants.TenantDbClaimName];
        AppDbContext db = await ResolveDbContextAsync(tenantDatabaseName);
        bool disposeTenantDb = !ReferenceEquals(db, masterDb);

        try
        {
            string tokenHash = HashToken(rawToken);
            await db.RefreshTokens
                .Where(rt => rt.TokenHash == tokenHash && !rt.IsRevoked)
                .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.IsRevoked, true));
        }
        finally
        {
            if (disposeTenantDb)
                await db.DisposeAsync();
        }
    }

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId, string? tenantDatabaseName = null)
    {
        AppDbContext db = await ResolveDbContextAsync(tenantDatabaseName);
        bool disposeTenantDb = !ReferenceEquals(db, masterDb);

        try
        {
            User? user = await db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Include(u => u.EffectiveMask!)
                    .ThenInclude(m => m.SubjectExpertiseMasks)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user is null)
                return null;

            UserEffectiveMask effectiveMask = user.EffectiveMask
                ?? await RebuildMaskAsync(db, userId);

            return BuildUserDto(user, effectiveMask);
        }
        finally
        {
            if (disposeTenantDb)
                await db.DisposeAsync();
        }
    }

    private async Task<AppDbContext> ResolveDbContextAsync(string? tenantDatabaseName, CancellationToken ct = default) =>
        string.IsNullOrEmpty(tenantDatabaseName)
            ? masterDb
            : await tenantFactory.CreateForRegisteredTenantAsync(tenantDatabaseName, ct);

    private async Task<AuthResponse> BuildAuthResponseAsync(
        User user,
        AppDbContext db,
        string? tenantDatabaseName)
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
            ?? await EffectiveMaskService.RebuildOnContextAsync(db, user.UserId);

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

        SetRefreshCookie(rawToken, refreshExpires, tenantDatabaseName);

        UserDto dto = BuildUserDto(user, effectiveMask);
        string accessToken = jwt.GenerateAccessToken(user, dto.Roles, ToEffectiveMaskDto(effectiveMask), tenantDatabaseName);

        return new AuthResponse
        {
            AccessToken = accessToken,
            ExpiresIn = AccessTokenMinutes * 60,
            User = dto,
        };
    }

    private static async Task<UserEffectiveMask> RebuildMaskAsync(AppDbContext db, Guid userId) =>
        await EffectiveMaskService.RebuildOnContextAsync(db, userId);

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

    private void SetRefreshCookie(string rawToken, DateTime expires, string? tenantDatabaseName)
    {
        HttpResponse? response = http.HttpContext?.Response;
        if (response is null) return;

        CookieOptions cookieOptions = new()
        {
            HttpOnly = true,
            Secure = http.HttpContext?.Request.IsHttps ?? false,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/api/auth",
        };

        response.Cookies.Append("refresh_token", rawToken, cookieOptions);

        if (string.IsNullOrEmpty(tenantDatabaseName))
        {
            response.Cookies.Delete(TenancyConstants.TenantDbClaimName, cookieOptions);
            return;
        }

        response.Cookies.Append(TenancyConstants.TenantDbClaimName, tenantDatabaseName, cookieOptions);
    }

    private static string HashToken(string token)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
