using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace ModularBank.Tests.Accounts;

public class AccountsIntegrationTests : IntegrationTestBase
{
    private async Task<string> GetToken()
    {
        await Client.PostAsJsonAsync("/auth/register", new
            { email = "acc@example.com", password = "Password123!", name = "Acc User" });
        var loginResp = await Client.PostAsJsonAsync("/auth/login",
            new { email = "acc@example.com", password = "Password123!" });
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
}
