using System.ComponentModel.DataAnnotations;

namespace ModularBank.Modules.Auth.Application.Dto;

public record LoginRequest(
    [Required][EmailAddress] string Email,
    [Required] string Password
);
