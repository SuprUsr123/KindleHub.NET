using System.Net;
using System.Text.Json;
using KindleHub.Core;
using static KindleHub.Tests.ApiHarness;

namespace KindleHub.Tests;

public class TransportTests
{
    [Fact]
    public async Task UsesOfficialSameSiteRouteAndNativeIdentity()
    {
        using var h = new ApiHarness();
        Assert.True((await h.Api.GetServerStatusAsync(default)).Online);
        var r = Assert.Single(h.Requests);
        Assert.Equal("https://kindlehub.pro/t/api/rest/v1/kh_scores?select=game&limit=1", r.Uri.AbsoluteUri);
        Assert.Equal("KindleHub.NET/1.0", r.Headers["User-Agent"]);
        Assert.DoesNotContain("Origin", r.Headers.Keys);
        Assert.DoesNotContain("Sec-Fetch-Site", r.Headers.Keys);
    }

    [Theory]
    [InlineData(403)] [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public async Task StatusDoesNotPretendFailedRequestsAreOnline(int status)
    {
        using var h = new ApiHarness { Respond = _ => Json("{}", (HttpStatusCode)status) };
        Assert.False((await h.Api.GetServerStatusAsync(default)).Online);
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task StatusBypassesPreviousSuccessAndReportsNetworkFailure()
    {
        using var h = new ApiHarness();
        Assert.True((await h.Api.GetServerStatusAsync(default)).Online);
        h.Respond = _ => throw new HttpRequestException("offline");
        Assert.False((await h.Api.GetServerStatusAsync(default)).Online);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task CancellationPropagatesFromStatus()
    {
        using var h = new ApiHarness();
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Api.GetServerStatusAsync(ct.Token));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task LoginAndSyncNeverReturnStaleAccountState()
    {
        var token = KeyDerivation.DeriveAuthToken("reader", "test password");
        string state = "{\"notes\":[]}";
        using var h = new ApiHarness();
        h.Respond = r => {
            if (r.Method == "POST") {
                using var d = JsonDocument.Parse(r.Body);
                Assert.Equal(token, r.Headers["X-KH-Secret"]);
                state = AccountEncryption.UnpackAccount(d.RootElement.GetProperty("state").GetString(), token);
                return Json("[]");
            }
            return Json(JsonSerializer.Serialize(new[] { new { state = AccountEncryption.PackAccount(state, token) } }));
        };
        Assert.Equal(state, (await h.Api.LoginAsync(new LoginRequest { Username = "reader", Password = "test password" }, default)).StateJson);
        await h.Api.SyncAccountAsync(token, "reader", "{\"notes\":[1]}", default);
        Assert.Equal("{\"notes\":[1]}", await h.Api.FetchAccountAsync(token, default));
        Assert.Equal(4, h.Requests.Count);
    }

    [Fact]
    public async Task SuccessfulMutationInvalidatesCatalogCache()
    {
        using var h = new ApiHarness();
        await h.Api.FetchAppsAsync(new FetchAppsRequest(), default);
        await h.Api.FetchAppsAsync(new FetchAppsRequest(), default);
        Assert.Single(h.Requests);
        await h.Api.PublishAppAsync(new PublishAppRequest { Name = "test", Html = "<p>test</p>" }, Token, Token, default);
        await h.Api.FetchAppsAsync(new FetchAppsRequest(), default);
        Assert.Equal(3, h.Requests.Count);
    }

    [Fact]
    public async Task ScoreRunTokensAreScopedToAccountAndGame()
    {
        using var h = new ApiHarness();
        h.Respond = r => Json("{\"token\":\"khr1." + r.Headers["X-KH-Secret"][0] + "\"}");
        Assert.Equal("khr1.a", await h.Api.GetRunTokenAsync(Token, "snake", default));
        Assert.Equal("khr1.b", await h.Api.GetRunTokenAsync(OtherToken, "snake", default));
        Assert.Equal("khr1.a", await h.Api.GetRunTokenAsync(Token, "SNAKE", default));
        Assert.Equal(2, h.Requests.Count);
        Assert.All(h.Requests, r => Assert.Equal("snake", JsonDocument.Parse(r.Body).RootElement.GetProperty("p_game").GetString()));
    }

    [Theory]
    [InlineData(403)] [InlineData(429)] [InlineData(500)]
    public async Task PublishingFailureIsSurfacedAndIsNeverRetried(int status)
    {
        using var h = new ApiHarness { Respond = _ => Json("{\"message\":\"Refused\"}", (HttpStatusCode)status) };
        var ex = await Assert.ThrowsAsync<ServerException>(() => h.Api.PublishAppAsync(new PublishAppRequest { Name = "test" }, Token, Token, default));
        Assert.Equal(status, ex.StatusCode);
        Assert.Single(h.Requests);
    }
}
