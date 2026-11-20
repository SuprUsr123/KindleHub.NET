using System.Text.Json;
using KindleHub.Core;
using static KindleHub.Tests.ApiHarness;

namespace KindleHub.Tests;

public class StateAndCryptoTests
{
    [Fact]
    public void ReadsIndependentJavaScriptCryptoVectors()
    {
        // AES-GCM/gzip vectors generated with Node crypto/zlib and the public
        // web build's compression functions, not with the .NET implementation.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/webcrypto.json")));
        var r = doc.RootElement;
        string text = r.GetProperty("text").GetString()!;
        Assert.Equal(r.GetProperty("compressed").GetString(), KindleHubCompression.Compress(text));
        foreach (var row in r.GetProperty("account").EnumerateArray()) {
            Assert.Equal(text, AccountEncryption.UnpackAccount(row.GetProperty("packed").GetString(), Token));
            Assert.Null(AccountEncryption.UnpackAccount(row.GetProperty("packed").GetString(), OtherToken));
        }
        foreach (var row in r.GetProperty("chat").EnumerateArray())
            Assert.Equal(text, ChatEncryption.Decrypt(Room, row.GetProperty("packed").GetString()));
    }

    [Theory]
    [InlineData("")] [InlineData("Hello 世界")] [InlineData("line one\nline two\r\nemoji 🦊")]
    public void EncryptionRoundTripsAndUsesFreshNonces(string text)
    {
        var a = AccountEncryption.PackAccount(JsonSerializer.Serialize(new { text }), Token);
        var b = AccountEncryption.PackAccount(JsonSerializer.Serialize(new { text }), Token);
        Assert.NotEqual(a, b);
        Assert.Equal(JsonSerializer.Serialize(new { text }), AccountEncryption.UnpackAccount(a, Token));
        Assert.Equal(text, ChatEncryption.Decrypt(Room, ChatEncryption.Encrypt(Room, text)));
    }

    [Theory]
    [InlineData("")] [InlineData("broken")] [InlineData("a.b")] [InlineData("KHA2:locked")] [InlineData("KHSL1:locked")]
    public void MalformedOrLockedAccountsNeverBecomeEmptyWritableAccounts(string packed)
        => Assert.Null(AccountEncryption.UnpackAccount(packed, Token));

    [Fact]
    public void UsernameCanonicalizationAndTokenMatchIndependentSha256()
    {
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("kh::arancool3000::password"))).ToLowerInvariant();
        Assert.Equal(expected, KeyDerivation.DeriveAuthToken(" ARAN ", "password"));
        Assert.NotEqual(expected, KeyDerivation.DeriveAuthToken("ARAN", "Password"));
    }

    [Fact]
    public void SettingsMergePreservesUnknownDataAndCloudTrimRemovesDeviceSecrets()
    {
        var state = "{\"future\":{\"keep\":42},\"authToken\":\"secret\",\"notes\":[]}";
        state = AccountState.WithText(state, "theme", "dark");
        state = AccountState.WithNumber(state, "fontSize", 19);
        state = AccountState.WithFlag(state, "syncEnabled", true);
        state = AccountState.AppendNote(state, "saved from chat");
        Assert.Equal("dark", AccountState.GetText(state, "theme", ""));
        Assert.Equal(19, AccountState.GetNumber(state, "fontSize", 0));
        Assert.True(AccountState.GetFlag(state, "syncEnabled", false));
        Assert.Equal(1, AccountState.CountNotes(state));
        using var doc = JsonDocument.Parse(AccountState.TrimStateForCloud(state));
        Assert.Equal(42, doc.RootElement.GetProperty("future").GetProperty("keep").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("authToken", out _));
    }

    [Fact]
    public void OpenedAndLeftRoomsRoundTripWithoutDuplicates()
    {
        var room = new Group { Code = Room, Name = "Test", Creator = "reader" };
        var state = AccountState.WithMessageRoom("{}", room);
        state = AccountState.WithMessageRoom(state, room);
        Assert.Equal(Room, Assert.Single(AccountState.GetMessageRooms(state)).Code);
        state = AccountState.WithLeftMessageRoom(state, Room);
        Assert.Empty(AccountState.GetMessageRooms(state));
    }

    [Fact]
    public async Task OfflineScoreQueuePersistsBestScoreAndFlushesAfterRecovery()
    {
        var path = Path.Combine(Path.GetTempPath(), "kh-score-test-" + Guid.NewGuid() + ".json");
        try {
            using (var q = new OfflineScoreQueue(path)) {
                q.Enqueue("snake", 10, "reader");
                q.Enqueue("snake", 5, "reader");
                q.Enqueue("snake", 20, "reader");
                Assert.Equal(1, q.PendingCount());
            }
            using var restored = new OfflineScoreQueue(path);
            Assert.Equal(1, restored.PendingCount());
            using var h = new ApiHarness { Respond = _ => throw new HttpRequestException("offline") };
            Assert.Equal(1, await restored.FlushAsync(h.Api, Token, default));
            h.Respond = r => r.Uri.AbsolutePath.EndsWith("kh_run_start") ? Json("{\"token\":\"khr1.test\"}") : Json("[]");
            Assert.Equal(0, await restored.FlushAsync(h.Api, Token, default));
            using var d = JsonDocument.Parse(h.Requests.Last().Body);
            Assert.Equal(20, d.RootElement.GetProperty("score").GetInt32());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ChatPreferencesPersistStarsReportsAndDmRooms()
    {
        var path = Path.Combine(Path.GetTempPath(), "kh-prefs-test-" + Guid.NewGuid() + ".json");
        try {
            using (var prefs = new ChatPrefsStore(path)) {
                Assert.True(prefs.ToggleStar("message"));
                prefs.MarkReported("message", "reason");
                prefs.RememberDmRoom(Token[..16], Room);
            }
            using var restored = new ChatPrefsStore(path);
            Assert.True(restored.IsStarred("message"));
            Assert.Equal("reason", restored.ReportedReason("message"));
            Assert.Equal(Room, restored.DmRoomFor(Token[..16]));
        }
        finally { File.Delete(path); }
    }
}
