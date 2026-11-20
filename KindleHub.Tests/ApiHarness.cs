using System.Net;
using System.Text;
using KindleHub.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace KindleHub.Tests;

internal sealed record RecordedRequest(string Method, Uri Uri, string Body, Dictionary<string, string> Headers);
internal sealed class ApiHarness : IDisposable
{
    public const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string OtherToken = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    public const string Room = "123456789012";
    public List<RecordedRequest> Requests { get; } = [];
    public Func<RecordedRequest, HttpResponseMessage> Respond { get; set; } = _ => Json("[]");
    public KindleHubApiClient Api { get; }
    private readonly HttpClient http;
    public ApiHarness()
    {
        http = new HttpClient(new Handler(this));
        Api = new KindleHubApiClient(http, new KindleHubApiOptions(), NullLogger<KindleHubApiClient>.Instance);
    }
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    public void Dispose() => http.Dispose();
    private sealed class Handler(ApiHarness harness) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var recorded = new RecordedRequest(request.Method.Method, request.RequestUri!,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
            harness.Requests.Add(recorded);
            return harness.Respond(recorded);
        }
    }
}
