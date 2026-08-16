using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediatorCommand = Elsa.Mediator.Core.Contracts.ICommand;

var outputDirectory = args.Length > 1 ? args[1] : "capture-output";
Directory.CreateDirectory(outputDirectory);

await using var host = await StartHostAsync();
var cases = Cases();
var observations = (await Task.WhenAll(cases.Select(testCase => HttpEvidenceCapture.CaptureAsync(host.Client, testCase)))).ToArray();
var openApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());

File.WriteAllText(Path.Join(outputDirectory, "workflows-design-http-fastendpoints.json"), CompatibilityJson.Serialize(observations));
File.WriteAllText(Path.Join(outputDirectory, "workflows-design-openapi-fastendpoints.json"), CompatibilityJson.Serialize(openApi));
var receipt = new
{
    capture = "real-fastendpoints-historical-worktree",
    sourceCommit = Environment.GetEnvironmentVariable("WORKFLOWS_DESIGN_BEFORE_COMMIT") ?? "unknown",
    runnerCommit = Environment.GetEnvironmentVariable("WORKFLOWS_DESIGN_CAPTURE_RUNNER_COMMIT") ?? "unknown",
    registrationCount = 27,
    caseCount = observations.Length,
    operationCount = openApi.Operations.Count,
    categories = new[] { "anonymous-401", "authenticated-success", "binding-failure", "content-type", "problem-details", "paging-filtering", "headers", "concurrency", "preflight-nonmutation", "all-routes" },
    httpSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(outputDirectory, "workflows-design-http-fastendpoints.json")))).ToLowerInvariant(),
    openApiSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(outputDirectory, "workflows-design-openapi-fastendpoints.json")))).ToLowerInvariant()
};
File.WriteAllText(Path.Join(outputDirectory, "workflows-design-before-capture-receipt.json"), CompatibilityJson.Serialize(receipt));

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
    cases.Add(Create(HttpMethod.Get, "describe-expression-tooling|trusted-success", "/design/workflows/expression-tooling/descriptors", "trusted-success"));
    cases.Add(Create(HttpMethod.Post, "analyze-scoped-variables|trusted-malformed-json", "/design/workflows/scoped-variables/analyze", "trusted-malformed-json", "{ malformed"));
    cases.Add(Create(HttpMethod.Post, "add-definition|trusted-unsupported-content-type", "/design/workflows/definitions", "trusted-unsupported-content-type", "{}", "text/plain"));
    cases.Add(Create(HttpMethod.Get, "list-definitions|paging-filtering", "/design/workflows/definitions?page=2&pageSize=10&search=sample", "trusted-paging"));
    cases.Add(Create(HttpMethod.Get, "get-definition|trusted-not-found", "/design/workflows/definitions/sample", "trusted-not-found"));
    cases.Add(Create(HttpMethod.Post, "promote-draft|trusted-404", "/design/workflows/drafts/sample/promote", "trusted-promote-404", "{}"));
    cases.Add(Create(HttpMethod.Post, "promote-draft|trusted-409-concurrency", "/design/workflows/drafts/sample/promote", "trusted-promote-409", "{}"));
    cases.Add(Create(HttpMethod.Post, "promote-draft|trusted-500", "/design/workflows/drafts/sample/promote", "trusted-promote-500", "{}"));
    cases.Add(Create(HttpMethod.Delete, "delete-definition-permanently|trusted-501", "/design/workflows/definitions/sample/permanent", "trusted-delete-501"));
    cases.Add(Create(HttpMethod.Delete, "delete-definition-permanently|trusted-404", "/design/workflows/definitions/sample/permanent", "trusted-delete-404"));
    cases.Add(Create(HttpMethod.Delete, "delete-definition-permanently|trusted-500", "/design/workflows/definitions/sample/permanent", "trusted-delete-500"));
    cases.Add(Create(HttpMethod.Post, "promotion-preflight|trusted-nonmutation", "/design/workflows/drafts/sample/promotion-preflight", "trusted-preflight", "{\"requestedVersion\":\"1.0.0\"}"));
    return cases;

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
            if (method != HttpMethod.Get && method != HttpMethod.Delete)
                request.Content = new StringContent(body ?? "{}", Encoding.UTF8, contentType);
            return request;
        })
        {
            Binding = "route=definitionId,draftId,versionId,activityVersionId,inputName;body=request",
            PagingFiltering = route.Contains('?', StringComparison.Ordinal) ? "query=?page=2&pageSize=10&search=sample;link=" : ""
        };
    }
}

static async Task<CaptureHost> StartHostAsync()
{
    var host = new HostBuilder()
        .ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.UseSetting(WebHostDefaults.ApplicationKey, "workflows-design-fastendpoints-capture");
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
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
    return new CaptureHost(host);
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

    public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull =>
        Scenario switch
        {
            "trusted-promote-404" => throw new EntityNotFoundException("draft sample was not found"),
            "trusted-promote-409" => throw new WorkflowDefinitionVersionConflictException("definition sample", "1.0.0"),
            "trusted-promote-500" => throw new InvalidOperationException("deterministic command failure"),
            _ => Task.FromResult(default(T)!)
        };

    public Task Send(MediatorCommand command, CancellationToken cancellationToken = default) =>
        Scenario switch
        {
            "trusted-delete-404" => throw new EntityNotFoundException("definition sample was not found"),
            "trusted-delete-501" => throw new PermanentDeletionUnavailableException("sample"),
            "trusted-delete-500" => throw new InvalidOperationException("deterministic command failure"),
            _ => Task.CompletedTask
        };
}

sealed class CaptureRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
{
    private string? Scenario => contextAccessor.HttpContext?.Request.Headers[CaptureAuthenticationHandler.IdentityHeader].ToString();

    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
        Scenario switch
        {
            "trusted-not-found" => throw new EntityNotFoundException("definition sample was not found"),
            "trusted-paging" when request is ListDefinitions => Task.FromResult((T)(object)new WorkflowDefinitionListView([])),
            "trusted-preflight" when request is PreflightDraftPromotion => Task.FromResult((T)(object)new PromotionPreflightAssessmentView(true, "exact", "1.0.0", "1.0.0", "1.0.0", [])),
            "trusted-success" => Task.FromResult(default(T)!),
            _ => Task.FromResult(default(T)!)
        };
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
