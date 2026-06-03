using System.Net;
using System.Net.Http.Json;

namespace ModularBank.Tests.Auth;

public class AuthIntegrationTests(SharedPostgresContainer db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task RegisterAndLoginSuccessfully()
    {
        var registerResponse = await Client.PostAsJsonAsync("/auth/register", new
        {
            email = "test@example.com",
            password = "Password123!",
            name = "Test User"
        });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var body = await registerResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body!["accessToken"]);
        Assert.NotNull(body["refreshToken"]);

        // Verify login with same credentials
        var loginResponse = await Client.PostAsJsonAsync("/auth/login", new
        {
            email = "test@example.com",
            password = "Password123!"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(loginBody!["accessToken"]);
    }

    [Fact]
    public async Task LoginWithWrongPasswordReturns401()
    {
        var response = await Client.PostAsJsonAsync("/auth/login", new
        {
            email = "nobody@example.com",
            password = "wrong"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshTokenReturnsNewAccessToken()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"refresh-{suffix}@example.com";

        await Client.PostAsJsonAsync("/auth/register",
            new { email, password = "Password123!", name = "Refresh User" });
        var loginResp = await Client.PostAsJsonAsync("/auth/login",
            new { email, password = "Password123!" });
        var loginBody = await loginResp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var refreshToken = loginBody!["refreshToken"];

        var refreshResp = await Client.PostAsJsonAsync("/auth/refresh",
            new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);

        var body = await refreshResp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body!["accessToken"]);
        Assert.NotNull(body["refreshToken"]);
        // Token is rotated
        Assert.NotEqual(refreshToken, body["refreshToken"]);
    }
}
