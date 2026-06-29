using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);

    /// <summary>Localhost dev bypass login. Uses DevAdmin when no persona is selected.</summary>
    Task<AuthResponse> DevLoginAsync(DevLoginRequest request);

    /// <summary>Returns developer accounts and personas eligible for dev bypass login.</summary>
    Task<DevLoginOptionsResponse> GetDevLoginOptionsAsync();

    Task<AuthResponse> RefreshAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken);
    Task<UserDto?> GetCurrentUserAsync(Guid userId);
}
