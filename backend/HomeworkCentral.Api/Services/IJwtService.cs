using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Services;

public interface IJwtService
{
    string GenerateAccessToken(User user, IEnumerable<string> roles, string permissionMask);
    (string token, DateTime expires) GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);
}
