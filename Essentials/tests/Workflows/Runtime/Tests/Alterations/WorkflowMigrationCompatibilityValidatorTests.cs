using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Alterations;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class WorkflowMigrationCompatibilityValidatorTests
{
    private readonly WorkflowMigrationCompatibilityValidator _validator = new();

    [Fact]
    public void Validate_AcceptsExactCompatibleTargetIncludingCompatibleDowngrade()
    {
        var source = Executable("source", "definition", "2", "2");
        var target = Executable("target", "definition", "1", "1", new Dictionary<string, string>
        {
            ["runtime.migration.compatible-from-schema:2"] = "true"
        });

        var report = _validator.Validate(Workflow(source.Identity), source, target, [], [], []);

        Assert.True(report.IsCompatible);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Validate_AcceptsSeparatelyMaterializedContractsWithTheSameVerifiedFingerprint()
    {
        var source = Executable("source", "definition", "1", "1", nodes: [Node("node", contract: Contract())]);
        var target = Executable("target", "definition", "1", "1", nodes: [Node("node", contract: Contract())]);

        var report = _validator.Validate(Workflow(source.Identity), source, target, [], [], []);

        Assert.True(report.IsCompatible);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Validate_ReportsOnlyStructuralFindingsForDefinitionNodeVariableAndBookmarkMismatch()
    {
        var source = Executable("source", "definition-a", "1", "1");
        var target = Executable("target", "definition-b", "1", "1", nodes: [Node("other")]);
        var bookmark = new BookmarkState("bookmark", "workflow", "activity", "node", "missing", "signal", "hash", null, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch, null);

        var report = _validator.Validate(Workflow(source.Identity), source, target, [], [bookmark], []);

        Assert.False(report.IsCompatible);
        Assert.Contains(report.Findings, finding => finding.Code == "DefinitionIdentityMismatch");
        Assert.Contains(report.Findings, finding => finding.Code == "RetainedNodeMissing" && finding.SubjectId == "node");
        Assert.Contains(report.Findings, finding => finding.Code == "BookmarkResumeTargetMismatch" && finding.SubjectId == "bookmark");
        Assert.DoesNotContain("definition-a", string.Join('|', report.Findings.Select(finding => $"{finding.Code}:{finding.SubjectId}")));
    }

    [Fact]
    public void Validate_RejectsRetainedActivityWhoseTargetNodeChangesType()
    {
        var source = Executable("source", "definition", "1", "1");
        var target = Executable("target", "definition", "1", "1", nodes: [Node("node", activityType: "changed")]);
        var activity = new ActivityExecutionState(
            new ActivityExecution("activity", "workflow", "node", "node", "original", "1"),
            ActivityExecutionStatus.Suspended, null, 0, DateTimeOffset.UnixEpoch, null, null, null, null, null, null,
            ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, null, null), null, [], [], 0, 0, new Dictionary<string, string>());

        var report = _validator.Validate(Workflow(source.Identity), source, target, [activity], [], []);

        Assert.False(report.IsCompatible);
        Assert.Contains(report.Findings, finding => finding.Code == "RetainedActivityTypeMismatch" && finding.SubjectId == "activity");
    }

    private static WorkflowExecutionState Workflow(WorkflowExecutableIdentity identity) => new(
        "workflow", identity, WorkflowExecutionStatus.Suspended, null, DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant", new Dictionary<string, string>())
    {
        PinnedSource = new WorkflowExecutableSourceProvenance("source", "published", "source", null, identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion, null, null)
    };

    private static WorkflowExecutable Executable(
        string artifactId,
        string definitionId,
        string schemaVersion,
        string requirementSchema,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyCollection<ExecutableNode>? nodes = null)
    {
        var node = (nodes ?? [Node("node")]).SingleOrDefault() ?? Node("node");
        return new WorkflowExecutable(
            new WorkflowExecutableIdentity(artifactId, definitionId, $"version-{artifactId}", "1.0.0", $"sha256:{artifactId}"),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget> { ["resume"] = new("resume", "node", "handler", new Dictionary<string, string>()) },
            DateTimeOffset.UnixEpoch,
            metadata ?? new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: [new Elsa.Activities.Runtime.Core.Models.RuntimeRequirement("consumer", requirementSchema)],
            storageDriverRequirements: null,
            IncidentStrategyBuiltIns.FaultReference,
            workflowVariables: null);
    }

    private static ExecutableNode Node(string id, string activityType = "original", ActivityContract? contract = null) => new(
        id, id, activityType, "1", "consumer", JsonSerializer.SerializeToElement(new { }),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract, descriptorSchemaVersion: "1");

    private static ActivityContract Contract() => new(
        "original",
        "1",
        "consumer",
        JsonSerializer.SerializeToElement(new { }),
        [],
        new ActivityResultContract(
            new ValueTypeDescriptor("Object"),
            false,
            ActivityValuePolicy.Default,
            []),
        [],
        new ActivityActivationRequirement("consumer", "original"));
}
