using System.Security.Cryptography;
using System.Text.Json;
using KindleHub.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace KindleHub.Tests;

public sealed class LiveAccountFactAttribute : FactAttribute
{
    public LiveAccountFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KINDLEHUB_LIVE_ACCOUNT_TESTS") != "1")
            Skip = "Explicit opt-in: creates disposable accounts and performs private test writes.";
    }
}

public class LiveAccountTests(ITestOutputHelper output)
{
    private sealed record TestAccount(string Username, string Password);

    [LiveAccountFact]
    public async Task TwoAccountsCanSyncChatMailPublishAndExchangeRelayEvents()
    {
        using var httpA = new HttpClient();
        using var httpB = new HttpClient();
        var a = new KindleHubApiClient(httpA, new(), NullLogger<KindleHubApiClient>.Instance);
        var b = new KindleHubApiClient(httpB, new(), NullLogger<KindleHubApiClient>.Instance);
        using var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = ct.Token;
        var directory = Environment.GetEnvironmentVariable("KINDLEHUB_TEST_ACCOUNT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "kindlehub-dotnet-live-tests");
        Directory.CreateDirectory(directory);
        var accountFile = Path.Combine(directory, "accounts.json");
        TestAccount[] accounts;
        if (File.Exists(accountFile)) accounts = JsonSerializer.Deserialize<TestAccount[]>(await File.ReadAllTextAsync(accountFile, token))!;
        else {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            accounts = [new("chatgpt_testing_" + suffix + "a", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                        new("chatgpt_testing_" + suffix + "b", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))];
            await File.WriteAllTextAsync(accountFile, JsonSerializer.Serialize(accounts), token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(accountFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            foreach (var account in accounts) {
                var registered = await a.RegisterAsync(new() { Username = account.Username, Password = account.Password }, token);
                Assert.True(registered.Success, "Registration: " + registered.Error);
            }
            output.WriteLine("PASS two disposable accounts registered");
        }
        var loginA = await a.LoginAsync(new() { Username = accounts[0].Username, Password = accounts[0].Password }, token);
        var loginB = await b.LoginAsync(new() { Username = accounts[1].Username, Password = accounts[1].Password }, token);
        Assert.True(loginA.Success, "Login A: " + loginA.Error);
        Assert.True(loginB.Success, "Login B: " + loginB.Error);
        output.WriteLine("PASS both accounts signed in");
        var authA = loginA.AuthToken; var authB = loginB.AuthToken;
        var state = AccountState.WithText(loginA.StateJson, "profileBio", "Disposable .NET integration test");
        await a.SyncAccountAsync(authA, accounts[0].Username, state, token);
        Assert.Equal(state, await a.FetchAccountAsync(authA, token));
        output.WriteLine("PASS encrypted cloud state saved and reloaded");

        var room = await a.CreateGroupAsync(new() { Name = "Private .NET integration test", Creator = accounts[0].Username }, authA, token);
        Assert.NotNull(await b.LookupGroupAsync(room.Code, token));
        var msg = await a.SendMessageAsync(new() { GroupCode = room.Code, DisplayName = accounts[0].Username, Text = "Private integration test 世界" }, authA, token);
        string? appId = null; MailItem? mail = null;
        try {
            var received = await b.FetchMessagesAsync(new() { GroupCode = room.Code }, authB, token);
            Assert.Contains(received, m => m.Id == msg.Id && m.Text == msg.Text);
            Assert.True(await a.EditMessageAsync(room.Code, msg.Id, msg.OwnerSecret, "Edited test 世界", token), "Edit");
            Assert.True(await a.ToggleImportantAsync(msg.Id, msg.OwnerSecret, true, token), "Important");
            var edited = Assert.Single(await b.FetchMessagesAsync(new() { GroupCode = room.Code }, authB, token), m => m.Id == msg.Id);
            Assert.Equal("Edited test 世界", edited.Text);
            Assert.True(edited.Important);
            Assert.True(await b.ToggleReactionAsync(msg.Id, "👍", authB[..16], authB, token), "Reaction");
            output.WriteLine("PASS private chat send/read/edit/important/reaction");

            var people = await a.SearchFriendUsersAsync(accounts[1].Username, authA, token);
            var recipient = Assert.Single(people, p => p.Name == accounts[1].Username);
            var invitation = JsonSerializer.Serialize(new { type = "FRIEND_REQUEST", fromName = accounts[0].Username, fromUserId = authA[..16], fromHash = authA[..16] });
            Assert.True(await a.SendInboxEventAsync(authA, recipient.Hash, invitation, accounts[0].Username, token), "Inbox event");
            var inbox = await b.PollRoomEventsAsync(RelayRooms.InboxRoomCodeFor(authB[..16]), authB, 50, token);
            Assert.Contains(inbox, e => e.Type == "FRIEND_REQUEST");
            output.WriteLine("PASS public-ID search and inbox delivery/read");

            mail = await a.SendMailAsync(new() { ToUser = accounts[1].Username, FromUser = accounts[0].Username, Subject = "Disposable test", Body = "Private mail 世界" }, authA, token);
            Assert.Contains(await b.FetchMailAsync(authB, token), m => m.Id == mail.Id && m.Subject == "Disposable test" && m.Body == "Private mail 世界");
            output.WriteLine("PASS mail sent and decrypted by second account");

            var app = await a.PublishAppAsync(new() { Name = "Disposable .NET test", Category = "Tools", Author = accounts[0].Username, Html = "<!doctype html><title>Disposable test</title><p>Private integration test.</p>" }, authA, authA, token);
            appId = app.Id;
            var downloaded = await a.DownloadAppWithSecretAsync(app.Id, authA, token);
            Assert.Contains("Private integration test", downloaded.Html);
            output.WriteLine("PASS app publish and owner download");

            var evt = await a.SendRoomEventAsync(room.Code, "{\"type\":\"JOIN\",\"game\":\"connect4\"}", accounts[0].Username, authA, token);
            Assert.Contains(await b.PollRoomEventsAsync(room.Code, authB, 20, token), e => e.Message.Id == evt.Id && e.Type == "JOIN");
            Assert.True(await a.UnsendMessageAsync(evt.Id, evt.OwnerSecret, token));
            output.WriteLine("PASS multiplayer encrypted relay round trip");
            Assert.StartsWith("khr1.", await a.GetRunTokenAsync(authA, "snake", token));
            Assert.NotNull(await a.ListTopicsAsync(authA, 5, token));
            Assert.NotNull(await a.ListCloudSavesAsync(authA, "khtopics", "index", 5, token));
            await b.PingPresenceAsync(authB, accounts[1].Username, "", "", "{}", token);
            Assert.Contains(await a.FetchPresenceAsync(7, 400, token), p => p.DisplayName == accounts[1].Username);
            output.WriteLine("PASS score-run authorization, topics/cloud reads and presence heartbeat");
        }
        finally {
            if (appId is not null) Assert.True(await a.UnpublishAppAsync(appId, authA, CancellationToken.None), "Clean up published test app");
            if (mail is not null) Assert.True(await a.DeleteMailAsync(mail.Id, authA, CancellationToken.None), "Clean up test mail");
            Assert.True(await a.UnsendMessageAsync(msg.Id, msg.OwnerSecret, CancellationToken.None), "Clean up test chat");
            await a.SyncAccountAsync(authA, accounts[0].Username, loginA.StateJson, CancellationToken.None);
        }
        output.WriteLine("PASS test app, mail, and chat removed; original account state restored");
    }
}
