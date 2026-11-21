using System.Text.Json;
using KindleHub.Core;
using static KindleHub.Tests.ApiHarness;

namespace KindleHub.Tests;

// These are HTTP contract tests: they exercise real serialization/transport and
// validate the destination/method. FeatureTests verifies decrypted/domain results.
public class ApiContractTests
{
    private sealed record Case(string Path, string Method, Func<KindleHubApiClient, Task> Run);
    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["check-user"] = new("kh_users", "GET", a => a.CheckUserExistsAsync("reader", default)),
        ["read-account"] = new("kh_users", "GET", a => a.FetchAccountAsync(Token, default)),
        ["sync"] = new("kh_users", "POST", a => a.SyncAccountAsync(Token, "reader", "{}", default)),
        ["read-messages"] = new("kh_messages", "GET", a => a.FetchMessagesAsync(new() { GroupCode = Room }, Token, default)),
        ["reply-previews"] = new("kh_messages", "GET", a => a.FetchMessagesByIdsAsync(["id"], Room, Token, default)),
        ["edit"] = new("kh_messages", "PATCH", a => a.EditMessageAsync(Room, "id", Token, "text", default)),
        ["unsend"] = new("kh_messages", "DELETE", a => a.UnsendMessageAsync("id", Token, default)),
        ["important"] = new("kh_messages", "PATCH", a => a.ToggleImportantAsync("id", Token, true, default)),
        ["reaction"] = new("rpc/kh_set_reaction", "POST", a => a.ToggleReactionAsync("id", "❤", Token[..16], Token, default)),
        ["lookup-group"] = new("kh_groups", "GET", a => a.LookupGroupAsync(Room, default)),
        ["create-group"] = new("kh_groups", "POST", a => a.CreateGroupAsync(new() { Code = Room, Name = "Test", Creator = "reader" }, Token, default)),
        ["group-batch"] = new("kh_groups", "GET", a => a.FetchGroupsByCodesAsync([Room], default)),
        ["topics"] = new("rpc/kh_app_list", "POST", a => a.ListTopicsAsync(Token, 10, default)),
        ["cloud-saves"] = new("rpc/kh_app_list", "POST", a => a.ListCloudSavesAsync(Token, "testapp", "main", 10, default)),
        ["recovery-email"] = new("rpc/kh_recovery_set", "POST", a => a.SetRecoveryEmailAsync(Token, "reader@example.com", default)),
        ["moderator-stats"] = new("rpc/kh_mod_stats", "POST", a => a.FetchModeratorStatsAsync("test-code", default)),
        ["moderator-claim"] = new("rpc/kh_mod_claim", "POST", a => a.ClaimModeratorCodeAsync(Token, "reader", "TEST-CODE", default)),
        ["moderator-application"] = new("kh_feedback", "POST", a => a.SubmitModeratorApplicationAsync(Token, "reader", "1 year", "adult", "I would like to help the community.", "none", default)),
        ["create-topic"] = new("rpc/kh_app_put", "POST", a => a.CreateTopicAsync(new() { Title = "Test", Blurb = "description", CategoryId = "general" }, Token, "reader", default)),
        ["store"] = new("kh_store_apps", "GET", a => a.FetchAppsAsync(new(), default)),
        ["download"] = new("kh_store_apps", "GET", a => a.DownloadAppAsync("app", default)),
        ["download-owner"] = new("kh_store_apps", "GET", a => a.DownloadAppAsync("app", Token, default)),
        ["download-secret"] = new("kh_store_apps", "GET", a => a.DownloadAppWithSecretAsync("app", Token, default)),
        ["download-counter"] = new("rpc/kh_store_download", "POST", a => a.CountAppDownloadAsync("app", default)),
        ["publish"] = new("kh_store_apps", "POST", a => a.PublishAppAsync(new() { Name = "test", Html = "<p>test</p>" }, Token, Token, default)),
        ["unpublish"] = new("kh_store_apps", "DELETE", a => a.UnpublishAppAsync("app", Token, default)),
        ["scores"] = new("kh_scores", "GET", a => a.FetchScoresAsync(new() { Game = "snake" }, default)),
        ["known-games"] = new("kh_scores", "GET", a => a.ListKnownGamesAsync(default)),
        ["presence-write"] = new("kh_presence", "POST", a => a.PingPresenceAsync(Token, "reader", Room, "", "{}", default)),
        ["presence-read"] = new("kh_presence", "GET", a => a.FetchPresenceAsync(7, 10, default)),
        ["avatars"] = new("kh_presence", "GET", a => a.FetchAvatarCodesAsync([Token[..16]], default)),
        ["profiles"] = new("kh_presence", "GET", a => a.FetchPublicProfilesAsync([Token[..16]], default)),
        ["search"] = new("kh_users", "GET", a => a.SearchFriendUsersAsync("read", Token, default)),
        ["relay-send"] = new("kh_messages", "POST", a => a.SendRoomEventAsync(Room, "{\"type\":\"JOIN\"}", "reader", Token, default)),
        ["relay-poll"] = new("kh_messages", "GET", a => a.PollRoomEventsAsync(Room, Token, 10, default)),
        ["message-report"] = new("kh_feedback", "POST", a => a.ReportMessageAsync("reason", "note", new Message { Id = "id", Text = "text", DisplayName = "reader", UserId = Token[..16], GroupCode = Room }, "group", "reader", Token[..16], default)),
        ["username-report"] = new("kh_feedback", "POST", a => a.ReportUsernameAsync("reason", "note", "reader", Token[..16], false, Room, "group", "id", "reader", Token[..16], default)),
        ["mail-read"] = new("kh_mail", "GET", a => a.FetchMailAsync(Token, default)),
        ["mail-send"] = new("kh_mail", "POST", a => a.SendMailAsync(new() { ToUser = "reader", FromUser = "sender", Subject = "test", Body = "body" }, Token, default)),
        ["mail-delete"] = new("kh_mail", "DELETE", a => a.DeleteMailAsync("mail", Token, default)),
        ["inbox"] = new("rpc/kh_inbox_send", "POST", a => a.SendInboxEventAsync(Token, Token[..16], "{}", "reader", default)),
        ["run-token"] = new("rpc/kh_run_start", "POST", a => a.GetRunTokenAsync(Token, "snake", default)),
        ["rate"] = new("rpc/kh_store_rate", "POST", a => a.RateAppAsync(Token, "app", 5, default)),
        ["store-review-list"] = new("rpc/kh_store_pending", "POST", a => a.FetchPendingStoreAppsAsync("test-code", default)),
        ["store-review"] = new("rpc/kh_store_review", "POST", a => a.ReviewStoreAppAsync("test-code", "app", "approve", "", default)),
        ["room-owner-delete"] = new("kh_messages", "DELETE", a => a.DeleteMessageAsRoomOwnerAsync("id", Token, default))
    };
    public static IEnumerable<object[]> Operations => Cases.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task FeatureUsesExpectedHttpContract(string operation)
    {
        using var h = new ApiHarness();
        h.Respond = r => r.Uri.AbsolutePath.Contains("/rpc/")
            ? Json("{\"rows\":[],\"token\":\"khr1.test\"}")
            : r.Uri.AbsolutePath.EndsWith("kh_store_apps") && r.Method == "GET"
                ? Json("[{\"id\":\"app\",\"name\":\"Test\",\"html\":\"<p>test</p>\"}]")
                : Json("[]");
        var c = Cases[operation];
        await c.Run(h.Api);
        Assert.Contains(h.Requests, r => r.Method == c.Method && r.Uri.AbsolutePath == "/t/api/rest/v1/" + c.Path);
        Assert.All(h.Requests, r => {
            Assert.Equal("kindlehub.pro", r.Uri.Host);
            if (r.Method == "POST" || r.Method == "PATCH") {
                using var body = JsonDocument.Parse(r.Body);
                Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
            }
        });
    }
}
