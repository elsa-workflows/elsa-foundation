using System.Security.Claims;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var outputDirectory = args.Length > 1 ? args[1] : "capture-output";
Directory.CreateDirectory(outputDirectory);

await using var host = await StartHostAsync();
var observations = (await Task.WhenAll(Cases().Select(testCase => HttpEvidenceCapture.CaptureAsync(host.Client, testCase)))).ToArray();
var openApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());

File.WriteAllText(Path.Join(outputDirectory, "runtime-http-fastendpoints.json"), CompatibilityJson.Serialize(observations));
File.WriteAllText(Path.Join(outputDirectory, "runtime-openapi-fastendpoints.json"), CompatibilityJson.Serialize(openApi));
var receipt = new
{
    capture = "real-fastendpoints-historical-worktree",
    sourceCommit = Environment.GetEnvironmentVariable("RUNTIME_BEFORE_COMMIT") ?? "unknown",
    runnerCommit = Environment.GetEnvironmentVariable("RUNTIME_CAPTURE_RUNNER_COMMIT") ?? "unknown",
    registrationCount = 24,
    caseCount = observations.Length,
    operationCount = openApi.Operations.Count,
    categories = new[] { "all-routes", "anonymous-401", "authenticated-success", "binding-failure", "content-type", "problem-details", "paging-filtering", "concurrency", "alteration-statuses" },
    httpSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(outputDirectory, "runtime-http-fastendpoints.json")))).ToLowerInvariant(),
    openApiSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(outputDirectory, "runtime-openapi-fastendpoints.json")))).ToLowerInvariant()
};
File.WriteAllText(Path.Join(outputDirectory, "runtime-before-capture-receipt.json"), CompatibilityJson.Serialize(receipt));

static IReadOnlyList<HttpCompatibilityCase> Cases()
{
    var routes = new (HttpMethod Method, string Name, string Path)[]
    {
        (HttpMethod.Get, "get-instance", "/runtime/workflows/instances/sample"),
        (HttpMethod.Get, "list-instances", "/runtime/workflows/instances"),
        (HttpMethod.Get, "list-instances-page", "/runtime/workflows/instances/page"),
        (HttpMethod.Get, "list-executables", "/runtime/workflows/executables"),
        (HttpMethod.Get, "get-executable", "/runtime/workflows/executables/sample"),
        (HttpMethod.Get, "get-executable-input-sources", "/runtime/workflows/executables/sample/source-references/source/input-sources"),
        (HttpMethod.Get, "get-executable-provenance", "/runtime/workflows/executables/sample/provenance"),
        (HttpMethod.Post, "execute", "/runtime/workflows/executables/sample/execute"),
        (HttpMethod.Post, "dispatch-stimulus", "/runtime/workflows/stimuli"),
        (HttpMethod.Get, "list-dispatches", "/runtime/workflows/dispatches"),
        (HttpMethod.Get, "get-dispatch", "/runtime/workflows/dispatches/sample"),
        (HttpMethod.Post, "redrive-dispatch", "/runtime/workflows/dispatches/sample/redrive"),
        (HttpMethod.Get, "get-activity-execution", "/runtime/workflows/instances/sample/activity-executions/activity"),
        (HttpMethod.Get, "get-activity-descendants", "/runtime/workflows/instances/sample/activity-executions/activity/descendants"),
        (HttpMethod.Get, "get-activity-layout", "/runtime/workflows/instances/sample/activity-executions/activity/layout"),
        (HttpMethod.Get, "get-activity-value-payload", "/runtime/workflows/instances/sample/activity-executions/activity/value-evidence/evidence/payload"),
        (HttpMethod.Get, "list-incidents", "/runtime/workflows/instances/sample/incidents"),
        (HttpMethod.Get, "get-runtime-diagnostics", "/runtime/workflows/diagnostics/settings"),
        (HttpMethod.Put, "save-runtime-diagnostics", "/runtime/workflows/diagnostics/settings"),
        (HttpMethod.Post, "submit-alteration-plan", "/runtime/workflows/alteration-plans"),
        (HttpMethod.Get, "get-alteration-plan", "/runtime/workflows/alteration-plans/sample"),
        (HttpMethod.Get, "page-alteration-jobs", "/runtime/workflows/alteration-plans/sample/jobs/page"),
        (HttpMethod.Get, "get-alteration-job", "/runtime/workflows/alteration-plans/sample/jobs/job"),
        (HttpMethod.Post, "cancel-alteration-plan", "/runtime/workflows/alteration-plans/sample/cancel")
    };

    var cases = routes.Select(route => Create(route.Method, route.Name, route.Path, null)).ToList();
    cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get).Select(route =>
        Create(route.Method, route.Name + "|trusted-malformed-json", route.Path, "trusted", "{ malformed")));
    cases.Add(Create(HttpMethod.Get, "list-instances|trusted-paging-filtering", "/runtime/workflows/instances?page=2&pageSize=10&status=completed", "trusted"));
    cases.Add(Create(HttpMethod.Get, "page-alteration-jobs|trusted-invalid-take", "/runtime/workflows/alteration-plans/sample/jobs/page?take=999", "trusted"));
    cases.Add(Create(HttpMethod.Post, "submit-alteration-plan|trusted-missing-idempotency", "/runtime/workflows/alteration-plans", "trusted", "{}"));
    return cases;

    static HttpCompatibilityCase Create(HttpMethod method, string name, string route, string? identity, string? body = null, string contentType = "application/json")
    {
        var endpointRoute = route.Split('?', 2)[0]
            .Replace("sample", "{param}", StringComparison.Ordinal)
            .Replace("activity", "{param}", StringComparison.Ordinal)
            .Replace("source", "{param}", StringComparison.Ordinal)
            .Replace("evidence", "{param}", StringComparison.Ordinal)
            .Replace("job", "{param}", StringComparison.Ordinal);
        return new(new(endpointRoute, method.Method), name, () =>
        {
            var request = new HttpRequestMessage(method, route);
            if (identity is not null)
                request.Headers.TryAddWithoutValidation(CaptureAuthenticationHandler.IdentityHeader, identity);
            if (method != HttpMethod.Get && method != HttpMethod.Delete)
                request.Content = new StringContent(body ?? "{}", Encoding.UTF8, contentType);
            return request;
        })
        {
            Binding = "route=workflowExecutionId,artifactId,sourceReferenceId,dispatchId,activityExecutionId,evidenceId,planId,jobId;body=request",
            PagingFiltering = route.Contains('?', StringComparison.Ordinal) ? "query=?page=2&pageSize=10;link=" : ""
        };
    }
}

static async Task<CaptureHost> StartHostAsync()
{
    var host = new HostBuilder()
        .ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.UseSetting(WebHostDefaults.ApplicationKey, "Elsa.Workflows.Runtime.Api");
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IHostEnvironment>(new CaptureHostEnvironment("Elsa.Workflows.Runtime.Api"));
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication(CaptureAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, CaptureAuthenticationHandler>(CaptureAuthenticationHandler.SchemeName, _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options =>
                    options.NormalizedAuthenticationTypes = new HashSet<string>([CaptureAuthenticationHandler.SchemeName], StringComparer.Ordinal));
                services.AddOpenApi();
                new WorkflowsRuntimeApiFeature().ConfigureServices(services);
                services.AddSingleton<IRequestSender, CaptureRequestSender>();
                services.AddSingleton<ICommandSender, CaptureCommandSender>();
                services.AddFastEndpoints(options => options.Assemblies = [typeof(WorkflowsRuntimeApiFeature).Assembly]);
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapFastEndpoints();
                    endpoints.MapOpenApi();
                });
            });
        })
        .Build();
    await host.StartAsync();
    return new CaptureHost(host);
}

internal sealed class CaptureHostEnvironment(string applicationName) : IHostEnvironment
{
    public string ApplicationName { get; set; } = applicationName;
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class CaptureHost(IHost host) : IAsyncDisposable
{
    public HttpClient Client { get; } = host.GetTestClient();

    public async Task<string> GetOpenApiAsync()
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
}

internal sealed class CaptureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "RuntimeBeforeCapture";
    public const string IdentityHeader = "X-Runtime-Capture-Identity";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(IdentityHeader, out var header) || string.IsNullOrWhiteSpace(header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new ClaimsIdentity(Scheme.Name);
        claims.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
        claims.AddClaim(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
        claims.AddClaim(new Claim(IdentityClaimTypes.TenantId, "capture-tenant"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), Scheme.Name)));
    }
}

internal sealed class CaptureRequestSender : IRequestSender
{
    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
        Task.FromResult((T)RuntimeHelpers.GetUninitializedObject(typeof(T)));
}

internal sealed class CaptureCommandSender : ICommandSender
{
    public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull =>
        Task.FromResult((T)RuntimeHelpers.GetUninitializedObject(typeof(T)));

    public Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
