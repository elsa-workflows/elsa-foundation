using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Persistence.Groundwork.DesignConformance.EFCore.Tests;

public sealed class EfCoreOracleEvidenceTests(ITestOutputHelper output)
{
    private const string WorkflowDraftHash = "8614e1ffb739e65f08a47c60da7c33891fff5f88c32ef5d88eed7b9fd5800369";
    private const string WorkflowVersionHash = "3fcb29139ffcac2c0fc3d5450d4e2682d3b504b86f31326b16b17f4712ceb9d4";
    private const string ActivityHash = "8bdeb707ea537e6357a8327b2d7404deb23af9e370c65a03f2e2883b182bf3f1";

    [Fact]
    public async Task SQLite_oracle_emits_deterministic_canonical_behavior_hashes()
    {
        await using var fixture = await EfCoreDesignPersistenceContractFixture.CreateAsync();
        await fixture.ValidateReadinessAsync();
        string versionId;

        using (var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA))
        {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IAddActivityDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.ActivityDefinition(),
                DesignPersistenceFixtureData.ActivityVersion(),
                CancellationToken.None);
            await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.WorkflowDefinition(),
                DesignPersistenceFixtureData.WorkflowDraft(state: DesignPersistenceFixtureData.WorkflowState()),
                DesignPersistenceFixtureData.WorkflowDraftLayout(),
                CancellationToken.None);
            versionId = await services.GetRequiredService<IPromoteDraftToVersionCommand>()
                .Execute(DesignPersistenceFixtureData.WorkflowDraftId);
        }

        await fixture.RestartAsync();

        using var readScope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var read = readScope.ServiceProvider;
        var definition = await read.GetRequiredService<IWorkflowDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.WorkflowDefinitionId);
        var draft = await read.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindWithLayoutByIdAsync(DesignPersistenceFixtureData.WorkflowDraftId) ?? throw new InvalidOperationException("The oracle did not persist its draft.");
        var version = await read.GetRequiredService<IWorkflowDefinitionVersionStore>()
            .GetAsync(versionId);
        var layout = await read.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>()
            .FindByVersionIdAsync(versionId) ?? throw new InvalidOperationException("The oracle did not persist its version layout.");
        var activityDefinition = await read.GetRequiredService<IActivityDefinitionStore>()
            .GetAsync(DesignPersistenceFixtureData.ActivityDefinitionId);
        var activityVersion = await read.GetRequiredService<IActivityDefinitionVersionStore>()
            .GetWithDefinitionAsync(DesignPersistenceFixtureData.ActivityVersionId);

        var hashes = new OracleHashes(
            WorkflowDesignContractSuite.CanonicalDraftResultHash(definition, draft.Draft, draft.Layout),
            WorkflowDesignContractSuite.CanonicalVersionResultHash(version, layout),
            ActivityDesignContractSuite.CanonicalResultHash(activityDefinition, activityVersion, activityVersion.Definition));

        output.WriteLine($"workflow-draft={hashes.WorkflowDraft}");
        output.WriteLine($"workflow-version={hashes.WorkflowVersion}");
        output.WriteLine($"activity={hashes.Activity}");
        Assert.Equal(WorkflowDraftHash, hashes.WorkflowDraft);
        Assert.Equal(WorkflowVersionHash, hashes.WorkflowVersion);
        Assert.Equal(ActivityHash, hashes.Activity);
        Assert.All(hashes.Values, value => Assert.Matches("^[0-9a-f]{64}$", value));
    }

    private sealed record OracleHashes(string WorkflowDraft, string WorkflowVersion, string Activity)
    {
        public IReadOnlyList<string> Values => [WorkflowDraft, WorkflowVersion, Activity];
    }
}
