using System.Collections.Concurrent;
using System.Security.Claims;
using CShells.FastEndpoints.Contracts;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Sources;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Tests.Support;

/// <summary>
/// Deterministic, plain TestServer host for capturing the current FastEndpoints contract. It deliberately
/// maps the real legacy endpoint types and leaves production registration untouched.
/// </summary>
public sealed class StructuredLogsApiHost : IAsyncDisposable
{
    public const string IdentityHeader = "X-Structured-Logs-Canary-Identity";
    public const string ExactIdentity = "exact";
    public const string WildcardIdentity = "wildcard";
    public const string AdjacentIdentity = "adjacent";
    public const string UntrustedIdentity = "untrusted";
    public const string ResourceDeniedIdentity = "resource-denied";
    public const string Permission = "Diagnostics:StructuredLogs";

    private const string SchemeName = "structured-logs-canary";
    private readonly IHost host;

    private StructuredLogsApiHost(IHost host, IReadOnlyList<EndpointDataSource> endpointDataSources)
    {
        this.host = host;
        Client = host.GetTestClient();
        EndpointDataSources = endpointDataSources;
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => host.Services;

    public IReadOnlyList<EndpointDataSource> EndpointDataSources { get; }

    public static Task<StructuredLogsApiHost> StartLegacyAsync(
        bool customPaths = false,
        bool seed = true) => StartAsync(customPaths, seed);

    public static async Task<IReadOnlyList<HttpCompatibilityObservation>> CaptureAsync(
        IReadOnlyList<HttpCompatibilityCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var observations = new List<HttpCompatibilityObservation>(cases.Count);

        foreach (var testCase in cases)
        {
            using var request = testCase.CreateRequest();
            var customPaths = request.RequestUri?.ToString().StartsWith("/canary/", StringComparison.Ordinal) == true;
            await using var canary = await StartAsync(customPaths, seed: !testCase.BoundedStreaming || testCase.Case == "stream-valid-resume");
            observations.Add(testCase.BoundedStreaming
                ? await canary.CaptureStreamCaseAsync(testCase)
                : await HttpEvidenceCapture.CaptureAsync(canary.Client, testCase));
        }

        return observations;
    }

    public static void ResetPermissionEvaluatorObservations() => RecordingPermissionEvaluator.Reset();

    public static int PermissionEvaluatorCallsFor(string path) => RecordingPermissionEvaluator.CallsFor(path);

    public async Task<IReadOnlyList<StructuredLogEntry>> AppendAsync(
        IEnumerable<StructuredLogEntry> entries,
        bool publish = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var committed = new List<StructuredLogEntry>();
        var store = Services.GetRequiredService<IStructuredLogStore>();
        var publisher = Services.GetRequiredService<IStructuredLogLivePublisher>();
        foreach (var entry in entries)
        {
            var value = await store.AppendAsync(entry, cancellationToken);
            committed.Add(value);
            if (publish)
                publisher.Publish(value);
        }

        return committed;
    }

    public Task<IReadOnlyList<StructuredLogEntry>> AppendAsync(
        params StructuredLogEntry[] entries) => AppendAsync(entries, publish: true);

    public async Task<string> GetCurrentOpenApiDocumentAsync()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    private static async Task<StructuredLogsApiHost> StartAsync(bool customPaths, bool seed)
    {
        IReadOnlyList<EndpointDataSource>? endpointDataSources = null;
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
                    services.AddRouting();
                    services.AddAuthentication(SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, CanaryAuthenticationHandler>(SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddOpenApi();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { SchemeName });
                    services.ReplacePermissionEvaluator<RecordingPermissionEvaluator>();
                    services.AddScoped<IPermissionResourceHandler, CanaryPermissionResourceHandler>();

                    new StructuredLogsFeature
                    {
                        ServiceName = "structured-logs-canary",
                        SourceDisplayName = "Structured Logs Canary",
                        BufferCapacity = 16
                    }.ConfigureServices(services);

                    // The production provider includes machine/process diagnostics. The canary uses a
                    // fixed source value so repeated captures are byte-stable across test processes.
                    services.AddSingleton<IStructuredLogSourceProvider, DeterministicSourceProvider>();
                    services.Configure<StructuredLogsOptions>(options =>
                    {
                        options.RecentPath = customPaths ? StructuredLogsCompatibilityCases.CustomRecentPath : StructuredLogsCompatibilityCases.RecentPath;
                        options.SourcesPath = customPaths ? StructuredLogsCompatibilityCases.CustomSourcesPath : StructuredLogsCompatibilityCases.SourcesPath;
                        options.StreamPath = customPaths ? StructuredLogsCompatibilityCases.CustomStreamPath : StructuredLogsCompatibilityCases.StreamPath;
                        options.MaxRecentQuerySize = 3;
                        options.TailPollInterval = TimeSpan.FromMilliseconds(10);
                    });

                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(StructuredLogsFeature).Assembly];
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints(config =>
                        {
                            using var scope = endpoints.ServiceProvider.CreateScope();
                            foreach (var configurator in scope.ServiceProvider.GetServices<IFastEndpointsConfigurator>())
                                configurator.Configure(config);
                        });

                        endpoints.MapOpenApi();
                        endpointDataSources = endpoints.DataSources.ToArray();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        var result = new StructuredLogsApiHost(host, endpointDataSources ?? []);
        if (seed)
            await result.AppendAsync(CreateSeedEntries());
        return result;
    }

    private async Task<HttpCompatibilityObservation> CaptureStreamCaseAsync(HttpCompatibilityCase testCase)
    {
        var name = testCase.Case;
        if (name is "stream-malformed-cursor" or "stream-unavailable-cursor")
            return await HttpEvidenceCapture.CaptureAsync(Client, testCase);

        using var request = testCase.CreateRequest();
        using var cancellation = new CancellationTokenSource();
        StructuredLogsStreamObservation capture;

        switch (name)
        {
            case "stream-valid-resume":
            {
                var recent = await Services.GetRequiredService<IStructuredLogStore>().GetRecentAsync(new StructuredLogFilter { MaxCount = 3 });
                var first = AssertCursor(recent.FirstOrDefault());
                request.Headers.Remove("Last-Event-ID");
                request.Headers.TryAddWithoutValidation("Last-Event-ID", first.Value);
                capture = await StructuredLogsStreamReader.CaptureAsync(
                    Client,
                    request,
                    new StructuredLogsStreamReaderOptions(testCase.MaxStreamFrames, testCase.MaxStreamBytes),
                    cancellation.Token);
                break;
            }
            case "stream-initial-entry":
                capture = await CaptureAfterHeadersAsync(request, testCase, cancellation, [CreateEntry(20, Microsoft.Extensions.Logging.LogLevel.Information, "Canary.Initial", "initial-entry")]);
                break;
            case "stream-filtered-entry":
                capture = await CaptureAfterHeadersAsync(request, testCase, cancellation,
                    [CreateEntry(21, Microsoft.Extensions.Logging.LogLevel.Information, "Canary.Other", "filtered-out"),
                     CreateEntry(22, Microsoft.Extensions.Logging.LogLevel.Warning, "Canary.Warning", "filtered-in")]);
                break;
            case "stream-custom-path":
                capture = await CaptureAfterHeadersAsync(request, testCase, cancellation, [CreateEntry(23, Microsoft.Extensions.Logging.LogLevel.Information, "Canary.Custom", "custom-entry")]);
                break;
            case "stream-heartbeat":
                cancellation.CancelAfter(TimeSpan.FromSeconds(17));
                capture = await StructuredLogsStreamReader.CaptureAsync(
                    Client,
                    request,
                    new StructuredLogsStreamReaderOptions(testCase.MaxStreamFrames, testCase.MaxStreamBytes),
                    cancellation.Token);
                break;
            case "stream-cancelled":
                capture = await CaptureThenCancelAsync(request, testCase, cancellation);
                break;
            default:
                throw new InvalidOperationException($"Unknown structured logs stream case '{name}'.");
        }

        return ToObservation(testCase, capture);
    }

    private async Task<StructuredLogsStreamObservation> CaptureAfterHeadersAsync(
        HttpRequestMessage request,
        HttpCompatibilityCase testCase,
        CancellationTokenSource cancellation,
        IReadOnlyList<StructuredLogEntry> entries)
    {
        return await StructuredLogsStreamReader.CaptureAsync(
            Client,
            request,
            new StructuredLogsStreamReaderOptions(testCase.MaxStreamFrames, testCase.MaxStreamBytes),
            cancellation.Token,
            async _ => await AppendAsync(entries));
    }

    private async Task<StructuredLogsStreamObservation> CaptureThenCancelAsync(
        HttpRequestMessage request,
        HttpCompatibilityCase testCase,
        CancellationTokenSource cancellation)
    {
        return await StructuredLogsStreamReader.CaptureAsync(
            Client,
            request,
            new StructuredLogsStreamReaderOptions(testCase.MaxStreamFrames, testCase.MaxStreamBytes),
            cancellation.Token,
            async _ =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                await cancellation.CancelAsync();
            });
    }

    private static HttpCompatibilityObservation ToObservation(
        HttpCompatibilityCase testCase,
        StructuredLogsStreamObservation capture) =>
        new()
        {
            Endpoint = testCase.Endpoint,
            Case = testCase.Case,
            Binding = testCase.Binding ?? string.Empty,
            Json = string.Empty,
            StatusCode = capture.StatusCode,
            ContentType = capture.ContentType,
            Headers = capture.Headers,
            Body = NormalizeCursorIds(capture.RawText),
            ProblemDetails = capture.StatusCode >= 400 ? NormalizeCursorIds(capture.RawText) : string.Empty,
            PagingFiltering = testCase.PagingFiltering ?? string.Empty,
            Streaming = NormalizeCursorIds(capture.FrameText),
            TerminalState = capture.TerminalState
        };

    private static string NormalizeCursorIds(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return string.Join(
            "\n",
            normalized.Split('\n').Select(line =>
                line.StartsWith("id: ", StringComparison.Ordinal) ? "id: <cursor>" : line));
    }

    private static StructuredLogReplayCursor AssertCursor(StructuredLogEntry? entry) =>
        entry?.ReplayCursor is { IsValid: true } cursor
            ? cursor
            : throw new InvalidOperationException("The canary seed did not produce a valid replay cursor.");

    private static IReadOnlyList<StructuredLogEntry> CreateSeedEntries() =>
    [
        CreateEntry(1, Microsoft.Extensions.Logging.LogLevel.Information, "Canary.Information", "information-entry"),
        CreateEntry(2, Microsoft.Extensions.Logging.LogLevel.Warning, "Canary.Warning", "warning-entry"),
        CreateEntry(3, Microsoft.Extensions.Logging.LogLevel.Error, "Canary.Error", "error-entry")
    ];

    private static StructuredLogEntry CreateEntry(long sequence, Microsoft.Extensions.Logging.LogLevel level, string category, string message) =>
        new()
        {
            Sequence = sequence,
            Timestamp = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero).AddSeconds(sequence),
            Level = level,
            Category = category,
            EventId = (int)sequence,
            EventName = $"Canary{sequence}",
            Message = message,
            MessageTemplate = $"{message}-template",
            Properties = [new LogProperty("marker", $"marker-{sequence}")],
            SourceId = "structured-logs-canary"
        };

    private sealed class DeterministicSourceProvider : IStructuredLogSourceProvider
    {
        private static readonly LogSource Source = new()
        {
            Id = "structured-logs-canary",
            DisplayName = "Structured Logs Canary",
            ServiceName = "structured-logs-canary",
            MachineName = "canary-machine",
            ProcessId = 4242
        };

        public LogSource GetLocalSource() => Source;

        public IReadOnlyList<LogSource> GetKnownSources() => [Source];
    }

    private sealed class CanaryPermissionResourceHandler(IHttpContextAccessor accessor) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            var httpContext = context.Resource as HttpContext ?? accessor.HttpContext;
            return ValueTask.FromResult<PermissionEvaluationResult?>(
                httpContext?.Request.Headers[IdentityHeader].ToString() == ResourceDeniedIdentity
                    ? PermissionEvaluationResult.Denied("The canary resource denied the request.")
                    : null);
        }
    }

    private sealed class RecordingPermissionEvaluator(IPermissionCatalog catalog) : IPermissionEvaluator
    {
        private static readonly ConcurrentDictionary<string, int> Calls = new(StringComparer.Ordinal);
        private readonly ClaimsPermissionEvaluator inner = new(catalog);

        public async ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Resource is HttpContext httpContext)
                Calls.AddOrUpdate(httpContext.Request.Path.Value ?? string.Empty, 1, static (_, count) => count + 1);

            return await inner.EvaluateAsync(context, cancellationToken);
        }

        public static void Reset() => Calls.Clear();

        public static int CallsFor(string path) => Calls.GetValueOrDefault(path);
    }

    private sealed class CanaryAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(IdentityHeader, out var values))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = values.ToString();
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.Normalized, identity == UntrustedIdentity ? "legacy" : "v1"),
                new(IdentityClaimTypes.Provider, "structured-logs-canary")
            };

            var permission = identity switch
            {
                ExactIdentity => Permission,
                WildcardIdentity => PermissionKey.Wildcard,
                AdjacentIdentity => "Diagnostics:StructuredLog",
                ResourceDeniedIdentity => Permission,
                _ => null
            };
            if (permission is not null)
                claims.Add(new Claim(IdentityClaimTypes.Permission, permission));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
