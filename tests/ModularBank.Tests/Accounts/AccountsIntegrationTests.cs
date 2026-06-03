using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace ModularBank.Tests.Accounts;

public class AccountsIntegrationTests(SharedPostgresContainer db) : IntegrationTestBase(db)
{
    private async Task<string> GetToken(string email = "acc@example.com")
    {
        await Client.PostAsJsonAsync("/auth/register",
            new { email, password = "Password123!", name = "Acc User" });
        var loginResp = await Client.PostAsJsonAsync("/auth/login",
            new { email, password = "Password123!" });
        var body = await loginResp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body!["accessToken"];
    }

    [Fact]
    public async Task CreateAccountAndCheckBalance()
    {
        var token = await GetToken();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await Client.PostAsJsonAsync("/accounts", new { });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var account = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var accountId = account!["id"].ToString();

        var balanceResp = await Client.GetAsync($"/accounts/{accountId}/balance");
        Assert.Equal(HttpStatusCode.OK, balanceResp.StatusCode);

        var balance = await balanceResp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("0.0000", balance!["amount"]);
    }

    [Fact]
    public async Task ListAccountsReturnsOwnedAccounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var token = await GetToken($"list-{suffix}@example.com");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await Client.PostAsJsonAsync("/accounts", new { });
        await Client.PostAsJsonAsync("/accounts", new { });

        var resp = await Client.GetAsync("/accounts");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var accounts = await resp.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.Equal(2, accounts!.Count);
    }
}
