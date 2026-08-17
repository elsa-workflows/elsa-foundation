using System.Security.Claims;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Services;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Primitives.Exceptions;
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
using MediatorCommand = Elsa.Mediator.Core.Contracts.ICommand;
using HistoricalOpenApiEvidenceCapture = Elsa.Workflows.Design.FastEndpointsCapture.HistoricalOpenApiEvidenceCapture;

var outputDirectory = args.Length > 1 ? args[1] : "capture-output";
Directory.CreateDirectory(outputDirectory);

await using var host = await StartHostAsync();
var cases = Cases();
var observations = (await Task.WhenAll(cases.Select(testCase => HttpEvidenceCapture.CaptureAsync(host.Client, testCase)))).ToArray();
var openApi = HistoricalOpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync(), includeIdentityMetadata: true);
var trace = CaptureTrace.Snapshot();
CaptureTrace.AssertMinimum(trace);

File.WriteAllText(Path.Join(outputDirectory, "workflows-design-http-fastendpoints.json"), CompatibilityJson.Serialize(observations));
File.WriteAllText(Path.Join(outputDirectory, "workflows-design-openapi-fastendpoints.json"), CompatibilityJson.Serialize(openApi));
File.WriteAllText(Path.Join(outputDirectory, "workflows-design-handler-trace-fastendpoints.json"), CompatibilityJson.Serialize(trace));
var receipt = new
{
    capture = "real-fastendpoints-historical-worktree",
    sourceCommit = Environment.GetEnvironmentVariable("WORKFLOWS_DESIGN_BEFORE_COMMIT") ?? "unknown",
    runnerCommit = Environment.GetEnvironmentVariable("WORKFLOWS_DESIGN_CAPTURE_RUNNER_COMMIT") ?? "unknown",
    captureCommand = $"WORKFLOWS_DESIGN_BEFORE_COMMIT={Environment.GetEnvironmentVariable("WORKFLOWS_DESIGN_BEFORE_COMMIT") ?? "unknown"} WORKFLOWS_DESIGN_CAPTURE_RUNNER_COMMIT={Environment.GetEnvironmentVariable("WORKFLOWS_DESIGN_CAPTURE_RUNNER_COMMIT") ?? "unknown"} bash tools/compatibility/capture-workflows-design-before.sh",
    registrationCount = 27,
    caseCount = observations.Length,
    operationCount = openApi.Operations.Count,
    categories = observations.SelectMany(observation => observation.Case.Split('|', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
    traceAssertions = new[] { "request-and-command-types-recorded", "lifecycle-body-properties-recorded", "list-query-properties-recorded", "promotion-error-paths-recorded", "permanent-delete-conflict-recorded" },
    runnerDependencies = RunnerDependencies(
        args[0],
        Environment.GetEnvironmentVariable("WORKFLOWS_DESIGN_BEFORE_COMMIT") ?? "unknown",
        Environment.GetEnvironmentVariable("WORKFLOWS_DESIGN_CAPTURE_RUNNER_COMMIT") ?? "unknown"),
    httpSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(outputDirectory, "workflows-design-http-fastendpoints.json")))).ToLowerInvariant(),
    openApiSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(outputDirectory, "workflows-design-openapi-fastendpoints.json")))).ToLowerInvariant(),
    traceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(outputDirectory, "workflows-design-handler-trace-fastendpoints.json")))).ToLowerInvariant()
};
File.WriteAllText(Path.Join(outputDirectory, "workflows-design-before-capture-receipt.json"), CompatibilityJson.Serialize(receipt));

static IReadOnlyList<object> RunnerDependencies(string sourceRoot, string sourceCommit, string runnerCommit) =>
new[]
{
    "tools/compatibility/capture-workflows-design-before.sh",
    "tools/compatibility/WorkflowsDesignFastEndpointsCapture/Program.cs",
    "tools/compatibility/WorkflowsDesignFastEndpointsCapture/WorkflowsDesignFastEndpointsCapture.csproj",
    "tools/compatibility/WorkflowsDesignFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs",
    "tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs",
    "tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs",
    "tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs"
}.Select(path => new
{
    path,
    commit = path.StartsWith("tools/compatibility/", StringComparison.Ordinal) ? runnerCommit : sourceCommit,
    sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(sourceRoot, path)))).ToLowerInvariant()
}).Cast<object>().ToArray();

static IReadOnlyList<HttpCompatibilityCase> Cases()
{
    var routes = new (HttpMethod Method, string Name, string Path)[]
    {
        (HttpMethod.Post, "analyze-scoped-variables", "/design/workflows/scoped-variables/analyze"),
        (HttpMethod.Post, "complete-expression-tooling", "/design/workflows/expression-tooling/completions"),
        (HttpMethod.Get, "describe-expression-tooling", "/design/workflows/expression-tooling/descriptors"),
        (HttpMethod.Post, "hover-expression-tooling", "/design/workflows/expression-tooling/hover"),
        (HttpMethod.Post, "resolve-activity-input-options", "/design/workflows/activities/sample/inputs/name/options"),
        (HttpMethod.Post, "resolve-expression-tooling-context", "/design/workflows/expression-tooling/context"),
        (HttpMethod.Post, "search-expression-tooling-symbols", "/design/workflows/expression-tooling/symbols"),
        (HttpMethod.Post, "validate-expression-tooling", "/design/workflows/expression-tooling/validate"),
        (HttpMethod.Post, "add-definition", "/design/workflows/definitions"),
        (HttpMethod.Delete, "delete-definition", "/design/workflows/definitions/sample"),
        (HttpMethod.Delete, "delete-definition-permanently", "/design/workflows/definitions/sample/permanent"),
        (HttpMethod.Get, "get-definition", "/design/workflows/definitions/sample"),
        (HttpMethod.Get, "list-definitions", "/design/workflows/definitions"),
        (HttpMethod.Post, "restore-definition", "/design/workflows/definitions/sample/restore"),
        (HttpMethod.Post, "submit-definition", "/design/workflows/definitions/submit"),
        (HttpMethod.Get, "submit-definition-schema", "/design/workflows/definitions/submit/schema"),
        (HttpMethod.Patch, "update-definition", "/design/workflows/definitions/sample"),
        (HttpMethod.Delete, "discard-draft", "/design/workflows/drafts/sample"),
        (HttpMethod.Get, "get-draft", "/design/workflows/drafts/sample"),
        (HttpMethod.Post, "promote-draft", "/design/workflows/drafts/sample/promote"),
        (HttpMethod.Post, "promotion-preflight", "/design/workflows/drafts/sample/promotion-preflight"),
        (HttpMethod.Put, "replace-draft", "/design/workflows/drafts/sample"),
        (HttpMethod.Get, "draft-validations", "/design/workflows/drafts/sample/validations"),
        (HttpMethod.Get, "list-structures", "/design/workflows/structures"),
        (HttpMethod.Post, "add-version", "/design/workflows/versions/ingest"),
        (HttpMethod.Get, "get-version", "/design/workflows/versions/sample"),
        (HttpMethod.Get, "list-versions", "/design/workflows/definitions/sample/versions")
    };

    var cases = routes.Select(route => Create(route.Method, route.Name, route.Path, null)).ToList();
    cases.AddRange(routes.Where(route => route.Name != "describe-expression-tooling").Select(route => Create(route.Method, $"{route.Name}|trusted-success", route.Path,
        "trusted-success", BodyFor(route.Name))));
    cases.Add(Create(HttpMethod.Get, "describe-expression-tooling|trusted-success", "/design/workflows/expression-tooling/descriptors", "trusted-success"));
    cases.Add(Create(HttpMethod.Post, "analyze-scoped-variables|trusted-malformed-json", "/design/workflows/scoped-variables/analyze", "trusted-malformed-json", "{ malformed"));
    cases.Add(Create(HttpMethod.Post, "add-definition|trusted-unsupported-content-type", "/design/workflows/definitions", "trusted-unsupported-content-type", "{}", "text/plain"));
    cases.Add(Create(HttpMethod.Get, "list-definitions|paging-filtering", "/design/workflows/definitions?searchTerm=sample&tenantAgnostic=false&state=published", "trusted-paging"));
    cases.Add(Create(HttpMethod.Get, "get-definition|trusted-not-found", "/design/workflows/definitions/sample", "trusted-not-found"));
    cases.Add(Create(HttpMethod.Post, "promote-draft|trusted-404", "/design/workflows/drafts/sample/promote", "trusted-promote-404", BodyFor("promote-draft")));
    cases.Add(Create(HttpMethod.Post, "promote-draft|trusted-409-concurrency", "/design/workflows/drafts/sample/promote", "trusted-promote-409", BodyFor("promote-draft")));
    cases.Add(Create(HttpMethod.Post, "promote-draft|trusted-500", "/design/workflows/drafts/sample/promote", "trusted-promote-500", BodyFor("promote-draft")));
    cases.Add(Create(HttpMethod.Delete, "delete-definition-permanently|trusted-501", "/design/workflows/definitions/sample/permanent", "trusted-delete-501", BodyFor("delete-definition-permanently")));
    cases.Add(Create(HttpMethod.Delete, "delete-definition-permanently|trusted-404", "/design/workflows/definitions/sample/permanent", "trusted-delete-404", BodyFor("delete-definition-permanently")));
    cases.Add(Create(HttpMethod.Delete, "delete-definition-permanently|trusted-409-not-soft-deleted", "/design/workflows/definitions/sample/permanent", "trusted-delete-409", BodyFor("delete-definition-permanently")));
    cases.Add(Create(HttpMethod.Delete, "delete-definition-permanently|trusted-500", "/design/workflows/definitions/sample/permanent", "trusted-delete-500", BodyFor("delete-definition-permanently")));
    cases.Add(Create(HttpMethod.Post, "promotion-preflight|trusted-nonmutation", "/design/workflows/drafts/sample/promotion-preflight", "trusted-preflight", "{\"requestedVersion\":\"1.0.0\"}"));
    foreach (var lifecycle in new[]
    {
        (HttpMethod.Delete, "delete-definition", "/design/workflows/definitions/sample"),
        (HttpMethod.Delete, "delete-definition-permanently", "/design/workflows/definitions/sample/permanent"),
        (HttpMethod.Post, "restore-definition", "/design/workflows/definitions/sample/restore"),
        (HttpMethod.Delete, "discard-draft", "/design/workflows/drafts/sample")
    })
    {
        cases.Add(Create(lifecycle.Item1, $"{lifecycle.Item2}|trusted-missing-body", lifecycle.Item3, "trusted-manage"));
        cases.Add(Create(lifecycle.Item1, $"{lifecycle.Item2}|trusted-malformed-json", lifecycle.Item3, "trusted-manage", "{"));
        cases.Add(Create(lifecycle.Item1, $"{lifecycle.Item2}|trusted-unsupported-content-type", lifecycle.Item3, "trusted-manage", "{}", "text/plain"));
    }
    return cases;

    static string? BodyFor(string name) => name switch
    {
        "complete-expression-tooling" => "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\",\"cursor\":{\"line\":0,\"character\":14}}",
        "hover-expression-tooling" => "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\",\"position\":{\"line\":0,\"character\":14}}",
        "resolve-expression-tooling-context" or "search-expression-tooling-symbols" => "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"contextRevision\":null,\"search\":\"symbol\",\"skip\":0,\"take\":20}",
        "validate-expression-tooling" => "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\"}",
        "analyze-scoped-variables" => "{\"state\":{\"activities\":[],\"connections\":[]},\"nodeId\":null}",
        "resolve-activity-input-options" => "{\"activityVersionId\":\"activity\",\"inputName\":\"name\",\"nodeId\":null,\"workflowState\":null}",
        "add-definition" => "{\"operationKey\":\"capture-add\",\"name\":\"Capture definition\",\"description\":\"capture\"}",
        "submit-definition" => "{\"operationKey\":\"capture-submit\",\"name\":\"Capture definition\",\"description\":\"capture\",\"state\":{\"activities\":[],\"connections\":[]}}",
        "update-definition" => "{\"operationKey\":\"capture-update\",\"name\":\"Capture definition\",\"description\":\"capture\"}",
        "delete-definition" => "{\"operationKey\":\"capture-delete\",\"definitionId\":\"body-definition\",\"reason\":\"capture\"}",
        "delete-definition-permanently" => "{\"operationKey\":\"capture-permanent\",\"definitionId\":\"body-definition\"}",
        "restore-definition" => "{\"operationKey\":\"capture-restore\",\"definitionId\":\"body-definition\"}",
        "discard-draft" => "{\"operationKey\":\"capture-discard\",\"draftId\":\"body-draft\"}",
        "promote-draft" => "{\"operationKey\":\"capture-promote\",\"draftId\":\"body-draft\",\"requestedVersion\":\"1.0.0\"}",
        "promotion-preflight" => "{\"draftId\":\"body-draft\",\"requestedVersion\":\"1.0.0\"}",
        "replace-draft" => "{\"operationKey\":\"capture-replace\",\"draftId\":\"body-draft\",\"state\":{\"activities\":[],\"connections\":[]}}",
        "add-version" => "{\"operationKey\":\"capture-version\",\"definitionId\":\"body-definition\",\"state\":{\"activities\":[],\"connections\":[]}}",
        _ => null
    };

    static HttpCompatibilityCase Create(HttpMethod method, string name, string route, string? identity, string? body = null, string contentType = "application/json")
    {
        var endpointRoute = route.Split('?', 2)[0]
            .Replace("sample", "{param}", StringComparison.Ordinal)
            .Replace("name", "{param}", StringComparison.Ordinal);
        return new(new(endpointRoute, method.Method), name, () =>
        {
            var request = new HttpRequestMessage(method, route);
            if (identity is not null)
                request.Headers.TryAddWithoutValidation(CaptureAuthenticationHandler.IdentityHeader, identity);
            if (method != HttpMethod.Get && body is not null)
                request.Content = new StringContent(body ?? "{}", Encoding.UTF8, contentType);
            return request;
        })
        {
            Binding = DescribeBinding(route, body),
            PagingFiltering = DescribeQuery(route)
        };
    }

    static string DescribeBinding(string route, string? body)
    {
        var path = route.Split('?', 2)[0];
        var routeParts = new[]
        {
            path.Contains("activities/", StringComparison.Ordinal) ? "activityVersionId" : null,
            path.Contains("inputs/name", StringComparison.Ordinal) ? "inputName" : null,
            path.Contains("definitions/sample", StringComparison.Ordinal) ? "definitionId" : null,
            path.Contains("drafts/sample", StringComparison.Ordinal) ? "draftId" : null,
            path.Contains("versions/sample", StringComparison.Ordinal) ? "versionId" : null
        }.Where(value => value is not null).ToArray();
        var bodyFields = "none";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                bodyFields = string.Join(',', document.RootElement.EnumerateObject().Select(property => property.Name));
            }
            catch (JsonException)
            {
                bodyFields = "malformed";
            }
        }
        return $"route={string.Join(',', routeParts)};query={DescribeQuery(route)};body={bodyFields}";
    }

    static string DescribeQuery(string route)
    {
        var query = route.Split('?', 2).ElementAtOrDefault(1);
        return string.IsNullOrWhiteSpace(query) ? "" : $"query=?{query};link=";
    }
}

static async Task<CaptureHost> StartHostAsync()
{
    var host = new HostBuilder()
        .ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.UseSetting(WebHostDefaults.ApplicationKey, "Elsa.Workflows.Design.Api");
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IHostEnvironment>(new CaptureHostEnvironment("Elsa.Workflows.Design.Api"));
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication(CaptureAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, CaptureAuthenticationHandler>(CaptureAuthenticationHandler.SchemeName, _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options =>
                    options.NormalizedAuthenticationTypes = new HashSet<string>([CaptureAuthenticationHandler.SchemeName], StringComparer.Ordinal));
                services.AddOpenApi();
                new WorkflowsDesignApiFeature().ConfigureServices(services);
                services.AddSingleton<IExpressionToolingProviderResolver, ExpressionToolingProviderResolver>();
                services.AddSingleton<ICommandSender, CaptureCommandSender>();
                services.AddSingleton<IRequestSender, CaptureRequestSender>();
                services.AddSingleton<IActivityDefinitionVersionStore, CaptureActivityDefinitionVersionStore>();
                services.AddFastEndpoints(options => options.Assemblies = [typeof(WorkflowsDesignApiFeature).Assembly]);
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
    Console.Error.WriteLine($"captureApplicationName={host.Services.GetRequiredService<IHostEnvironment>().ApplicationName}");
    return new CaptureHost(host);
}

sealed class CaptureHostEnvironment(string applicationName) : IHostEnvironment
{
    public string ApplicationName { get; set; } = applicationName;
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class CaptureHost(IHost host) : IAsyncDisposable
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

sealed class CaptureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "WorkflowsDesignBeforeCapture";
    public const string IdentityHeader = "X-Workflows-Design-Capture-Identity";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(IdentityHeader, out var header) || string.IsNullOrWhiteSpace(header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new ClaimsIdentity(Scheme.Name);
        claims.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
        claims.AddClaim(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
        claims.AddClaim(new Claim(IdentityClaimTypes.TenantId, "capture-tenant"));
        CaptureScenario.Current = header.ToString();
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), Scheme.Name)));
    }
}

sealed class CaptureCommandSender(IHttpContextAccessor contextAccessor) : ICommandSender
{
    private string? Scenario => contextAccessor.HttpContext?.Request.Headers[CaptureAuthenticationHandler.IdentityHeader].ToString();

    public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull
    {
        CaptureTrace.Record("command", command, Scenario);
        return Scenario switch
        {
            "trusted-promote-404" => throw new EntityNotFoundException("draft sample was not found"),
            "trusted-promote-409" => throw new WorkflowDefinitionVersionConflictException("definition sample", "1.0.0"),
            "trusted-promote-500" => throw new InvalidOperationException("deterministic command failure"),
            _ => Task.FromResult(default(T)!)
        };
    }

    public Task Send(MediatorCommand command, CancellationToken cancellationToken = default)
    {
        CaptureTrace.Record("command", command, Scenario);
        return Scenario switch
        {
            "trusted-delete-404" => throw new EntityNotFoundException("definition sample was not found"),
            "trusted-delete-501" => throw new PermanentDeletionUnavailableException("sample"),
            "trusted-delete-409" => throw new WorkflowDefinitionNotSoftDeletedException("sample"),
            "trusted-delete-500" => throw new InvalidOperationException("deterministic command failure"),
            _ => Task.CompletedTask
        };
    }
}

sealed class CaptureRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
{
    private string? Scenario => contextAccessor.HttpContext?.Request.Headers[CaptureAuthenticationHandler.IdentityHeader].ToString();

    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
    {
        CaptureTrace.Record("request", request, Scenario);
        return Scenario switch
        {
            "trusted-not-found" => throw new EntityNotFoundException("definition sample was not found"),
            "trusted-paging" when request is ListDefinitions => Task.FromResult((T)(object)new WorkflowDefinitionListView([])),
            "trusted-preflight" when request is PreflightDraftPromotion => Task.FromResult((T)(object)new PromotionPreflightAssessmentView(true, "exact", "1.0.0", "1.0.0", "1.0.0", [])),
            "trusted-success" => Task.FromResult(default(T)!),
            _ => Task.FromResult(default(T)!)
        };
    }
}

static class CaptureTrace
{
    private static readonly ConcurrentBag<object> Entries = [];

    public static void Record(string kind, object value, string? scenario) =>
        Entries.Add(new
        {
            kind,
            scenario,
            type = value.GetType().Name,
            properties = value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0)
                .ToDictionary(property => property.Name, property => Convert.ToString(property.GetValue(value), System.Globalization.CultureInfo.InvariantCulture) ?? "", StringComparer.Ordinal)
        });

    public static object[] Snapshot() => Entries.OrderBy(entry => entry.ToString(), StringComparer.Ordinal).ToArray();

    public static void AssertMinimum(IEnumerable<object> entries)
    {
        var text = string.Join('\n', entries.Select(entry => entry.ToString()));
        foreach (var required in new[] { "ListDefinitions", "PreflightDraftPromotion", "PromoteDraft", "DeleteDefinitionPermanently", "SoftDeleteDefinition", "RestoreDefinition", "DiscardDraft" })
            if (!text.Contains(required, StringComparison.Ordinal))
                throw new InvalidOperationException($"Historical capture did not execute required FE handler input '{required}'.");
        if (!text.Contains("trusted-delete-409", StringComparison.Ordinal))
            throw new InvalidOperationException("Historical capture did not execute permanent-delete conflict handling.");
    }
}

static class CaptureScenario
{
    private static readonly AsyncLocal<string?> Value = new();
    public static string? Current { get => Value.Value; set => Value.Value = value; }
}

sealed class CaptureActivityDefinitionVersionStore : IActivityDefinitionVersionStore
{
    private static InvalidOperationException NotExecuted() => new("The historical capture did not execute an activity-definition store operation.");

    public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw NotExecuted();
    public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw NotExecuted();
    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw NotExecuted();
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw NotExecuted();
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw NotExecuted();
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw NotExecuted();
}
