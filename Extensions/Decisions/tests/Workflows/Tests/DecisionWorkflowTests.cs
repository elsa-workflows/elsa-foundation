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
        Assert.Equal("automatic", Assert.Single(evaluation.Trace.Where(row => row.Selected)).RuleId);
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

    private sealed class Fixture
    {
        public DecisionEngine Engine { get; } = new();
        public string Selection { get; init; } = "asset@latest";
        public DecisionVersion Version { get; set; }
        public bool VersionAvailable { get; set; } = true;
        public bool CanPublish { get; set; } = true;
        public CurrentSubjectPermissionResult Permission { get; set; } = CurrentSubjectPermissionResult.Granted;
        public string? LastSubject { get; private set; }
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
            return new("node", "node", EvaluateDecision.ActivityType, "1.0.0", "test.clr", JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, RuntimeInputBinding> { [nameof(EvaluateDecision.Decision)] = new(nameof(EvaluateDecision.Decision), type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal, literal) },
                metadata ?? new Dictionary<string, string>());
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
            return new(Engine, workflows, executables, access, new DecisionWorkflowAuthorizer(subject))
            { Decision = Selection, Inputs = new() { ["amount"] = new("number", "1000") } };
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
