using System.ComponentModel.DataAnnotations;

namespace HomeworkCentral.Api.DTOs;

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = null!;

    [Required, MinLength(3), MaxLength(64)]
    public string Username { get; set; } = null!;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = null!;

    // Optional: registration succeeds either way (as Guest if omitted or incorrect), but a
    // correct answer grants VerifiedUser immediately instead of Guest — see AuthService.RegisterAsync.
    public string? CaptchaChallengeId { get; set; }
    public string? CaptchaAnswer { get; set; }
}
