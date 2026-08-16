using System.Runtime.CompilerServices;
using System.Collections;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
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
var observations = (await Task.WhenAll(Cases().Select(testCase => CaptureAsync(host.Client, testCase)))).ToArray();
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

static async Task<HttpCompatibilityObservation> CaptureAsync(HttpClient client, HttpCompatibilityCase testCase)
{
    var observation = await HttpEvidenceCapture.CaptureAsync(client, testCase);
    if (!observation.Headers.TryGetValue("x-runtime-capture-binding", out var binding))
        return observation;

    var headers = new SortedDictionary<string, string>(observation.Headers.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), StringComparer.Ordinal);
    headers.Remove("x-runtime-capture-binding");
    return observation with { Binding = binding, Headers = headers };
}

static IReadOnlyList<HttpCompatibilityCase> Cases()
{
    var routes = new (HttpMethod Method, string Name, string Path, string Template)[]
    {
        (HttpMethod.Get, "get-instance", "/runtime/workflows/instances/sample", "/runtime/workflows/instances/{workflowExecutionId}"),
        (HttpMethod.Get, "list-instances", "/runtime/workflows/instances?status=Completed&definitionId=sample-definition&correlationId=sample-correlation&take=2&cursor=next&workflowExecutionId=sample-execution&artifactId=sample-artifact&from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z&runKind=Root", "/runtime/workflows/instances"),
        (HttpMethod.Get, "list-instances-page", "/runtime/workflows/instances/page?status=Completed&definitionId=sample-definition&take=2&cursor=next", "/runtime/workflows/instances/page"),
        (HttpMethod.Get, "list-executables", "/runtime/workflows/executables?scope=All&includeRetired=true", "/runtime/workflows/executables"),
        (HttpMethod.Get, "get-executable", "/runtime/workflows/executables/sample", "/runtime/workflows/executables/{artifactId}"),
        (HttpMethod.Get, "get-executable-input-sources", "/runtime/workflows/executables/sample/source-references/source/input-sources", "/runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources"),
        (HttpMethod.Get, "get-executable-provenance", "/runtime/workflows/executables/sample/provenance", "/runtime/workflows/executables/{artifactId}/provenance"),
        (HttpMethod.Post, "execute", "/runtime/workflows/executables/sample/execute", "/runtime/workflows/executables/{artifactId}/execute"),
        (HttpMethod.Post, "dispatch-stimulus", "/runtime/workflows/stimuli", "/runtime/workflows/stimuli"),
        (HttpMethod.Get, "list-dispatches", "/runtime/workflows/dispatches?parentWorkflowExecutionId=parent&childWorkflowExecutionId=child&status=Completed&take=2&afterCreatedAt=2026-01-01T00:00:00Z&afterDispatchId=after", "/runtime/workflows/dispatches"),
        (HttpMethod.Get, "get-dispatch", "/runtime/workflows/dispatches/sample", "/runtime/workflows/dispatches/{dispatchId}"),
        (HttpMethod.Post, "redrive-dispatch", "/runtime/workflows/dispatches/sample/redrive", "/runtime/workflows/dispatches/{dispatchId}/redrive"),
        (HttpMethod.Get, "get-activity-execution", "/runtime/workflows/instances/sample/activity-executions/activity", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}"),
        (HttpMethod.Get, "get-activity-descendants", "/runtime/workflows/instances/sample/activity-executions/activity/descendants?cursor=cursor&limit=2&include=incidents", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants"),
        (HttpMethod.Get, "get-activity-layout", "/runtime/workflows/instances/sample/activity-executions/activity/layout", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout"),
        (HttpMethod.Get, "get-activity-value-payload", "/runtime/workflows/instances/sample/activity-executions/activity/value-evidence/evidence/payload", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload"),
        (HttpMethod.Get, "list-incidents", "/runtime/workflows/instances/sample/incidents?blockingOnly=true", "/runtime/workflows/instances/{workflowExecutionId}/incidents"),
        (HttpMethod.Get, "get-runtime-diagnostics", "/runtime/workflows/diagnostics/settings?scope=runtime", "/runtime/workflows/diagnostics/settings"),
        (HttpMethod.Put, "save-runtime-diagnostics", "/runtime/workflows/diagnostics/settings", "/runtime/workflows/diagnostics/settings"),
        (HttpMethod.Post, "submit-alteration-plan", "/runtime/workflows/alteration-plans", "/runtime/workflows/alteration-plans"),
        (HttpMethod.Get, "get-alteration-plan", "/runtime/workflows/alteration-plans/sample", "/runtime/workflows/alteration-plans/{planId}"),
        (HttpMethod.Get, "page-alteration-jobs", "/runtime/workflows/alteration-plans/sample/jobs/page?take=2&cursor=cursor", "/runtime/workflows/alteration-plans/{planId}/jobs/page"),
        (HttpMethod.Get, "get-alteration-job", "/runtime/workflows/alteration-plans/sample/jobs/job", "/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}"),
        (HttpMethod.Post, "cancel-alteration-plan", "/runtime/workflows/alteration-plans/sample/cancel", "/runtime/workflows/alteration-plans/{planId}/cancel")
    };

    var cases = routes.Select(route => Create(route.Method, route.Name, route.Path, route.Template, null)).ToList();
    cases.AddRange(routes.Select(route =>
        Create(route.Method, route.Name + "|trusted-success", route.Path, route.Template, "trusted",
            route.Method == HttpMethod.Get ? null : route.Name == "submit-alteration-plan" ? "{\"target\":null,\"alterations\":[]}" : "{}")));
    cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name is "execute" or "redrive-dispatch") .Select(route =>
        Create(route.Method, route.Name + "|trusted-route-body-precedence", route.Path.Replace("sample", "route-id", StringComparison.Ordinal), route.Template, "trusted",
            route.Name == "execute" ? "{\"artifactId\":\"body-id\",\"sourceReferenceId\":\"body-source\"}" : "{\"dispatchId\":\"body-id\",\"requestId\":\"request-id\"}")));
    cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name != "cancel-alteration-plan").Select(route =>
        Create(route.Method, route.Name + "|trusted-malformed-json", route.Path, route.Template, "trusted", "{ malformed")));
    cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name != "cancel-alteration-plan").Select(route =>
        Create(route.Method, route.Name + "|trusted-literal-null", route.Path, route.Template, "trusted", "null", null)));
    cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name != "cancel-alteration-plan").Select(route =>
        Create(route.Method, route.Name + "|trusted-empty-body", route.Path, route.Template, "trusted", "", "application/json")));
    cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name != "cancel-alteration-plan").Select(route =>
        Create(route.Method, route.Name + "|trusted-absent-content-type", route.Path, route.Template, "trusted", "{}", null)));
    cases.Add(Create(HttpMethod.Get, "get-instance|trusted-not-found", "/runtime/workflows/instances/missing", "/runtime/workflows/instances/{workflowExecutionId}", "trusted"));
    cases.Add(Create(HttpMethod.Get, "get-activity-execution|trusted-not-found", "/runtime/workflows/instances/missing/activity-executions/missing", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", "trusted"));
    cases.Add(Create(HttpMethod.Get, "list-incidents|trusted-not-found", "/runtime/workflows/instances/missing/incidents", "/runtime/workflows/instances/{workflowExecutionId}/incidents", "trusted"));
    cases.Add(Create(HttpMethod.Get, "get-alteration-plan|trusted-not-found", "/runtime/workflows/alteration-plans/missing", "/runtime/workflows/alteration-plans/{planId}", "trusted"));
    cases.Add(Create(HttpMethod.Get, "page-alteration-jobs|trusted-not-found", "/runtime/workflows/alteration-plans/missing/jobs/page?take=2", "/runtime/workflows/alteration-plans/{planId}/jobs/page", "trusted"));
    cases.Add(Create(HttpMethod.Get, "page-alteration-jobs|trusted-invalid-take", "/runtime/workflows/alteration-plans/sample/jobs/page?take=999", "/runtime/workflows/alteration-plans/{planId}/jobs/page", "trusted"));
    cases.Add(Create(HttpMethod.Post, "submit-alteration-plan|trusted-missing-idempotency", "/runtime/workflows/alteration-plans", "/runtime/workflows/alteration-plans", "trusted", "{}"));
    return cases;

    static HttpCompatibilityCase Create(HttpMethod method, string name, string route, string template, string? identity, string? body = null, string? contentType = "application/json")
    {
        return new(new(template, method.Method), name, () =>
        {
            var request = new HttpRequestMessage(method, route);
            if (identity is not null)
                request.Headers.TryAddWithoutValidation(CaptureAuthenticationHandler.IdentityHeader, identity);
            if (method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                request.Content = contentType is null
                    ? new ByteArrayContent(Encoding.UTF8.GetBytes(body ?? "{}"))
                    : new StringContent(body ?? "{}", Encoding.UTF8, contentType);
            }
            if (name.StartsWith("submit-alteration-plan", StringComparison.Ordinal) && !name.Contains("missing-idempotency", StringComparison.Ordinal))
                request.Headers.TryAddWithoutValidation("Idempotency-Key", "capture-idempotency-key");
            return request;
        });
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
                services.AddHttpContextAccessor();
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

internal sealed class CaptureRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
{
    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
        Task.FromResult(CreateResponse<T>(request, contextAccessor.HttpContext));

    private static T CreateResponse<T>(IRequest<T> request, HttpContext? context) where T : notnull
    {
        if (context is not null)
        {
            var route = string.Join(",", context.Request.RouteValues
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Value}"));
            var requestValues = string.Join(",", request.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={Format(property.GetValue(request))}"));
            context.Response.Headers["X-Runtime-Capture-Binding"] = $"route={route};request={requestValues}";

            if (context.Request.Path.Value?.Contains("/missing", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (typeof(T).Name is "GetWorkflowInstanceResponse" or "GetActivityExecutionResponse" or "GetActivityExecutionDescendantsResponse" or "GetActivityExecutionLayoutResponse" or "GetWorkflowDispatchResponse")
                    return (T)CreateResponseWrapper(typeof(T));
                if (typeof(T).Name == "ListIncidentsResponse")
                    return (T)CreateNotFoundListIncidentsResponse();
                if (typeof(T).Name.Contains("WorkflowAlteration", StringComparison.Ordinal))
                    throw new Elsa.Workflows.Runtime.Api.Handlers.Alterations.WorkflowAlterationResourceNotFoundException();
            }

            if (context.Request.Query.TryGetValue("take", out var take) && int.TryParse(take, out var requestedTake) && requestedTake > 100)
                throw new ArgumentException("The take value must be between 1 and 100.");
        }

        return (T)CreateValue(typeof(T), typeof(T).Name, context?.Request.Path.Value?.Contains("/terminal", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static object CreateResponseWrapper(Type type)
    {
        var constructor = type.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => CreateDefault(parameter.ParameterType))
            .ToArray();
        return constructor.Invoke(arguments);
    }

    private static object CreateNotFoundListIncidentsResponse()
    {
        var constructor = typeof(ListIncidentsResponse).GetConstructors().Single();
        return constructor.Invoke([false, Array.Empty<IncidentStateView>(), 0]);
    }

    private static object? CreateDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    internal static object? CreateValue(Type type, string name, bool terminal = false, int depth = 0)
    {
        if (depth > 8)
            return CreateDefault(type);
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return CreateValue(nullable, name, terminal, depth + 1);
        if (type == typeof(string))
            return name.Contains("Status", StringComparison.OrdinalIgnoreCase) ? "Completed" : $"capture-{name.ToLowerInvariant()}";
        if (type == typeof(DateTimeOffset))
            return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        if (type == typeof(DateTime))
            return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (type == typeof(Guid))
            return Guid.Parse("11111111-1111-1111-1111-111111111111");
        if (type == typeof(bool))
            return name is "Shed" or "IsTerminalNoOp"
                ? terminal && name == "IsTerminalNoOp"
                : name == "WorkflowExists" || name == "Live" || name == "ProtectedFromCollection" || name == "RedriveEligible";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return Convert.ChangeType(1, type, System.Globalization.CultureInfo.InvariantCulture);
        if (type == typeof(JsonElement))
            return JsonDocument.Parse("{\"capture\":true}").RootElement.Clone();
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0);
        if (type == typeof(object))
            return new Dictionary<string, object> { ["capture"] = true };
        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments()))!;
        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (!type.IsClass)
            return CreateDefault(type);

        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(x => x.GetParameters().Length)
            .FirstOrDefault();
        if (constructor is null)
            return RuntimeHelpers.GetUninitializedObject(type);
        var arguments = constructor.GetParameters()
            .Select(parameter => CreateValue(parameter.ParameterType, parameter.Name ?? name, terminal, depth + 1))
            .ToArray();
        try
        {
            if (type == typeof(WorkflowExecutionStartDispatchView))
                arguments[4] = "Accepted";
            return constructor.Invoke(arguments);
        }
        catch
        {
            return RuntimeHelpers.GetUninitializedObject(type);
        }
    }

    internal static string Format(object? value) => value switch
    {
        null => "<null>",
        IEnumerable values when value is not string => $"[{string.Join("|", values.Cast<object?>().Select(Format))}]",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"
    };
}

internal sealed class CaptureCommandSender(IHttpContextAccessor contextAccessor) : ICommandSender
{
    public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull =>
        Task.FromResult(CreateResponse<T>(command, contextAccessor.HttpContext));

    public Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static T CreateResponse<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, HttpContext? context) where T : notnull
    {
        if (context is not null)
        {
            var route = string.Join(",", context.Request.RouteValues
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Value}"));
            var requestValues = string.Join(",", command.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={CaptureRequestSender.Format(property.GetValue(command))}"));
            context.Response.Headers["X-Runtime-Capture-Binding"] = $"route={route};request={requestValues}";
        }

        return (T)CaptureRequestSender.CreateValue(typeof(T), typeof(T).Name)!;
    }
}
