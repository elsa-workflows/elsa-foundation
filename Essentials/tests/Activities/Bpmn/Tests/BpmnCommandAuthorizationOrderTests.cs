using System.Security.Claims;
using System.Text.Json;
using Elsa.Activities.Bpmn.Api.Endpoints;
using Elsa.Activities.Bpmn.Core;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Ordering regressions only; real signed-in HTTP journeys qualify the host boundary separately.</summary>
public sealed class BpmnCommandAuthorizationOrderTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_denied_inspection_does_not_load_material_or_a_projection()
    {
        var runtime = new Runtime(Saved, () => null) { Inspection = () => throw new InvalidOperationException("The denied inspection ran.") };
        var caller = Caller(runtime, _ => PermissionEvaluationResult.Denied());
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => caller.InspectAsync(Saved().ExecutionId, default).AsTask());
        Assert.Equal(0, runtime.InspectionReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inspection_reauthorizes_the_exact_returned_revision_before_disclosure(bool allowNewRevision)
    {
        var original = Saved();
        var current = new RuntimeModelExecutionState(original.FormatVersion, original.Scope, original.ExecutionId, original.TenantId,
            original.Protocol, original.Executable, original.MaterialIncarnationId, original.Revision + 1, original.Lifecycle,
            original.Authority, original.CreatedAt, original.UpdatedAt, original.Payload, original.TerminalResult);
        var checks = new List<RuntimeModelExecutionState>();
        var runtime = new Runtime(() => original, () => null) { Inspection = () => Inspection(current) };
        var caller = Caller(runtime, context =>
        {
            var resource = Assert.IsType<RuntimeModelExecutionState>(context.Resource); checks.Add(resource);
            return resource.Revision == original.Revision || allowNewRevision ? PermissionEvaluationResult.Success : PermissionEvaluationResult.Denied();
        });
        if (allowNewRevision) Assert.Equal(current.Revision, (await caller.InspectAsync(original.ExecutionId, default)).Execution.Revision);
        else await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => caller.InspectAsync(original.ExecutionId, default).AsTask());
        Assert.Equal([original, current], checks); Assert.Equal(1, runtime.InspectionReads);
    }

    [Fact]
    public void Inspection_HTTP_projection_excludes_private_state_and_authored_input_values()
    {
        var state = Saved();
        var source = new WorkflowExecutableSourceReference("source", state.Executable.ArtifactId, "BpmnModel", "version", "1.0.0",
            state.Executable.DefinitionId, state.Executable.DefinitionVersionId, state.Executable.ArtifactVersion,
            state.CreatedAt, state.CreatedAt, WorkflowExecutableReferenceScope.Published,
            AuthoredInputs: [new("node", "connector", "json", JsonSerializer.SerializeToElement("private-connector-input"), true)]);
        var inspection = Inspection(state, new(source));
        var json = JsonSerializer.SerializeToElement(BpmnInspectionView.From(inspection), BpmnHostingJsonContext.Default.BpmnInspectionView);
        Assert.False(json.TryGetProperty("state", out _));
        Assert.False(json.GetProperty("execution").TryGetProperty("payload", out _));
        Assert.False(json.GetProperty("source").TryGetProperty("authoredInputs", out _));
        Assert.DoesNotContain("private-connector-input", json.GetRawText());
        Assert.Equal(state.Revision, json.GetProperty("execution").GetProperty("revision").GetInt64());
        Assert.True(json.TryGetProperty("process", out _));
    }

    private static BpmnModelInspection Inspection(RuntimeModelExecutionState state, BpmnInspectionSource? source = null) =>
        new(state, BpmnModelProtocol.Profile, JsonSerializer.SerializeToElement(new { processId = "process" }), source, [], [], [], []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_denied_caller_never_looks_up_a_retained_or_pending_command(bool pending)
    {
        var state = Saved(); var calls = new List<string>();
        var runtime = new Runtime(() => pending ? null : state, () => throw new InvalidOperationException("The denied lookup ran."));
        var caller = Caller(runtime, context =>
        {
            calls.Add("authorize");
            if (pending) AssertPendingIdentity(context.Resource, state.ExecutionId);
            else Assert.Same(state, context.Resource);
            return PermissionEvaluationResult.Denied();
        });
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => caller.CommandAsync(state.ExecutionId, "command", default).AsTask());
        Assert.Equal(0, runtime.CommandLookups); Assert.Single(calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_authorized_lookup_follows_the_resource_decision(bool pending)
    {
        var state = Saved(); var authorized = false;
        var outcome = new BpmnModelCommandOutcome(state.ExecutionId, "command", false, pending, pending ? null : JsonSerializer.SerializeToElement(new { status = "Accepted" }));
        var runtime = new Runtime(() => pending ? null : state, () => { Assert.True(authorized); return outcome; });
        var caller = Caller(runtime, context =>
        {
            authorized = true;
            if (pending) AssertPendingIdentity(context.Resource, state.ExecutionId);
            else Assert.Same(state, context.Resource);
            return PermissionEvaluationResult.Success;
        });
        Assert.Same(outcome, await caller.CommandAsync(state.ExecutionId, "command", default));
        Assert.Equal(1, runtime.CommandLookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_admission_that_becomes_retained_is_reauthorized_against_the_actual_execution(bool allowExecution)
    {
        var state = Saved(); var reads = 0; var authorizations = new List<object?>();
        var outcome = new BpmnModelCommandOutcome(state.ExecutionId, "command", true, false, JsonSerializer.SerializeToElement(new { status = "Accepted", revision = 1 }));
        var runtime = new Runtime(() => ++reads == 1 ? null : state, () => outcome);
        var caller = Caller(runtime, context =>
        {
            authorizations.Add(context.Resource);
            return context.Resource is RuntimeModelExecutionState && !allowExecution ? PermissionEvaluationResult.Denied() : PermissionEvaluationResult.Success;
        });
        if (allowExecution) Assert.Same(outcome, await caller.CommandAsync(state.ExecutionId, "command", default));
        else await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => caller.CommandAsync(state.ExecutionId, "command", default).AsTask());
        Assert.Equal(2, reads); Assert.Equal(2, authorizations.Count);
        AssertPendingIdentity(authorizations[0], state.ExecutionId); Assert.Same(state, authorizations[1]);
    }

    [Fact]
    public async Task A_retained_result_without_its_execution_remains_unavailable()
    {
        var state = Saved();
        var runtime = new Runtime(() => null, () => new(state.ExecutionId, "command", true, false, JsonSerializer.SerializeToElement(new { status = "Accepted" })));
        var caller = Caller(runtime, _ => PermissionEvaluationResult.Success);
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => caller.CommandAsync(state.ExecutionId, "command", default).AsTask());
    }

    private static void AssertPendingIdentity(object? resource, string execution)
    {
        var pending = Assert.IsType<BpmnModelCommandOutcome>(resource);
        Assert.Equal(execution, pending.ExecutionId); Assert.Equal("command", pending.InputId);
        Assert.True(pending.Pending); Assert.Null(pending.Result);
    }

    private static BpmnApiCaller Caller(IBpmnModelRuntime runtime, Func<PermissionEvaluationContext, PermissionEvaluationResult> decide)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(IdentityClaimTypes.Normalized, "v1"), new(ClaimTypes.NameIdentifier, "caller"),
            new(IdentityClaimTypes.TenantId, "default")], "ordering-unit"));
        var contexts = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        return new(contexts, new(Options.Create(new FoundationIdentityOptions { NormalizedAuthenticationTypes = new HashSet<string>(["ordering-unit"]) })), new Permission(decide), runtime);
    }

    private sealed class Permission(Func<PermissionEvaluationContext, PermissionEvaluationResult> decide) : IPermissionAuthorizationService
    {
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult(decide(context));
    }

    private sealed class Runtime(Func<RuntimeModelExecutionState?> state, Func<BpmnModelCommandOutcome?> command) : IBpmnModelRuntime
    {
        public int CommandLookups { get; private set; }
        public int InspectionReads { get; private set; }
        public Func<BpmnModelInspection>? Inspection { get; init; }
        public ValueTask<BpmnModelInspection> InspectAsync(string executionId, BpmnModelCaller caller, CancellationToken cancellationToken = default)
        { InspectionReads++; return ValueTask.FromResult(Inspection!()); }
        public ValueTask<RuntimeModelExecutionState?> FindExecutionAsync(string executionId, BpmnModelCaller caller, CancellationToken cancellationToken = default) => ValueTask.FromResult(state());
        public ValueTask<BpmnModelCommandOutcome?> FindCommandAsync(string executionId, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken = default)
        { CommandLookups++; return ValueTask.FromResult(command()); }
        public ValueTask<BpmnPublishedModel> PublishAsync(BpmnModelDefinition definition, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BpmnModelCommandOutcome> StartAsync(string sourceReferenceId, string requestId, IReadOnlyDictionary<string, JsonElement>? inputs, BpmnModelCaller caller, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BpmnHumanWork>> ListWorkAsync(string executionId, RuntimeStorePageRequest request, BpmnModelCaller caller, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BpmnModelCommandOutcome> CompleteHumanAsync(string executionId, string requestId, string invocationId, string attemptId, string[]? outcomes, BpmnModelCaller caller, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static RuntimeModelExecutionState Saved()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualHumanEmbedding.json")));
        return fixture.RootElement.GetProperty("model").Deserialize<RuntimeModelExecutionState>(Web)!;
    }
}
