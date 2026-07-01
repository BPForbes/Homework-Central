using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Services;

public interface IJwtService
{
    string GenerateAccessToken(
        User user,
        IEnumerable<string> roles,
        EffectiveMaskDto masks,
        AccountClass accountClass,
        string? tenantDatabaseName = null);
    (string token, DateTime expires) GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);
}
