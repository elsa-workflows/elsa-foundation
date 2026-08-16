using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

/// <summary>
/// Baseline-only capture harness. This test is committed on the FastEndpoints source commit before
/// the Minimal API migration and is the only code allowed to produce the supplemental fixtures.
/// </summary>
public sealed class OpenTelemetryFastEndpointsCaptureHarnessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] PermissionQueryPaths =
    [
        "/diagnostics/opentelemetry/resources/search",
        "/diagnostics/opentelemetry/traces/search",
        "/diagnostics/opentelemetry/metrics/search",
        "/diagnostics/opentelemetry/logs/search",
        "/diagnostics/opentelemetry/traces/missing",
        "/diagnostics/opentelemetry/storage",
        "/diagnostics/opentelemetry/collector-configuration",
        "/_elsa/studio/diagnostics/opentelemetry/stream"
    ];

    [Fact]
    public async Task Capture_deleted_fastendpoints_http_supplements()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("OTEL_CAPTURE_OUTPUT");
        // The harness is included in the owner test project so the capture implementation remains
        // reviewable, but ordinary owner-suite runs must not write fixtures. The receipt command sets
        // this variable explicitly and therefore exercises the real capture path.
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory!);

        using var host = await StartHostAsync(cookieChallenge: false);
        var authenticated = await CaptureAuthenticatedAsync(host.GetTestClient());
        var binding = await CaptureBindingAsync(host.GetTestClient());
        await File.WriteAllTextAsync(Path.Combine(outputDirectory!, "otel-http-authenticated-fastendpoints.json"), JsonSerializer.Serialize(new { capturedAt = "db6e363db", cases = authenticated }, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory!, "otel-http-binding-fastendpoints.json"), JsonSerializer.Serialize(new { capturedAt = "db6e363db", cases = binding }, JsonOptions));

        using var cookieHost = await StartHostAsync(cookieChallenge: true);
        using var cookieClient = cookieHost.GetTestClient();
        cookieClient.DefaultRequestHeaders.Clear();
        cookieClient.DefaultRequestHeaders.Add("Accept", "application/json");
        var redirects = new List<object>();
        foreach (var path in PermissionQueryPaths)
        {
            using var request = new HttpRequestMessage(new HttpMethod(path.EndsWith("search", StringComparison.Ordinal) ? "POST" : "GET"), path);
            if (request.Method == HttpMethod.Post)
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await cookieClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            redirects.Add(new
            {
                method = request.Method.Method,
                path,
                status = (int)response.StatusCode,
                location = response.Headers.Location?.ToString() ?? string.Empty
            });
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory!, "otel-http-redirect-fastendpoints.json"),
            JsonSerializer.Serialize(new { sourceCommit = "db6e363db", capture = "anonymous cookie-challenge redirects from the deleted FastEndpoints host", cases = redirects }, JsonOptions));
    }

    private static async Task<List<object>> CaptureAuthenticatedAsync(HttpClient client)
    {
        var cases = new List<object>();
        cases.Add(await CaptureAsync(client, "resources-exact", HttpMethod.Post, "/diagnostics/opentelemetry/resources/search", "{}", "application/json", "exact"));
        cases.Add(await CaptureAsync(client, "traces-filter", HttpMethod.Post, "/diagnostics/opentelemetry/traces/search", "{\"traceId\":\"trace-1\"}", "application/json", "exact"));
        cases.Add(await CaptureAsync(client, "trace-missing", HttpMethod.Get, "/diagnostics/opentelemetry/traces/missing", null, null, "exact"));
        cases.Add(await CaptureAsync(client, "storage", HttpMethod.Get, "/diagnostics/opentelemetry/storage", null, null, "exact"));
        cases.Add(await CaptureAsync(client, "anonymous-storage", HttpMethod.Get, "/diagnostics/opentelemetry/storage", null, null, null));
        cases.Add(await CaptureAsync(client, "lacking-storage", HttpMethod.Get, "/diagnostics/opentelemetry/storage", null, null, "lacking"));
        cases.Add(await CaptureAsync(client, "stream-invalid", HttpMethod.Get, "/_elsa/studio/diagnostics/opentelemetry/stream?status=bad", null, null, "exact"));
        cases.Add(await CaptureAsync(client, "stream-success", HttpMethod.Get, "/_elsa/studio/diagnostics/opentelemetry/stream", null, null, "exact"));
        return cases;
    }

    private static async Task<List<object>> CaptureBindingAsync(HttpClient client)
    {
        var cases = new List<object>();
        cases.Add(await CaptureAsync(client, "resources-null", HttpMethod.Post, "/diagnostics/opentelemetry/resources/search", "null", "application/json", "exact"));
        cases.Add(await CaptureAsync(client, "resources-absent", HttpMethod.Post, "/diagnostics/opentelemetry/resources/search", "{}", null, "exact"));
        cases.Add(await CaptureAsync(client, "resources-text", HttpMethod.Post, "/diagnostics/opentelemetry/resources/search", "{}", "text/plain", "exact"));
        cases.Add(await CaptureAsync(client, "resources-empty", HttpMethod.Post, "/diagnostics/opentelemetry/resources/search", string.Empty, "application/json", "exact"));
        cases.Add(await CaptureAsync(client, "resources-malformed", HttpMethod.Post, "/diagnostics/opentelemetry/resources/search", "{", "application/json", "exact"));
        return cases;
    }

    private static async Task<object> CaptureAsync(HttpClient client, string name, HttpMethod method, string path, string? body, string? contentType, string? identity)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            if (contentType is not null)
                request.Content.Headers.ContentType = new(contentType);
        }
        if (identity is not null)
            request.Headers.Add("X-Test-Identity", identity);

        using var response = await client.SendAsync(request, path.Contains("/stream", StringComparison.Ordinal) ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in new[] { "Cache-Control", "X-Accel-Buffering" })
        {
            if (response.Headers.TryGetValues(header, out var values))
                headers[header] = values.Single();
            else if (response.Content.Headers.TryGetValues(header, out var contentValues))
                headers[header] = contentValues.Single();
        }

        return new
        {
            name,
            method = method.Method,
            path,
            body,
            contentType,
            identity,
            status = (int)response.StatusCode,
            responseContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            responseBody = await response.Content.ReadAsStringAsync(),
            headers = headers.Count == 0 ? null : headers
        };
    }

    private static async Task<IHost> StartHostAsync(bool cookieChallenge)
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                if (cookieChallenge)
                    services.AddAuthentication("otel-cookie").AddCookie("otel-cookie");
                else
                    services.AddAuthentication("otel-test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("otel-test", _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { cookieChallenge ? "otel-cookie" : "otel-test" });
                new OpenTelemetryFeature().ConfigureServices(services);
                services.AddSingleton<IOpenTelemetryLiveFeed, SnapshotLiveFeed>();
                services.AddFastEndpoints(options => options.Assemblies = [typeof(OpenTelemetryFeature).Assembly]);
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapFastEndpoints());
            });
        });
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private sealed class HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers["X-Test-Identity"].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var permission = identity == "exact" ? "Diagnostics:OpenTelemetry" : "Diagnostics:Other";
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(IdentityClaimTypes.Permission, permission), new Claim(IdentityClaimTypes.Normalized, "v1")],
                Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class SnapshotLiveFeed : IOpenTelemetryLiveFeed
    {
        public ValueTask PublishAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<OpenTelemetryStreamItem> SubscribeAsync(OpenTelemetryTraceFilter filter, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new OpenTelemetryStreamItem
            {
                Resource = new TelemetryResource("resource-1", "service-1", null, "dotnet", new Dictionary<string, string?>(), new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero), TelemetryResourceStatus.Active)
            };
            await Task.CompletedTask;
        }
    }
}
