using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Accounts.Infrastructure;

namespace ModularBank.Tests.Transfers;

public class TransferIntegrationTests(SharedPostgresContainer db) : IntegrationTestBase(db)
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

    [Fact]
    public async Task TransferFromAnotherUsersAccountReturns403()
    {
        var (tokenA, accountAId) = await SetupUserWithAccount("alice3@example.com");
        var (tokenB, accountBId) = await SetupUserWithAccount("bob3@example.com");

        // Bob tries to transfer FROM Alice's account
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var response = await Client.PostAsJsonAsync("/transfers", new
        {
            sourceAccountId = accountAId,  // Alice's account
            targetAccountId = accountBId,
            amount = 1.00m
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TransferHistoryForAnotherUsersAccountReturns403()
    {
        var (tokenA, accountAId) = await SetupUserWithAccount("alice4@example.com");
        var (tokenB, _) = await SetupUserWithAccount("bob4@example.com");

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var response = await Client.GetAsync($"/transfers?accountId={accountAId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task FundAccount(Guid accountId, decimal amount)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var account = await db.Accounts.FindAsync(accountId);
        account!.Balance = amount;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SuccessfulTransferCreatesHistoryEntry()
    {
        var (tokenA, accountAId) = await SetupUserWithAccount("alice5@example.com");
        var (_, accountBId) = await SetupUserWithAccount("bob5@example.com");
        await FundAccount(accountAId, 1000m);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var transferResp = await Client.PostAsJsonAsync("/transfers", new
        {
            sourceAccountId = accountAId,
            targetAccountId = accountBId,
            amount = 250.00m,
            reference = "test-payment"
        });
        Assert.Equal(HttpStatusCode.Created, transferResp.StatusCode);
        var transfer = await transferResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(transfer!["id"]);

        var historyResp = await Client.GetAsync($"/transfers?accountId={accountAId}");
        Assert.Equal(HttpStatusCode.OK, historyResp.StatusCode);
        var history = await historyResp.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.Single(history!);
    }

    [Fact]
    public async Task AuditRecordsTransferEvent()
    {
        var (tokenA, accountAId) = await SetupUserWithAccount("alice6@example.com");
        var (_, accountBId) = await SetupUserWithAccount("bob6@example.com");
        await FundAccount(accountAId, 500m);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        await Client.PostAsJsonAsync("/transfers", new
        {
            sourceAccountId = accountAId,
            targetAccountId = accountBId,
            amount = 50.00m
        });

        var auditResp = await Client.GetAsync("/audit");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
        var audit = await auditResp.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.NotEmpty(audit!);
    }

    [Fact]
    public async Task NotificationsRecordTransferEvent()
    {
        var (tokenA, accountAId) = await SetupUserWithAccount("alice7@example.com");
        var (_, accountBId) = await SetupUserWithAccount("bob7@example.com");
        await FundAccount(accountAId, 500m);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        await Client.PostAsJsonAsync("/transfers", new
        {
            sourceAccountId = accountAId,
            targetAccountId = accountBId,
            amount = 75.00m
        });

        var notifResp = await Client.GetAsync("/notifications");
        Assert.Equal(HttpStatusCode.OK, notifResp.StatusCode);
        var notifications = await notifResp.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.NotEmpty(notifications!);
    }
}
