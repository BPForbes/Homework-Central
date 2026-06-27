using System.ComponentModel.DataAnnotations;

namespace HomeworkCentral.Api.DTOs;

public class LoginRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = null!;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = null!;
}
