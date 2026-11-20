using System.ComponentModel.DataAnnotations;

namespace NemediClinic.Application.DTOs.Auth;

public class LoginRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
