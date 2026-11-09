using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Decisions.Core;
using Elsa.Decisions.Persistence.Core;
using Elsa.Decisions.Workflows.Design;
using Elsa.Decisions.Workflows.Runtime;
using Elsa.Decisions.Workflows.Runtime.Activities;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Decisions.Workflows.Tests;

public sealed class DecisionWorkflowTests
{
    private const string Xml = """
        <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="definitions" name="Approval" namespace="urn:test">
          <decision id="approval" name="Approval"><decisionTable id="table" hitPolicy="UNIQUE">
            <input id="amount"><inputExpression id="amountExpr" typeRef="number"><text>amount</text></inputExpression></input>
            <output id="level" name="level" typeRef="string" />
            <rule id="automatic"><inputEntry id="aIn"><text>&lt;= 1000</text></inputEntry><outputEntry id="aOut"><text>"Automatic"</text></outputEntry></rule>
            <rule id="manager"><inputEntry id="mIn"><text>&gt; 1000</text></inputEntry><outputEntry id="mOut"><text>"Manager"</text></outputEntry></rule>
          </decisionTable></decision>
        </definitions>
        """;

    [Fact]
    public void Tagged_inputs_preserve_exact_values_under_the_runtime_object_materializer_serializer()
    {
        // Runtime Object expressions use the default CLR serializer rather than API Web options.
        const string json = """{"amount":{"kind":"number","value":"9007199254740993"}}""";
        var inputs = JsonSerializer.Deserialize<Dictionary<string, DecisionValue>>(json)!;
        Assert.Equal("number", inputs["amount"].Kind);
        Assert.Equal("9007199254740993", inputs["amount"].Value);
        var evaluated = new DecisionEngine().Evaluate(new DecisionEngine().Compile(Xml).Artifact!, inputs);
        Assert.Equal("Success", evaluated.Status);
        Assert.Equal("Manager", evaluated.Result.GetProperty("value").GetString());
        Assert.Equal(json, JsonSerializer.Serialize(inputs));
    }

    [Fact]
    public async Task Publication_freezes_latest_and_runtime_needs_no_decision_reader()
    {
        var fixture = new Fixture();
        var oldVersion = fixture.Version;
        var frozenNode = await fixture.PublishAsync();
        fixture.Version = fixture.Version with { Id = "version-2", Xml = Xml.Replace("Automatic", "New policy"), Artifact = fixture.Engine.Compile(Xml.Replace("Automatic", "New policy")).Artifact! };
        fixture.Executable = fixture.CreateExecutable(frozenNode);
        // A fresh activity/provider reads the persisted executable; it has no IDecisionReader or Design dependency.
        var activity = fixture.CreateActivity();
        var result = Assert.IsAssignableFrom<IActivityCompletionTransition>(await ((IActivity)activity).ExecuteAsync(fixture.Context));
        var evaluation = Assert.IsType<DecisionEvaluation>(result.Result);
        Assert.Equal("Success", evaluation.Status);
        Assert.Equal(oldVersion.Artifact.ContentHash, evaluation.ContentHash);
        Assert.Contains("Automatic", evaluation.Result.GetRawText());
        Assert.Equal("automatic", Assert.Single(evaluation.Trace, row => row.Selected).RuleId);
        Assert.Equal(fixture.Engine.Evaluate(oldVersion.Artifact, activity.Inputs).Result.GetRawText(), evaluation.Result.GetRawText());
    }

    [Fact]
    public async Task Specific_version_is_not_replaced_by_latest()
    {
        var fixture = new Fixture { Selection = "asset@version-1" };
        var node = await fixture.PublishAsync();
        Assert.Equal("version-1", DecisionWorkflowPin.Parse(node.Metadata[DecisionWorkflowPin.MetadataKey]).VersionId);
    }

    [Fact]
    public async Task Current_execution_permission_is_rechecked_on_retry()
    {
        var fixture = new Fixture();
        fixture.Executable = fixture.CreateExecutable(await fixture.PublishAsync());
        var activity = fixture.CreateActivity();
        await ((IActivity)activity).ExecuteAsync(fixture.Context);
        fixture.Permission = CurrentSubjectPermissionResult.Denied("revoked");
        var exception = await Assert.ThrowsAsync<DecisionInvocationException>(async () => await ((IActivity)fixture.CreateActivity()).ExecuteAsync(fixture.Context));
        Assert.Equal("forbidden", exception.Code);
        Assert.Equal("execution-subject", fixture.LastSubject);
    }

    [Fact]
    public async Task Unsupported_authority_does_not_fall_back_to_audit_initiator()
    {
        var fixture = new Fixture { Permission = CurrentSubjectPermissionResult.Unsupported("provider") };
        fixture.Executable = fixture.CreateExecutable(await fixture.PublishAsync());
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await ((IActivity)fixture.CreateActivity()).ExecuteAsync(fixture.Context));
        Assert.Equal("execution-subject", fixture.LastSubject);
    }

    [Theory]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-selection")]
    [InlineData("corrupt-artifact")]
    public async Task Frozen_metadata_cannot_be_silently_retargeted(string kind)
    {
        var fixture = new Fixture();
        var node = await fixture.PublishAsync();
        var pin = DecisionWorkflowPin.Parse(node.Metadata[DecisionWorkflowPin.MetadataKey]);
        pin = kind switch
        {
            "wrong-tenant" => pin with { TenantId = "other-tenant" },
            "wrong-selection" => pin with { Selection = "other@latest" },
            _ => pin with { Artifact = new(pin.Artifact.DecisionId, pin.Artifact.SourceHash, "wrong-hash", pin.Artifact.ProfileId, pin.Artifact.SemanticsId, pin.Artifact.ExecutableBytes) }
        };
        fixture.Executable = fixture.CreateExecutable(fixture.CreateNode(new Dictionary<string, string> { [DecisionWorkflowPin.MetadataKey] = pin.Serialize() }));
        if (kind == "corrupt-artifact")
        {
            var result = Assert.IsAssignableFrom<IActivityCompletionTransition>(await ((IActivity)fixture.CreateActivity()).ExecuteAsync(fixture.Context));
            Assert.NotEqual("Success", Assert.IsType<DecisionEvaluation>(result.Result).Status);
            Assert.Equal("Refused", result.Outcome);
        }
        else await Assert.ThrowsAsync<DecisionInvocationException>(async () => await ((IActivity)fixture.CreateActivity()).ExecuteAsync(fixture.Context));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("decision-id")]
    [InlineData("source-hash")]
    public async Task Publication_refuses_corrupt_retained_artifacts_instead_of_recompiling_a_replacement(string corruption)
    {
        var fixture = new Fixture();
        var original = fixture.Version.Artifact;
        var bytes = original.ExecutableBytes;
        if (corruption == "bytes") bytes[^1] ^= 1;
        fixture.Version = fixture.Version with
        {
            Artifact = new(corruption == "decision-id" ? "other-decision" : original.DecisionId,
                corruption == "source-hash" ? new string('0', 64) : original.SourceHash,
                original.ContentHash, original.ProfileId, original.SemanticsId, bytes)
        };
        var exception = await Assert.ThrowsAsync<DecisionInvocationException>(async () => await fixture.PublishAsync());
        Assert.Equal("invalid-selection", exception.Code);
    }

    [Fact]
    public async Task Protected_policy_publication_fails_before_embedding_rule_source()
    {
        var fixture = new Fixture(); fixture.Version = fixture.Version with { IsSensitive = true };
        var exception = await Assert.ThrowsAsync<DecisionInvocationException>(async () => await fixture.PublishAsync());
        Assert.Equal("protected-binding-unsupported", exception.Code);
    }

    [Fact]
    public async Task Missing_version_and_denied_source_never_embed_an_alternative()
    {
        var fixture = new Fixture { VersionAvailable = false };
        await Assert.ThrowsAsync<DecisionInvocationException>(async () => await fixture.PublishAsync());
        fixture.VersionAvailable = true; fixture.CanPublish = false;
        await Assert.ThrowsAsync<DecisionInvocationException>(async () => await fixture.PublishAsync());
    }

    [Fact]
    public void Malformed_pin_preserves_safe_domain_boundary()
    {
        var exception = Assert.Throws<DecisionInvocationException>(() => DecisionWorkflowPin.Parse("not JSON"));
        Assert.Equal("invalid-pin", exception.Code);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Theory]
    [InlineData(RuntimeInputBindingSource.WorkflowRequest, false)]
    [InlineData(RuntimeInputBindingSource.VariableRead, false)]
    [InlineData(RuntimeInputBindingSource.ActivityResult, false)]
    [InlineData(RuntimeInputBindingSource.WorkflowRequest, true)]
    [InlineData(RuntimeInputBindingSource.VariableRead, true)]
    [InlineData(RuntimeInputBindingSource.ActivityResult, true)]
    public async Task Materialized_dynamic_source_protection_refuses_plain_decision_results(RuntimeInputBindingSource source, bool requiresEncryption)
    {
        var fixture = new Fixture { InputSource = source, InputSourcePolicy = new(DurableValueLifecycle.Instance,
            DurableValueStorage.Inline, isSensitive: !requiresEncryption, requiresEncryption: requiresEncryption) };
        fixture.Executable = fixture.CreateExecutable(await fixture.PublishAsync());
        Assert.False(fixture.Executable.NodesById[fixture.Context.ExecutableNodeId].InputBindings[nameof(EvaluateDecision.Inputs)].EffectivePolicy.HidesValue());
        var exception = await Assert.ThrowsAsync<DecisionInvocationException>(async () => await ((IActivity)fixture.CreateActivity()).ExecuteAsync(fixture.Context));
        Assert.Equal("protected-binding-unsupported", exception.Code);
    }

    [Theory]
    [InlineData(RuntimeInputBindingSource.WorkflowRequest)]
    [InlineData(RuntimeInputBindingSource.VariableRead)]
    [InlineData(RuntimeInputBindingSource.ActivityResult)]
    public async Task Public_dynamic_source_inputs_still_produce_an_actual_decision_result(RuntimeInputBindingSource source)
    {
        var fixture = new Fixture { InputSource = source };
        fixture.Executable = fixture.CreateExecutable(await fixture.PublishAsync());
        var transition = Assert.IsAssignableFrom<IActivityCompletionTransition>(await ((IActivity)fixture.CreateActivity()).ExecuteAsync(fixture.Context));
        var result = Assert.IsType<DecisionEvaluation>(transition.Result);
        Assert.Equal("Done", transition.Outcome);
        Assert.Equal("Automatic", result.Result.GetProperty("value").GetString());
        Assert.Equal("automatic", Assert.Single(result.Trace, trace => trace.Selected).RuleId);
    }

    [Fact]
    public async Task Dynamically_protected_budget_cannot_disclose_control_value_through_a_plain_result()
    {
        var fixture = new Fixture { SensitiveBudget = true };
        fixture.Executable = fixture.CreateExecutable(await fixture.PublishAsync());
        Assert.False(fixture.Executable.NodesById[fixture.Context.ExecutableNodeId].InputBindings[nameof(EvaluateDecision.OperationBudget)].EffectivePolicy.HidesValue());
        var exception = await Assert.ThrowsAsync<DecisionInvocationException>(async () => await ((IActivity)fixture.CreateActivity()).ExecuteAsync(fixture.Context));
        Assert.Equal("protected-binding-unsupported", exception.Code);
    }

    [Theory]
    [InlineData("missing-state")]
    [InlineData("missing-snapshot")]
    [InlineData("snapshot-invocation")]
    [InlineData("snapshot-contract")]
    [InlineData("state-node")]
    [InlineData("state-contract")]
    [InlineData("missing-input")]
    [InlineData("withheld-input")]
    public async Task Unverifiable_invocation_input_protection_never_emits_a_result(string alteration)
    {
        var fixture = new Fixture { InvocationAlteration = alteration };
        fixture.Executable = fixture.CreateExecutable(await fixture.PublishAsync());
        await Assert.ThrowsAsync<DecisionInvocationException>(async () => await ((IActivity)fixture.CreateActivity()).ExecuteAsync(fixture.Context));
    }

    private sealed class Fixture
    {
        public DecisionEngine Engine { get; } = new();
        public string Selection { get; init; } = "asset@latest";
        public DecisionVersion Version { get; set; }
        public bool VersionAvailable { get; set; } = true;
        public bool CanPublish { get; set; } = true;
        public CurrentSubjectPermissionResult Permission { get; set; } = CurrentSubjectPermissionResult.Granted;
        public string? LastSubject { get; private set; }
        public RuntimeInputBindingSource InputSource { get; init; } = RuntimeInputBindingSource.WorkflowRequest;
        public ValueProtectionPolicy InputSourcePolicy { get; init; } = ValueProtectionPolicy.InstanceInline;
        public string? InvocationAlteration { get; init; }
        public bool SensitiveBudget { get; init; }
        public WorkflowExecutable? Executable { get; set; }
        public ActivityExecutionContext Context { get; } = new("workflow", "invocation", "attempt", "node", CancellationToken.None);
        private readonly WorkflowExecutableIdentity identity = new("executable", "definition", "definition-version", "1", "artifact-hash");
        private readonly Access access = new();

        public Fixture()
        {
            var compilation = Engine.Compile(Xml);
            Assert.True(compilation.Success, string.Join(',', compilation.Diagnostics.Select(x => x.Code)));
            Version = new("version-1", "asset", 1, "Approval", Xml, compilation.Artifact!.SourceHash, compilation.Artifact, false, DateTimeOffset.UtcNow);
        }

        public ExecutableNode CreateNode(IReadOnlyDictionary<string, string>? metadata = null)
        {
            var type = new ValueTypeDescriptor("System.String");
            var literal = ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(Selection), ValueProtectionPolicy.InstanceInline);
            var inputsType = new ValueTypeDescriptor("System.Object");
            var inputBinding = new RuntimeInputBinding(nameof(EvaluateDecision.Inputs), inputsType, ValueProtectionPolicy.InstanceInline, InputSource,
                workflowRequest: InputSource == RuntimeInputBindingSource.WorkflowRequest ? new("inputs") : null,
                variable: InputSource == RuntimeInputBindingSource.VariableRead ? new("inputs", "root") : null,
                activityResult: InputSource == RuntimeInputBindingSource.ActivityResult ? new("producer", "$", "root") : null);
            var payload = JsonSerializer.SerializeToElement(new { });
            var contract = new ActivityContract(EvaluateDecision.ActivityType, "1.0.0", "test.clr", payload,
                [new(nameof(EvaluateDecision.Decision), "Decision", type, true, false, false, null, ActivityValuePolicy.Default),
                 new(nameof(EvaluateDecision.Inputs), "Inputs", inputsType, false, false, false, null, ActivityValuePolicy.Default),
                 new(nameof(EvaluateDecision.OperationBudget), "Budget", new("System.Int32"), false, false, false, null, ActivityValuePolicy.Default)],
                new(new("System.Object"), false, ActivityValuePolicy.Default, []), ["Done", "NoMatch", "Refused"],
                new("test.clr", EvaluateDecision.ActivityType), SideEffectProfile.ReplaySafe);
            return new("node", "node", EvaluateDecision.ActivityType, "1.0.0", "test.clr", payload,
                new Dictionary<string, RuntimeInputBinding>
                {
                    [nameof(EvaluateDecision.Decision)] = new(nameof(EvaluateDecision.Decision), type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal, literal),
                    [nameof(EvaluateDecision.Inputs)] = inputBinding,
                    [nameof(EvaluateDecision.OperationBudget)] = new(nameof(EvaluateDecision.OperationBudget), new("System.Int32"), ValueProtectionPolicy.InstanceInline,
                        RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new("budget"))
                }, metadata ?? new Dictionary<string, string>(), activityContract: contract);
        }

        public WorkflowExecutable CreateExecutable(ExecutableNode node) => new(identity, node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow,
            new Dictionary<string, string>(), new IncidentStrategyReference("Fault", "1"));

        public async Task<ExecutableNode> PublishAsync()
        {
            var reader = Port<IDecisionReader>((name, _) => name switch
            {
                nameof(IDecisionReader.FindAssetAsync) => ValueTask.FromResult<DecisionAsset?>(new("asset", "Approval", null, "draft", Xml, Version.SourceHash, false, Version.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)),
                nameof(IDecisionReader.FindVersionAsync) => ValueTask.FromResult(VersionAvailable ? Version : null),
                _ => throw new NotSupportedException(name)
            });
            var gate = Port<IDecisionPublicationAuthorizer>((_, _) => CanPublish ? ValueTask.CompletedTask : ValueTask.FromException(new DecisionInvocationException("forbidden", "Denied")));
            var source = new DecisionPinSource(reader, Engine, gate, access);
            var request = new WorkflowExecutableCompileRequest("definition-version", WorkflowExecutableReferenceScope.Published,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "test", TenantId: "tenant");
            var contribution = await source.GetContributionAsync(new(request, null!, CreateNode()));
            var claim = Assert.Single(contribution.NodeMetadata);
            return CreateNode(new Dictionary<string, string> { [claim.Key] = claim.Value });
        }

        public EvaluateDecision CreateActivity()
        {
            var state = new WorkflowExecutionState("workflow", identity, WorkflowExecutionStatus.Running, null,
                DateTimeOffset.UtcNow, null, null, null, null, null, "tenant", new Dictionary<string, string>())
            { Partition = new("tenant"), Authority = new("runtime-api", "audit-initiator", new Dictionary<string, string>
                { ["elsa.identity.subject-kind"] = "persisted-user", ["elsa.identity.subject-id"] = "execution-subject" }) };
            var workflows = Port<IWorkflowExecutionStateStore>((name, _) => name == "FindAsync" ? ValueTask.FromResult<WorkflowExecutionState?>(state) : throw new NotSupportedException(name));
            var executables = Port<IWorkflowExecutableStore>((name, _) => name == "FindAsync" ? ValueTask.FromResult(Executable) : throw new NotSupportedException(name));
            var subject = Port<ICurrentSubjectPermissionAuthorizer>((_, args) =>
            {
                LastSubject = ((CurrentSubjectPermissionRequest)args![0]!).SubjectId;
                return ValueTask.FromResult(Permission);
            });
            var node = Executable!.NodesById[Context.ExecutableNodeId];
            var contract = node.ActivityContract!;
            var inputsType = node.InputBindings[nameof(EvaluateDecision.Inputs)].TargetType;
            var effectivePolicy = ValuePolicyCombiner.Combine(node.InputBindings[nameof(EvaluateDecision.Inputs)].EffectivePolicy, InputSourcePolicy, "Decision inputs");
            var values = new Dictionary<string, ValueEnvelope>
            {
                [nameof(EvaluateDecision.Inputs)] = ValueEnvelope.Inline(inputsType, JsonSerializer.SerializeToElement(new Dictionary<string, DecisionValue> { ["amount"] = new("number", "1000") }), effectivePolicy),
                [nameof(EvaluateDecision.Decision)] = node.InputBindings[nameof(EvaluateDecision.Decision)].Literal!,
                [nameof(EvaluateDecision.OperationBudget)] = ValueEnvelope.Inline(new("System.Int32"), JsonSerializer.SerializeToElement(SensitiveBudget ? 1 : 1000000),
                    new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: SensitiveBudget))
            };
            if (InvocationAlteration == "missing-input") values.Clear();
            if (InvocationAlteration == "withheld-input") values[nameof(EvaluateDecision.Inputs)] = ValueEnvelope.Withheld(inputsType, WithheldValue.PolicyRequiresEncryption(), ValueProtectionPolicy.InstanceInline);
            var snapshot = new ActivityInputSnapshot(InvocationAlteration == "snapshot-invocation" ? "other-invocation" : Context.InvocationId,
                InvocationAlteration == "snapshot-contract" ? "other-contract" : contract.SchemaFingerprint, "binding-fingerprint", values, DateTimeOffset.UtcNow);
            var execution = new ActivityExecution(Context.InvocationId, Context.WorkflowExecutionId,
                InvocationAlteration == "state-node" ? "other-node" : Context.ExecutableNodeId, "node", EvaluateDecision.ActivityType, "1.0.0");
            var invocation = new ActivityExecutionState(execution, ActivityExecutionStatus.Running, null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                null, null, null, null, null, ActivitySchedulingProvenance.Empty, null, [], [], 0, 0, new Dictionary<string, string>(),
                ContractIdentity: new(contract.ActivityTypeKey, contract.ContractVersion, InvocationAlteration == "state-contract" ? "other-contract" : contract.SchemaFingerprint),
                InputSnapshot: InvocationAlteration == "missing-snapshot" ? null : snapshot);
            var activities = Port<IActivityExecutionStateStore>((name, args) => name == nameof(IActivityExecutionStateStore.FindAsync)
                && (string)args![0]! == Context.WorkflowExecutionId && (string)args[1]! == Context.InvocationId
                ? ValueTask.FromResult(InvocationAlteration == "missing-state" ? null : invocation) : throw new NotSupportedException(name));
            return new(Engine, workflows, executables, access, new DecisionWorkflowAuthorizer(subject), activities)
            { Decision = Selection, Inputs = new() { ["amount"] = new("number", "1000") }, OperationBudget = SensitiveBudget ? 1 : 1000000 };
        }
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant")); }

    private static T Port<T>(Func<string, object?[]?, object?> route) where T : class
    {
        var value = DispatchProxy.Create<T, RouteProxy>();
        ((RouteProxy)(object)value).Route = route;
        return value;
    }

    public class RouteProxy : DispatchProxy
    {
        public Func<string, object?[]?, object?> Route { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Route(targetMethod!.Name, args);
    }
}
