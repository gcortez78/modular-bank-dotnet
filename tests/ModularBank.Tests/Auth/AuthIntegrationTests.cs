using System.Net;
using System.Net.Http.Json;

namespace ModularBank.Tests.Auth;

public class AuthIntegrationTests : IntegrationTestBase
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
}
