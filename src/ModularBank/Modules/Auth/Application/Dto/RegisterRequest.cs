using System.ComponentModel.DataAnnotations;

namespace ModularBank.Modules.Auth.Application.Dto;

public record RegisterRequest(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    [Required] string Name
);
