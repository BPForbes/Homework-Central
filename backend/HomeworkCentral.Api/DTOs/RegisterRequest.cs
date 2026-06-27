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
}
