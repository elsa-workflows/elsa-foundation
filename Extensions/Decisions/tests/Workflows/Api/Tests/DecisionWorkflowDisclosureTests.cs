using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Decisions.Core;
using Elsa.Decisions.Workflows.Api;
using Elsa.Decisions.Workflows.Runtime;
using Elsa.Decisions.Workflows.Runtime.Activities;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions.Workflows.Api.Tests;

public sealed class DecisionWorkflowDisclosureTests
{
    [Fact]
    public async Task Exact_exported_closure_checks_transitive_decision_source_and_preserves_workflow_grants()
    {
        using var fixture = new Fixture();
        var child = fixture.Native("decision", true); var root = fixture.Native("parent", false, [child]);
        var closure = new WorkflowArtifactClosure(1, root.Identity.ArtifactId, [root, child], [], []);
        var principal = fixture.Principal("workflows/publishing:read", DecisionPermissions.Read);
        Assert.False((await fixture.Authorize(principal, "workflows/publishing:read", closure)).Succeeded);
        principal = fixture.Principal("workflows/publishing:read", DecisionPermissions.Read, DecisionPermissions.Source);
        Assert.True((await fixture.Authorize(principal, "workflows/publishing:read", closure)).Succeeded);
        // Domain grants alone never replace the ordinary workflow permission.
        Assert.False((await fixture.Authorize(fixture.Principal(DecisionPermissions.Read, DecisionPermissions.Source), "workflows/publishing:read", closure)).Succeeded);
    }

    [Fact]
    public async Task Extra_or_missing_dependency_material_cannot_escape_source_authorization()
    {
        using var fixture = new Fixture(); var child = fixture.Native("child", true); var root = fixture.Native("root", false, [child]);
        var principal = fixture.Principal("workflows/publishing:read", DecisionPermissions.Read, DecisionPermissions.Source);
        Assert.False((await fixture.Authorize(principal, "workflows/publishing:read", new WorkflowArtifactClosure(1, root.Identity.ArtifactId, [root], [], []))).Succeeded);
        var plain = fixture.Native("plain", false);
        Assert.False((await fixture.Authorize(principal, "workflows/publishing:read", new WorkflowArtifactClosure(1, plain.Identity.ArtifactId, [plain, child], [], []))).Succeeded);
    }

    [Fact]
    public async Task Native_retained_value_read_requires_decision_read_but_structure_remains_a_workflow_permission()
    {
        using var fixture = new Fixture(); var executable = fixture.Native("decision", true); fixture.Add(executable);
        var state = fixture.State(executable);
        Assert.False((await fixture.Authorize(fixture.Principal("workflows/activity-executions:inspect-values"), "workflows/activity-executions:inspect-values", state)).Succeeded);
        Assert.True((await fixture.Authorize(fixture.Principal("workflows/activity-executions:inspect-values", DecisionPermissions.Read), "workflows/activity-executions:inspect-values", state)).Succeeded);
        Assert.True((await fixture.Authorize(fixture.Principal("workflows/activity-executions:inspect"), "workflows/activity-executions:inspect", state)).Succeeded);
        // Removing a current grant changes the very next disclosure, without changing accepted execution state.
        Assert.False((await fixture.Authorize(fixture.Principal("workflows/activity-executions:inspect-values"), "workflows/activity-executions:inspect-values", state)).Succeeded);
    }

    [Fact]
    public async Task Actual_protected_policy_requires_sensitive_grant_in_addition_to_read()
    {
        using var fixture = new Fixture(); var executable = fixture.Native("protected", true, protectedValues: true); fixture.Add(executable);
        var state = fixture.State(executable);
        Assert.False((await fixture.Authorize(fixture.Principal("workflows/activity-executions:resolve-value-payloads", DecisionPermissions.Read), "workflows/activity-executions:resolve-value-payloads", state)).Succeeded);
        Assert.True((await fixture.Authorize(fixture.Principal("workflows/activity-executions:resolve-value-payloads", DecisionPermissions.Read, DecisionPermissions.SensitiveValues), "workflows/activity-executions:resolve-value-payloads", state)).Succeeded);
    }

    [Fact]
    public async Task Direct_model_read_traverses_native_worker_material_with_exact_pin_checks()
    {
        using var fixture = new Fixture(); var worker = fixture.Native("worker", true); fixture.Add(worker);
        var identity = new WorkflowExecutableIdentity("bpmn", "definition", "version", "1", "model-hash");
        var material = new RuntimeModelMaterial(1, identity, new("bpmn", 1), "profile", JsonSerializer.SerializeToElement(new { }), new(1, []), [], [new("service", worker.Identity)], []);
        fixture.Materials[identity.ArtifactId] = new RuntimeExecutionMaterial(material, "incarnation");
        var now = DateTimeOffset.UtcNow;
        var state = new RuntimeModelExecutionState(1, new("tenant"), "model-execution", "tenant", material.Protocol, identity,
            "incarnation", 1, RuntimeModelLifecycle.TerminalSettled, new("system", "audit"), now, now, JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { result = "value" }));
        Assert.False((await fixture.Authorize(fixture.Principal("bpmn:read"), "bpmn:read", state)).Succeeded);
        Assert.True((await fixture.Authorize(fixture.Principal("bpmn:read", DecisionPermissions.Read), "bpmn:read", state)).Succeeded);
        fixture.Materials.Remove(worker.Identity.ArtifactId);
        Assert.False((await fixture.Authorize(fixture.Principal("bpmn:read", DecisionPermissions.Read), "bpmn:read", state)).Succeeded);
    }

    [Theory]
    [InlineData("missing-pin")]
    [InlineData("corrupt-pin")]
    [InlineData("wrong-tenant")]
    public async Task Decision_disclosure_fails_closed_for_unverifiable_material(string kind)
    {
        using var fixture = new Fixture(); var node = fixture.Node(true, kind); var executable = fixture.Native("bad", node); fixture.Add(executable);
        Assert.False((await fixture.Authorize(fixture.Principal("workflows/activity-executions:inspect-values", DecisionPermissions.Read), "workflows/activity-executions:inspect-values", fixture.State(executable))).Succeeded);
    }

    [Fact]
    public async Task Ordinary_workflows_retain_their_existing_value_read_behavior()
    {
        using var fixture = new Fixture(); var executable = fixture.Native("ordinary", false); fixture.Add(executable);
        Assert.True((await fixture.Authorize(fixture.Principal("workflows/activity-executions:inspect-values"), "workflows/activity-executions:inspect-values", fixture.State(executable))).Succeeded);
    }

    [Fact]
    public async Task Default_library_visibility_is_unchanged_for_this_nonfiltering_contribution()
    {
        using var fixture = new Fixture();
        var policy = fixture.Provider.GetRequiredService<IWorkflowLibraryVisibilityPolicy>();
        var scope = await policy.GetScopeAsync(fixture.Principal("workflows/design:read"));
        Assert.Equal("tenant-action-v1", scope.Revision); Assert.Null(scope.AllowedDefinitionIds); Assert.Null(scope.DeniedDefinitionIds);
        var other = new AnotherHandler();
        var strict = new DecisionLibraryVisibilityPolicy([fixture.Provider.GetRequiredService<IEnumerable<IPermissionResourceHandler>>().Single(), other]);
        await Assert.ThrowsAsync<DecisionInvocationException>(async () => await strict.GetScopeAsync(fixture.Principal("workflows/design:read")));
    }

    [Fact]
    public void Explicit_host_library_visibility_is_preserved()
    {
        var services = new ServiceCollection(); var custom = new CustomVisibility();
        services.AddSingleton<IWorkflowLibraryVisibilityPolicy>(custom);
        new DecisionsWorkflowsApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        Assert.Same(custom, provider.GetRequiredService<IWorkflowLibraryVisibilityPolicy>());
    }

    private sealed class Fixture : IDisposable
    {
        public ServiceProvider Provider { get; }
        public HttpContextAccessor Context { get; } = new();
        public Dictionary<string, RuntimeExecutionMaterial> Materials { get; } = new(StringComparer.Ordinal);
        public Fixture()
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "test" });
            services.AddSingleton<IHttpContextAccessor>(Context); services.AddSingleton<IPermissionContributor, TestPermissions>();
            var store = Port<IWorkflowExecutableStore>((name, args) => name switch
            {
                "get_SupportsModelMaterial" => true,
                "FindMaterialAsync" => ValueTask.FromResult(Materials.GetValueOrDefault((string)args![0]!)),
                _ => throw new NotSupportedException(name)
            });
            services.AddSingleton(store); new DecisionsWorkflowsApiFeature().ConfigureServices(services);
            Provider = services.BuildServiceProvider(); Context.HttpContext = new DefaultHttpContext { RequestServices = Provider };
        }
        public ClaimsPrincipal Principal(params string[] permissions) => new(new ClaimsIdentity(permissions.Select(x => new Claim(IdentityClaimTypes.Permission, x))
            .Append(new Claim(IdentityClaimTypes.Normalized, "v1")).Append(new Claim(IdentityClaimTypes.UserId, "user"))
            .Append(new Claim(IdentityClaimTypes.TenantId, "tenant")), "test"));
        public void SetPrincipal(ClaimsPrincipal principal) => Context.HttpContext!.User = principal;
        public ValueTask<PermissionEvaluationResult> Authorize(ClaimsPrincipal principal, string permission, object resource) =>
            Provider.GetRequiredService<IPermissionAuthorizationService>().AuthorizeAsync(new(principal, permission, "tenant", resource));
        public void Add(WorkflowExecutable executable) => Materials[executable.Identity.ArtifactId] = new(executable, "incarnation");
        public WorkflowExecutionState State(WorkflowExecutable executable) => new("execution", executable.Identity, WorkflowExecutionStatus.Completed,
            null, DateTimeOffset.UtcNow, null, null, null, null, null, "tenant", new Dictionary<string, string>());
        public ExecutableNode Node(bool decision, string? malformed = null, bool protectedValues = false)
        {
            var engine = new DecisionEngine(); var artifact = engine.Compile(Xml).Artifact!;
            var metadata = new Dictionary<string, string>();
            if (decision && malformed != "missing-pin") metadata[DecisionWorkflowPin.MetadataKey] = malformed == "corrupt-pin" ? "invalid" :
                new DecisionWorkflowPin("asset@version", malformed == "wrong-tenant" ? "other" : "tenant", "asset", "version", artifact).Serialize();
            var inputs = new Dictionary<string, RuntimeInputBinding>();
            if (protectedValues)
            {
                var type = new ValueTypeDescriptor("System.String"); var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
                inputs["Inputs"] = new("Inputs", type, policy, RuntimeInputBindingSource.Literal, ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("value"), policy));
            }
            return new("node", "node", decision ? EvaluateDecision.ActivityType : "Ordinary", "1", "test", JsonSerializer.SerializeToElement(new { }), inputs, metadata);
        }
        public WorkflowExecutable Native(string id, bool decision, IReadOnlyList<WorkflowExecutable>? children = null, bool protectedValues = false) => Native(id, Node(decision, protectedValues: protectedValues), children);
        public WorkflowExecutable Native(string id, ExecutableNode node, IReadOnlyList<WorkflowExecutable>? children = null) => new(new(id, "definition", "version", "1", "hash-" + id), node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), null,
            children?.Select(x => new WorkflowExecutableDependency(x.Identity.ArtifactId, x.Identity.ArtifactHash, ["node"])).ToArray(), new("Fault", "1"));
        public void Dispose() { Context.HttpContext = null; Provider.Dispose(); }
        private const string Xml = """
          <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="definitions" name="Policy" namespace="urn:test"><decision id="policy"><decisionTable id="table"><input id="input"><inputExpression id="expression" typeRef="number"><text>amount</text></inputExpression></input><output id="output" name="result" typeRef="string"/><rule id="rule"><inputEntry id="test"><text>-</text></inputEntry><outputEntry id="answer"><text>"Answer"</text></outputEntry></rule></decisionTable></decision></definitions>
          """;
    }
    private sealed class TestPermissions : IPermissionContributor
    {
        public IEnumerable<Permission> Contribute() => new[] { DecisionPermissions.Read, DecisionPermissions.Source, DecisionPermissions.SensitiveValues, "workflows/publishing:read", "workflows/activity-executions:inspect", "workflows/activity-executions:inspect-values", "workflows/activity-executions:resolve-value-payloads", "bpmn:read", "workflows/design:read" }
            .Select(key => new Permission(key, key, "Tests", key));
    }
    private sealed class AnotherHandler : IPermissionResourceHandler
    { public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult<PermissionEvaluationResult?>(null); }
    private sealed class CustomVisibility : IWorkflowLibraryVisibilityPolicy
    { public ValueTask<WorkflowLibraryReadScope> GetScopeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => ValueTask.FromResult(new WorkflowLibraryReadScope("custom", ["allowed"])); }
    private static T Port<T>(Func<string, object?[]?, object?> route) where T : class
    { var proxy = DispatchProxy.Create<T, RoutingProxy>(); ((RoutingProxy)(object)proxy).Route = route; return proxy; }
    public class RoutingProxy : DispatchProxy
    { public Func<string, object?[]?, object?> Route { get; set; } = null!; protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Route(targetMethod!.Name, args); }
}
