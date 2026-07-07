using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Infrastructure;

public interface IPasswordConfirmationService
{
    Task<bool> VerifyAsync(Guid userId, string password, CancellationToken ct = default);
}

public class PasswordConfirmationService(
    AppDbContext db,
    IConfiguration config,
    IWebHostEnvironment env) : IPasswordConfirmationService
{
    public const string DevAccountPassword = "hcentralpassword";

    public async Task<bool> VerifyAsync(Guid userId, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        User? user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null)
            return false;

        if (DevBypass.IsEnabled(config, env)
            && string.Equals(password, DevAccountPassword, StringComparison.Ordinal))
        {
            return true;
        }

        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
    }
}
