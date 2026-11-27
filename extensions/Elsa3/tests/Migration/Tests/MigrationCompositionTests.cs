using CShells.Lifecycle;
using Elsa.Tasks.Core;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Elsa3.Migration.Tests.Support;
using Elsa3.Migration.Tooling;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Migration.Tests;

/// <summary>Spec 1237 T009/T026: the migration host's composition, built in process for one run.</summary>
[Collection(nameof(CompositionCollection))]
public sealed class MigrationCompositionTests
{
    [Fact]
    public async Task Inventory_composes_the_host_and_the_addition_list_with_the_import_run_in_the_shell()
    {
        using var host = new TestHostFolder();

        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Inventory, host.Wiring, CancellationToken.None);

        Assert.Null(refusal);
        await using (composition!)
        {
            await using var scope = composition.Shell.BeginScope();
            Assert.NotNull(scope.ServiceProvider.GetService<Elsa3ImportRun>());
            Assert.Contains("Elsa3MigrationImportRun", composition.Facts.Features);
            Assert.Contains("JsonWorkflowReconciliation", composition.Facts.Features);
            Assert.True(composition.Facts.PersistenceOverridden);
        }
    }

    [Fact]
    public async Task A_listed_feature_no_project_provides_refuses_with_composition_failed_naming_it()
    {
        using var host = new TestHostFolder();
        host.AddToAdditionList("NoSuchFeature1237");

        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Inventory, host.Wiring, CancellationToken.None);

        Assert.Null(composition);
        Assert.Equal(MigrationRefusal.CompositionFailed, refusal?.Reason);
        Assert.Contains("NoSuchFeature1237", refusal!.Sentence);
    }
}

/// <summary>Compositions run one at a time: each holds a shared in-memory database and starts the shell's startup tasks.</summary>
[CollectionDefinition(nameof(CompositionCollection), DisableParallelization = true)]
public sealed class CompositionCollection;
