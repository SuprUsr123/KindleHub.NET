using System.Net;
using System.Text.Json;
using KindleHub.Core;
using static KindleHub.Tests.ApiHarness;

namespace KindleHub.Tests;

public class FeatureTests
{
    [Fact]
    public async Task ChatSendFetchEditReactionDeleteAndReplyPreserveProtocol()
    {
        using var h = new ApiHarness();
        MessageSecretStore.Initialise(Path.Combine(Path.GetTempPath(), "kh-test-" + Guid.NewGuid() + ".json"));
        var msg = await h.Api.SendMessageAsync(new SendMessageRequest { GroupCode = Room, Text = "Hello 世界", DisplayName = "Reader", Important = true, ReplyTo = "parent" }, Token, default);
        var send = Assert.Single(h.Requests);
        using var payload = JsonDocument.Parse(send.Body);
        var p = payload.RootElement;
        Assert.Equal("Hello 世界", ChatEncryption.Decrypt(Room, p.GetProperty("text").GetString()));
        Assert.Equal("parent", p.GetProperty("reply_to").GetString());
        Assert.True(p.GetProperty("important").GetBoolean());
        Assert.Equal(Token, send.Headers["X-KH-Secret"]);
        Assert.NotEqual(Token, msg.OwnerSecret);
        h.Respond = r => r.Method == "GET" ? Json("[" + send.Body + "]") : Json("{}");
        var read = Assert.Single(await h.Api.FetchMessagesAsync(new FetchMessagesRequest { GroupCode = Room }, Token, default));
        Assert.Equal(msg.Text, read.Text);
        Assert.True(read.IsMine);
        Assert.True(await h.Api.EditMessageAsync(Room, msg.Id, msg.OwnerSecret, "edited", default));
        Assert.True(await h.Api.ToggleImportantAsync(msg.Id, msg.OwnerSecret, false, default));
        Assert.True(await h.Api.ToggleReactionAsync(msg.Id, "❤️", Token[..16], Token, default));
        Assert.True(await h.Api.UnsendMessageAsync(msg.Id, msg.OwnerSecret, default));
        Assert.Contains(h.Requests, r => r.Method == "PATCH" && r.Headers["X-KH-Secret"] == msg.OwnerSecret);
        Assert.Contains(h.Requests, r => r.Method == "DELETE" && r.Headers["X-KH-Secret"] == msg.OwnerSecret);
    }

    [Fact]
    public async Task MailSendAndReadUseCanonicalRecipientAndEncryptBothColumns()
    {
        using var h = new ApiHarness();
        var item = await h.Api.SendMailAsync(new SendMailRequest { ToUser = " READER@kindlehub.pro ", FromUser = "sender", Subject = "Subject", Body = "Body", ReplyTo = "old" }, Token, default);
        var sent = Assert.Single(h.Requests);
        using var d = JsonDocument.Parse(sent.Body);
        var row = d.RootElement;
        Assert.Equal("reader", row.GetProperty("to_user").GetString());
        Assert.Equal("Subject", ChatEncryption.Decrypt("mail:reader", row.GetProperty("subject").GetString()));
        Assert.Equal("Body", ChatEncryption.Decrypt("mail:reader", row.GetProperty("body").GetString()));
        h.Respond = _ => Json("[" + sent.Body + "]");
        var fetched = Assert.Single(await h.Api.FetchMailAsync(Token, default));
        Assert.Equal("Body", fetched.Body);
        Assert.Equal("Subject", fetched.Subject);
        await h.Api.FetchMailAsync(Token, default);
        Assert.Equal(3, h.Requests.Count); // mailbox polling must not be cached
        Assert.True(await h.Api.DeleteMailAsync(item.Id, item.OwnerSecret, default));
    }

    [Theory]
    [InlineData("pabcdefghijklmn2")]
    [InlineData("1234567890abcdef")]
    public async Task PeopleSearchAcceptsCurrentAndLegacyPublicIdsWithoutEmailSuffix(string id)
    {
        using var h = new ApiHarness { Respond = _ => Json(JsonSerializer.Serialize(new[] { new { pid = id.StartsWith('p') ? id : "", hash = id.StartsWith('p') ? "" : id, email = "reader" } })) };
        var person = Assert.Single(await h.Api.SearchFriendUsersAsync("test_reader", Token, default));
        Assert.Equal(id, person.Hash);
        Assert.Equal("reader", person.Name);
        var r = Assert.Single(h.Requests);
        Assert.Equal(Token, r.Headers["X-KH-Secret"]);
        Assert.Equal("1", r.Headers["x-kh-want-pid"]);
        Assert.Contains("test_reader", Uri.UnescapeDataString(r.Uri.Query));
    }

    [Fact]
    public async Task ProfilesAcceptPublicIdsAndRejectQueryFragments()
    {
        const string pid = "pabcdefghijklmn2";
        using var h = new ApiHarness { Respond = _ => Json("[{\"user_id\":\"" + pid + "\",\"avatar\":\"KHAV1:test\",\"profile\":{\"fr\":\"gold\",\"ns\":\"bold\"}}]") };
        var profiles = await h.Api.FetchPublicProfilesAsync([pid, "bad),id=neq.any"], default);
        Assert.Equal("gold", profiles[pid].ProfileFrame);
        Assert.DoesNotContain("bad", Assert.Single(h.Requests).Uri.Query);
        Assert.Equal("KHAV1:test", (await h.Api.FetchAvatarCodesAsync([pid], default))[pid]);
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(422)] [InlineData(429)]
    public async Task InboxRefusalsNeverTriggerLegacyWrite(int status)
    {
        using var h = new ApiHarness { Respond = _ => Json("{\"message\":\"INBOX_UPDATE\"}", (HttpStatusCode)status) };
        await Assert.ThrowsAsync<ServerException>(() => h.Api.SendInboxEventAsync(Token, "1234567890abcdef", "{}", "Reader", default));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task InboxPublicIdDoesNotUseLegacyHexRoomOnUnavailableRpc()
    {
        using var h = new ApiHarness { Respond = _ => Json("{}", HttpStatusCode.ServiceUnavailable) };
        Assert.False(await h.Api.SendInboxEventAsync(Token, "pabcdefghijklmn2", "{}", "Reader", default));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task InboxPayloadIsAnObjectRatherThanDoubleEncodedJson()
    {
        using var h = new ApiHarness();
        Assert.True(await h.Api.SendInboxEventAsync(Token, "pabcdefghijklmn2", "{\"type\":\"FRIEND_REQUEST\"}", "reader", default));
        using var d = JsonDocument.Parse(Assert.Single(h.Requests).Body);
        Assert.Equal(JsonValueKind.Object, d.RootElement.GetProperty("p_payload").ValueKind);
        Assert.Equal("FRIEND_REQUEST", d.RootElement.GetProperty("p_payload").GetProperty("type").GetString());
    }

    [Fact]
    public async Task PresenceProfileUsesTheCurrentTextColumnFormat()
    {
        using var h = new ApiHarness();
        await h.Api.PingPresenceAsync(Token, "reader", "", "", "{\"b\":\"Hello\"}", default);
        using var doc = JsonDocument.Parse(Assert.Single(h.Requests).Body);
        var profile = doc.RootElement.GetProperty("profile");
        Assert.Equal(JsonValueKind.String, profile.ValueKind);
        using var card = JsonDocument.Parse(profile.GetString()!);
        Assert.Equal("Hello", card.RootElement.GetProperty("b").GetString());
    }

    [Fact]
    public async Task ScoreSubmissionUsesRunTokenAndDeduplicatesOnlySuccessfulPosts()
    {
        using var h = new ApiHarness();
        h.Respond = r => r.Uri.AbsolutePath.EndsWith("kh_run_start") ? Json("{\"token\":\"khr1.test\"}") : Json("[]");
        var score = new SubmitScoreRequest { Game = "snake", Score = 100, DisplayName = "reader" };
        Assert.True(await h.Api.SubmitScoreAsync(score, Token, default));
        Assert.True(await h.Api.SubmitScoreAsync(score, Token, default));
        Assert.Equal(2, h.Requests.Count);
        using var d = JsonDocument.Parse(h.Requests[1].Body);
        Assert.Equal("khr1.test", d.RootElement.GetProperty("run").GetString());
        Assert.Equal(100, d.RootElement.GetProperty("score").GetInt32());
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1000000001)]
    public async Task InvalidScoresDoNotReachServer(long score)
    {
        using var h = new ApiHarness();
        await Assert.ThrowsAsync<ValidationException>(() => h.Api.SubmitScoreAsync(new SubmitScoreRequest { Game = "snake", Score = score }, Token, default));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task RegistrationWritesEncryptedStateAndHandlesConflict()
    {
        using var h = new ApiHarness();
        var request = new RegisterRequest { Username = "testreader", Password = "test password" };
        var result = await h.Api.RegisterAsync(request, default);
        Assert.True(result.Success);
        var write = h.Requests.Last();
        using var d = JsonDocument.Parse(write.Body);
        Assert.Equal("{}", AccountEncryption.UnpackAccount(d.RootElement.GetProperty("state").GetString(), result.AuthToken));
        Assert.DoesNotContain(request.Password, write.Body);
        h.Respond = r => r.Method == "POST" ? Json("{}", HttpStatusCode.Conflict) : Json("[]");
        Assert.False((await h.Api.RegisterAsync(request, default)).Success);
    }

    [Fact]
    public async Task LoginDistinguishesWrongPasswordMissingUserAndUnreadableState()
    {
        using var h = new ApiHarness();
        var req = new LoginRequest { Username = "reader", Password = "bad" };
        Assert.Contains("no account", (await h.Api.LoginAsync(req, default)).Error);
        h.Respond = r => r.Uri.Query.Contains("email=eq") ? Json("[{\"email\":\"reader\"}]") : Json("[]");
        Assert.Contains("password", (await h.Api.LoginAsync(req, default)).Error);
        h.Respond = _ => Json("[{\"state\":\"invalid\"}]");
        Assert.False((await h.Api.LoginAsync(req, default)).Success);
    }
}
