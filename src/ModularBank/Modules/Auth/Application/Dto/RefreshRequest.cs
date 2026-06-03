using System.Text.Json.Serialization;

namespace ModularBank.Modules.Auth.Application.Dto;

public record RefreshRequest([property: JsonPropertyName("refreshToken")] string Token);
