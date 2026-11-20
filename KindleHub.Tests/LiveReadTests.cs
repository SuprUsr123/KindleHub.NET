using KindleHub.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace KindleHub.Tests;

public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KINDLEHUB_LIVE_TESTS") != "1")
            Skip = "Opt in with KINDLEHUB_LIVE_TESTS=1; public reads only, no account needed.";
    }
}

public class LiveReadTests
{
    [LiveFact]
    public async Task PublicFeaturesWorkThroughOfficialSameSiteRoute()
    {
        using var http = new HttpClient();
        var api = new KindleHubApiClient(http, new KindleHubApiOptions(), NullLogger<KindleHubApiClient>.Instance);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Assert.True((await api.GetServerStatusAsync(ct.Token)).Online);
        Assert.NotNull(await api.LookupGroupAsync(KindleHubApiClient.GlobalGroupCode, ct.Token));
        Assert.NotEmpty(await api.FetchAppsAsync(new FetchAppsRequest { Limit = 5 }, ct.Token));
        Assert.NotNull(await api.FetchMessagesAsync(new FetchMessagesRequest { GroupCode = KindleHubApiClient.GlobalGroupCode, Limit = 2 }, "", ct.Token));
        Assert.NotNull(await api.FetchScoresAsync(new FetchScoresRequest { Game = "snake", Limit = 5 }, ct.Token));
        Assert.NotNull(await api.FetchPresenceAsync(7, 5, ct.Token));
        Assert.NotEmpty(await api.ListKnownGamesAsync(ct.Token));
    }

}
