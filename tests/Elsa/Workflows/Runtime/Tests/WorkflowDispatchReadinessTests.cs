using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowDispatchReadinessTests
{
    [Fact]
    public void DistributedReady_IsAdditiveWithoutRenumberingExistingGuarantees()
    {
        Assert.Equal(0, (int)WorkflowDispatchReadinessGuarantee.ProcessLocal);
        Assert.Equal(1, (int)WorkflowDispatchReadinessGuarantee.DurableReady);
        Assert.Equal(2, (int)WorkflowDispatchReadinessGuarantee.Unsafe);
        Assert.Equal(3, (int)WorkflowDispatchReadinessGuarantee.DistributedReady);
    }

    [Fact]
    public async Task CompleteProcessLocalComposition_IsReportedHonestlyWithoutClaimingRestartSafety()
    {
        var report = await NewAssessor(WorkflowDispatchDurabilityLevel.ProcessLocal).AssessAsync();

        Assert.Equal(WorkflowDispatchReadinessGuarantee.ProcessLocal, report.Guarantee);
        Assert.False(report.Ready);
        Assert.All(report.Components, item => Assert.Equal("process-local", item.ReasonCode));
    }

    [Fact]
    public async Task ProcessLocalInfrastructureWithResumptionPump_RemainsProcessLocal()
    {
        var evidence = WorkflowDispatchDurabilityComponents.Required
            .Where(component => component != WorkflowDispatchDurabilityComponents.Resumption)
            .Select(component => new WorkflowDispatchDurabilityEvidence(component, WorkflowDispatchDurabilityLevel.ProcessLocal))
            .Append(new WorkflowDispatchDurabilityEvidence(
                WorkflowDispatchDurabilityComponents.Resumption,
                WorkflowDispatchDurabilityLevel.Durable));

        var report = await new WorkflowDispatchReadinessAssessor(evidence).AssessAsync();

        Assert.Equal(WorkflowDispatchReadinessGuarantee.ProcessLocal, report.Guarantee);
        Assert.False(report.Ready);
    }

    [Fact]
    public async Task CompleteDurableCompositionWithoutDistribution_IsDurableReady()
    {
        var report = await NewAssessor(WorkflowDispatchDurabilityLevel.Durable).AssessAsync();

        Assert.Equal(WorkflowDispatchReadinessGuarantee.DurableReady, report.Guarantee);
        Assert.True(report.Ready);
        Assert.Empty(report.ReasonCodes);
        Assert.DoesNotContain(
            report.Components,
            component => component.Component == WorkflowDispatchDurabilityComponents.Distribution);
    }

    [Fact]
    public async Task DurableCompositionWithDurableDistribution_IsDistributedReady()
    {
        var report = await NewAssessor(
            WorkflowDispatchDurabilityLevel.Durable,
            WorkflowDispatchDurabilityLevel.ProcessLocal,
            WorkflowDispatchDurabilityLevel.Durable).AssessAsync();

        Assert.Equal(WorkflowDispatchReadinessGuarantee.DistributedReady, report.Guarantee);
        Assert.True(report.Ready);
        Assert.Empty(report.ReasonCodes);
        var distribution = Assert.Single(
            report.Components,
            component => component.Component == WorkflowDispatchDurabilityComponents.Distribution);
        Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, distribution.Level);
    }

    [Fact]
    public async Task DurableCompositionWithProcessLocalDistribution_IsUnsafe()
    {
        var report = await NewAssessor(
            WorkflowDispatchDurabilityLevel.Durable,
            WorkflowDispatchDurabilityLevel.ProcessLocal).AssessAsync();

        Assert.Equal(WorkflowDispatchReadinessGuarantee.Unsafe, report.Guarantee);
        Assert.False(report.Ready);
        Assert.Contains("process-local", report.ReasonCodes);
    }

    [Fact]
    public async Task ProcessLocalCompositionWithProcessLocalDistribution_RemainsProcessLocal()
    {
        var report = await NewAssessor(
            WorkflowDispatchDurabilityLevel.ProcessLocal,
            WorkflowDispatchDurabilityLevel.ProcessLocal).AssessAsync();

        Assert.Equal(WorkflowDispatchReadinessGuarantee.ProcessLocal, report.Guarantee);
        Assert.False(report.Ready);
    }

    [Fact]
    public async Task PartialDurableComposition_IsUnsafeAndUsesOnlyStableComponentCodes()
    {
        var evidence = WorkflowDispatchDurabilityComponents.Required
            .Where(component => component != WorkflowDispatchDurabilityComponents.Resumption)
            .Select(component => new WorkflowDispatchDurabilityEvidence(component, WorkflowDispatchDurabilityLevel.Durable));
        var report = await new WorkflowDispatchReadinessAssessor(evidence).AssessAsync();

        Assert.Equal(WorkflowDispatchReadinessGuarantee.Unsafe, report.Guarantee);
        Assert.False(report.Ready);
        Assert.Contains("missing-resumption", report.ReasonCodes);
        Assert.DoesNotContain(report.ReasonCodes, code => code.Contains("Elsa.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingInfrastructureWithResumptionPump_IsUnsafe()
    {
        var missingComponent = WorkflowDispatchDurabilityComponents.Checkpoint;
        var evidence = WorkflowDispatchDurabilityComponents.Required
            .Where(component => component != missingComponent)
            .Select(component => new WorkflowDispatchDurabilityEvidence(component, WorkflowDispatchDurabilityLevel.ProcessLocal));

        var report = await new WorkflowDispatchReadinessAssessor(evidence).AssessAsync();

        Assert.Equal(WorkflowDispatchReadinessGuarantee.Unsafe, report.Guarantee);
        Assert.False(report.Ready);
        Assert.Contains($"missing-{missingComponent}", report.ReasonCodes);
    }

    private static WorkflowDispatchReadinessAssessor NewAssessor(
        WorkflowDispatchDurabilityLevel level,
        params WorkflowDispatchDurabilityLevel[] distributionLevels)
    {
        var evidence = WorkflowDispatchDurabilityComponents.Required
            .Select(component => new WorkflowDispatchDurabilityEvidence(component, level))
            .Concat(distributionLevels.Select(distributionLevel => new WorkflowDispatchDurabilityEvidence(
                WorkflowDispatchDurabilityComponents.Distribution,
                distributionLevel)));

        return new WorkflowDispatchReadinessAssessor(evidence);
    }
}
