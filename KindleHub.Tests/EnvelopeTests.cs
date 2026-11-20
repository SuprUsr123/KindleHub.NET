using System.Text.Json;
using KindleHub.Core;
using static KindleHub.Tests.ApiHarness;

namespace KindleHub.Tests;

public class EnvelopeTests
{
    public static IEnumerable<object[]> Formats()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/webcrypto.json")));
        foreach (var row in doc.RootElement.GetProperty("wrapped").EnumerateArray())
            yield return [row.GetProperty("packed").GetString()!, row.GetProperty("password").GetString()!, row.GetProperty("key").GetString()!, doc.RootElement.GetProperty("text").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void NewAccountFormatsUnlockWithPasswordAndPreserveRecoveryMetadata(string packed, string password, string key, string text)
    {
        string opened = "";
        var context = AccountEnvelope.Open(packed, password, ref opened);
        Assert.NotNull(context);
        Assert.Equal(text, opened);
        var saved = context.Pack("{\"notes\":[1]}");
        var prefix = packed[..(packed.LastIndexOf(':') + 1)];
        Assert.StartsWith(prefix, saved);
        Assert.Equal("{\"notes\":[1]}", AccountEncryption.UnpackAccount(saved[prefix.Length..], key));
        Assert.Null(AccountEnvelope.Open(packed, "wrong password", ref opened));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task LoginThenSyncKeepsStrongEnvelopeAndFreshClientsRequirePassword(string packed, string password, string key, string text)
    {
        string remote = packed;
        using var h = new ApiHarness();
        h.Respond = r => {
            if (r.Method == "POST") {
                using var d = JsonDocument.Parse(r.Body);
                remote = d.RootElement.GetProperty("state").GetString()!;
                return Json("[]");
            }
            return Json(JsonSerializer.Serialize(new[] { new { state = remote } }));
        };
        var result = await h.Api.LoginAsync(new LoginRequest { Username = "reader", Password = password }, default);
        Assert.True(result.Success);
        Assert.Equal(text, result.StateJson);
        await h.Api.SyncAccountAsync(result.AuthToken, "reader", "{\"theme\":\"sepia\"}", default);
        var prefix = packed[..(packed.LastIndexOf(':') + 1)];
        Assert.StartsWith(prefix, remote);
        Assert.Equal("{\"theme\":\"sepia\"}", AccountEncryption.UnpackAccount(remote[prefix.Length..], key));
        Assert.Equal("{\"theme\":\"sepia\"}", await h.Api.FetchAccountAsync(result.AuthToken, default));
        using var fresh = new ApiHarness { Respond = _ => Json(JsonSerializer.Serialize(new[] { new { state = remote } })) };
        await Assert.ThrowsAsync<AuthenticationException>(() => fresh.Api.FetchAccountAsync(result.AuthToken, default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fresh.Api.SyncAccountAsync(result.AuthToken, "reader", "{}", default));
        Assert.DoesNotContain(fresh.Requests, r => r.Method == "POST");
    }

    [Theory]
    [InlineData("KHA2:0011223344556677:2000001:a.b")]
    [InlineData("KHA2:0011223344556677:0:a.b")]
    [InlineData("KHA2:0011223344556677:R120000:invalid:invalid:a.b")]
    public void RejectsMalformedOrExcessiveWorkEnvelopes(string packed)
    {
        string opened = "";
        Assert.Null(AccountEnvelope.Open(packed, "password", ref opened));
        Assert.Null(opened);
    }

    [Fact]
    public async Task SuperlockCannotBeOverwrittenWithTokenEncryptedState()
    {
        using var h = new ApiHarness { Respond = _ => Json("[{\"state\":\"KHSL1:locked\"}]") };
        var result = await h.Api.LoginAsync(new LoginRequest { Username = "reader", Password = "password" }, default);
        Assert.False(result.Success);
        Assert.Contains("PIN", result.Error);
        await Assert.ThrowsAsync<AuthenticationException>(() => h.Api.SyncAccountAsync(Token, "reader", "{}", default));
        Assert.DoesNotContain(h.Requests, r => r.Method == "POST");
    }
}
