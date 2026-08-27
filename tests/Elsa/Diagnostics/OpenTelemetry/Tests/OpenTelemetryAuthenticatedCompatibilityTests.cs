using Elsa.Api.AspNetCore;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

public sealed class OpenTelemetryAuthenticatedCompatibilityTests
{
    private static readonly string BaselineDirectory = Path.Combine(AppContext.BaseDirectory, "Baselines");

    [Fact]
    public async Task Authenticated_and_binding_cases_match_the_deleted_fastendpoints_oracle()
    {
        using var host = await StartHostAsync();
        var baseline = await LoadCasesAsync("otel-http-authenticated-fastendpoints.json");

        foreach (var expected in baseline)
        {
            using var request = CreateRequest(expected);
            using var response = await host.GetTestClient().SendAsync(
                request,
                expected.Path.Contains("/stream", StringComparison.Ordinal)
                    ? HttpCompletionOption.ResponseHeadersRead
                    : HttpCompletionOption.ResponseContentRead);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(expected.Status, (int)response.StatusCode);
            Assert.Equal(expected.ResponseContentType, response.Content.Headers.ContentType?.ToString() ?? string.Empty);
            Assert.Equal(expected.ResponseBody, body);
            foreach (var header in expected.Headers ?? new Dictionary<string, string>())
            {
                var actual = response.Headers.TryGetValues(header.Key, out var values)
                    ? values.Single()
                    : response.Content.Headers.TryGetValues(header.Key, out var contentValues) ? contentValues.Single() : null;
                Assert.Equal(header.Value, actual);
            }
        }
    }

    [Fact]
    public async Task Post_binding_cases_match_fastendpoints_for_null_content_type_and_errors()
    {
        using var host = await StartHostAsync();
        var baseline = await LoadCasesAsync("otel-http-binding-fastendpoints.json");

        foreach (var expected in baseline)
        {
            using var request = CreateRequest(expected);
            using var response = await host.GetTestClient().SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(expected.Status, (int)response.StatusCode);
            Assert.Equal(expected.ResponseContentType, response.Content.Headers.ContentType?.ToString() ?? string.Empty);
            Assert.Equal(expected.ResponseBody, body);
        }
    }

    private static async Task<FixtureCase[]> LoadCasesAsync(string fileName)
    {
        await using var stream = File.OpenRead(Path.Combine(BaselineDirectory, fileName));
        var fixture = await JsonSerializer.DeserializeAsync<Fixture>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return fixture?.Cases ?? throw new InvalidOperationException($"Fixture '{fileName}' was empty.");
    }

    private static HttpRequestMessage CreateRequest(FixtureCase expected)
    {
        var request = new HttpRequestMessage(new HttpMethod(expected.Method), expected.Path);
        if (expected.Body is not null)
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(expected.Body));
            if (expected.ContentType is not null)
                request.Content.Headers.ContentType = new(expected.ContentType);
        }

        if (expected.Identity is not null)
            request.Headers.Add("X-Test-Identity", expected.Identity);
        return request;
    }

    private static async Task<IHost> StartHostAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddElsaEndpoints();
                services.AddLogging();
                services.AddRouting();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "otel-test";
                    options.DefaultChallengeScheme = "otel-test";
                }).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("otel-test", _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options =>
                    options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "otel-test" });
                new OpenTelemetryFeature().ConfigureServices(services);
                services.AddSingleton<IOpenTelemetryLiveFeed, SnapshotLiveFeed>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => new OpenTelemetryFeature().MapEndpoints(endpoints, null));
            });
        });
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers["X-Test-Identity"].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var permission = identity == "exact"
                ? "Diagnostics:OpenTelemetry.Read"
                : "Diagnostics:Other";
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(IdentityClaimTypes.Permission, permission),
                new Claim(IdentityClaimTypes.Normalized, "v1")
            ], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }

    private sealed class SnapshotLiveFeed : IOpenTelemetryLiveFeed
    {
        public ValueTask PublishAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<OpenTelemetryStreamItem> SubscribeAsync(
            OpenTelemetryTraceFilter filter,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new OpenTelemetryStreamItem
            {
                Resource = new TelemetryResource(
                    "resource-1",
                    "service-1",
                    null,
                    "dotnet",
                    new Dictionary<string, string?>(),
                    new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
                    TelemetryResourceStatus.Active)
            };
            await Task.CompletedTask;
        }
    }

    private sealed record Fixture(FixtureCase[] Cases);

    private sealed record FixtureCase(
        string Name,
        string Method,
        string Path,
        string? Body,
        string? ContentType,
        string? Identity,
        int Status,
        string ResponseContentType,
        string ResponseBody,
        Dictionary<string, string>? Headers);
}
