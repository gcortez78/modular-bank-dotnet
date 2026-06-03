using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace ModularBank.Tests.Transfers;

public class TransferIntegrationTests : IntegrationTestBase
{
    private async Task<(string token, Guid accountId)> SetupUserWithAccount(string email)
    {
        await Client.PostAsJsonAsync("/auth/register",
            new { email, password = "Password123!", name = email });
        var loginResp = await Client.PostAsJsonAsync("/auth/login",
            new { email, password = "Password123!" });
        var loginBody = await loginResp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var token = loginBody!["accessToken"];

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await Client.PostAsJsonAsync("/accounts", new { });
        var account = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return (token, Guid.Parse(account!["id"].ToString()!));
    }

    [Fact]
    public async Task TransferWithInsufficientFundsReturns422()
    {
        var (tokenA, accountAId) = await SetupUserWithAccount("alice2@example.com");
        var (_, accountBId) = await SetupUserWithAccount("bob2@example.com");

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var response = await Client.PostAsJsonAsync("/transfers", new
        {
            sourceAccountId = accountAId,
            targetAccountId = accountBId,
            amount = 100.00m
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
