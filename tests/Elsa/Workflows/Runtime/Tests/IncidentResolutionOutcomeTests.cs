using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class IncidentResolutionOutcomeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_RequiresProvenance()
    {
        Assert.Throws<ArgumentException>(() =>
            new IncidentResolutionOutcome("FaultWorkflow", Now, strategy: null, systemSource: null));
    }

    [Fact]
    public void Constructor_SnapshotsMetadata()
    {
        var metadata = new Dictionary<string, string> { ["phase"] = "Resolve" };
        var outcome = new IncidentResolutionOutcome(
            "FaultWorkflow",
            Now,
            new IncidentStrategyReference("Fault", "1"),
            "IncidentStrategyFailure",
            metadata);

        metadata["phase"] = "Execute";

        Assert.Equal("Resolve", outcome.Metadata["phase"]);
    }

    [Fact]
    public void Constructor_RejectsMetadataOutsideTheSafeBoundedShape()
    {
        var oversized = Enumerable.Range(0, IncidentStrategyMetadata.MaxEntries + 1)
            .ToDictionary(index => $"key-{index}", _ => "value");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IncidentResolutionOutcome(
                "FaultWorkflow",
                Now,
                new IncidentStrategyReference("Fault", "1"),
                systemSource: null,
                oversized));
        Assert.Throws<ArgumentException>(() =>
            new IncidentResolutionOutcome(
                "FaultWorkflow",
                Now,
                new IncidentStrategyReference("Fault", "1"),
                systemSource: null,
                new Dictionary<string, string> { ["unsafe"] = "line-one\nline-two" }));
    }

    [Fact]
    public void IncidentProjection_AllowsOnlyBoundedPolicySafeMetadata()
    {
        var projected = IncidentStrategyMetadata.ProjectIncident(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CausationKind] = "ActivityFault",
            [RuntimeMetadataKeys.FaultMessage] = "secret message",
            [RuntimeMetadataKeys.ScopedVariableValues] = "secret variables"
        });

        Assert.Equal("ActivityFault", projected[RuntimeMetadataKeys.CausationKind]);
        Assert.DoesNotContain(RuntimeMetadataKeys.FaultMessage, projected);
        Assert.DoesNotContain(RuntimeMetadataKeys.ScopedVariableValues, projected);
    }

    [Fact]
    public void IncidentState_RequiresOutcomeForTerminalStatus()
    {
        Assert.Throws<ArgumentException>(() =>
            new IncidentState(
                "incident-1",
                "workflow-1",
                "activity-1",
                "node-1",
                IncidentSeverity.Error,
                IncidentStatus.Resolved,
                null,
                "Failure",
                "Failed",
                Now,
                Now));
    }
}
