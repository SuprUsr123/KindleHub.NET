using System;
using KindleHub.Client.Models;
using Xunit;

namespace KindleHub.Tests;

/// <summary>
/// The App Store polyfill layer must wrap downloaded apps in exactly the
/// environment the live kindlehub.pro runtime provides: the sealed CSP first,
/// then the perf/guard/storage/screenshot/dialog/nonce shims, then the app.
/// These tests pin the wrap order, the scope plumbing and the app HTML's
/// survival for every HTML shape the store can hand us.
/// </summary>
public class PolyfillTests
{
    private const string AppId = "pub_ab12cd34ef56";

    private static int Count(string haystack, string needle) =>
        haystack.Split(needle, StringSplitOptions.None).Length - 1;

    [Fact]
    public void WrapsHtmlWithHeadFirst()
    {
        var html = "<!doctype html><html><head><title>App</title></head><body><h1>Hi</h1></body></html>";
        var wrapped = AppPolyfills.WrapForStandalone(html, AppId);

        // The CSP is the first thing in <head>, like the web runtime's sealed frame.
        var headAt = wrapped.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        var cspAt = wrapped.IndexOf(AppPolyfills.AppCsp, StringComparison.Ordinal);
        Assert.True(headAt >= 0 && cspAt == headAt + "<head>".Length);

        // Every shim layer the website injects is present exactly once.
        Assert.Equal(1, Count(wrapped, AppPolyfills.AppCsp));
        Assert.Contains("kh_appls_", wrapped);          // scoped storage key family
        Assert.Contains("KH_APP_NONCE", wrapped);        // per-launch nonce
        Assert.Contains("__khNativeLocalStorage", wrapped);
        Assert.Contains("beforeunload", wrapped);        // guard: bye on leave
        Assert.Contains("RTCPeerConnection", wrapped);   // guard: WebRTC removed
        Assert.Contains("toDataURL", wrapped);           // screenshot capture
        Assert.Contains("__khdlg", wrapped);             // dialog boxes
        Assert.Contains("action:\"saveLocal\"", wrapped);
        Assert.Contains("action===\"saveLocal\"", wrapped);
        Assert.Contains("cloudPut", wrapped);            // bridge: cloud saves
        Assert.Contains("sendMail", wrapped);            // bridge: mail confirm

        // The app itself survives untouched, after the shims.
        var appAt = wrapped.IndexOf("<h1>Hi</h1>", StringComparison.Ordinal);
        Assert.True(appAt > cspAt);
    }

    [Fact]
    public void WrapsHtmlWithoutHeadIntoFullDocument()
    {
        var wrapped = AppPolyfills.WrapForStandalone("<p>fragment</p>", AppId);
        Assert.StartsWith("<!doctype html>", wrapped, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<head><meta charset=\"utf-8\">" + AppPolyfills.AppCsp, wrapped);
        Assert.EndsWith("<body><p>fragment</p></body></html>".Replace("<body>", "<body>"), wrapped);
        Assert.Contains("<p>fragment</p>", wrapped);
    }

    [Fact]
    public void WrapsHtmlTagWithoutHead()
    {
        var wrapped = AppPolyfills.WrapForStandalone("<html><body>x</body></html>", AppId);
        Assert.Contains("<head>" + AppPolyfills.AppCsp, wrapped);
        Assert.Contains("<body>x</body>", wrapped);
    }

    [Fact]
    public void ScopeIsSanitizedAndThreadedThroughTheShims()
    {
        var wrapped = AppPolyfills.WrapForStandalone("<body>y</body>", "pub_ZZ-99!!weird");
        Assert.DoesNotContain("__KH_SCOPE__", wrapped);
        // Only the alphanumeric characters survive into the scope.
        Assert.Contains("kh_appls_pubZZ99weird", wrapped);
        Assert.Equal("pubZZ99weird", AppPolyfills.SanitizeScope("pub_ZZ-99!!weird"));
        Assert.Equal("app", AppPolyfills.SanitizeScope("!!!"));
        Assert.Equal("app", AppPolyfills.SanitizeScope(""));
        Assert.Equal(48, AppPolyfills.SanitizeScope(new string('a', 200)).Length);
    }

    [Fact]
    public void DifferentAppsGetDifferentScopes()
    {
        var a = AppPolyfills.WrapForStandalone("<body>a</body>", "pub_1111");
        var b = AppPolyfills.WrapForStandalone("<body>b</body>", "pub_2222");
        Assert.Contains("kh_appls_pub1111", a);
        Assert.Contains("kh_appls_pub2222", b);
    }

    [Fact]
    public void CspMatchesTheWebsiteExactly()
    {
        // Byte-for-byte the policy from kh-app.js _khAppCsp.
        Assert.Equal(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; media-src data: blob:; font-src data:; connect-src 'none'; form-action 'none'; base-uri 'none'; frame-src 'none'\">",
            AppPolyfills.AppCsp);
    }

    [Fact]
    public void StorageCapMatchesTheWebsite()
    {
        Assert.Equal(65536, AppPolyfills.LocalStoreMax); // KH_APP_LS_MAX
    }

    [Fact]
    public void NullAndEmptyHtmlStillProduceAValidDocument()
    {
        var wrappedNull = AppPolyfills.WrapForStandalone(null!, AppId);
        var wrappedEmpty = AppPolyfills.WrapForStandalone("", AppId);
        Assert.Contains(AppPolyfills.AppCsp, wrappedNull);
        Assert.Contains(AppPolyfills.AppCsp, wrappedEmpty);
        Assert.Contains("<body></body>", wrappedEmpty);
    }

    [Fact]
    public void TheBridgeIgnoresItsOwnReplies()
    {
        // The reply-echo guard is what stops the local bridge from re-processing
        // its own acknowledgements as fresh app requests (an infinite loop).
        var wrapped = AppPolyfills.WrapForStandalone("<body>z</body>", AppId);
        Assert.Contains("if(typeof d.ok!==\"undefined\")return;", wrapped);
    }

    [Fact]
    public void LegacyBetaHelpersSurvive()
    {
        // Apps published against the desktop's first polyfill layer still call
        // these; the wrap keeps defining them when the app does not.
        var wrapped = AppPolyfills.WrapForStandalone("<body>l</body>", AppId);
        Assert.Contains("window.NOW=window.NOW||", wrapped);
        Assert.Contains("window.saveGame=window.saveGame||", wrapped);
        Assert.Contains("window._khRunSave=window._khRunSave||", wrapped);
        Assert.Contains("window._khRunLoad=window._khRunLoad||", wrapped);
    }
}
