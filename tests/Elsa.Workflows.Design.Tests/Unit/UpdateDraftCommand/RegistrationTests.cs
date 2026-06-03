using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T024 (SC-001, G27) — the collapsed command surface. The single
/// <see cref="IUpdateDraftCommand"/> resolves from DI; the 20 former granular mutation
/// contracts no longer exist on the public surface of
/// <c>Elsa.Workflows.Design.Persistence.Core.Contracts</c>; the 4 lifecycle contracts remain.
/// Supersedes the former <c>CommandRegistrationTests</c>.
/// </summary>
public sealed class RegistrationTests
{
    [Fact]
    public void UpdateDraftCommand_resolves_to_its_implementation()
    {
        using var host = WorkflowsDesignTestHost.Create();
        using var scope = host.Services.CreateScope();

        var resolved = scope.ServiceProvider.GetService<IUpdateDraftCommand>();

        Assert.NotNull(resolved);
        Assert.IsAssignableFrom<IUpdateDraftCommand>(resolved);
    }

    [Theory]
    [InlineData(nameof(ICreateDraftCommand))]
    [InlineData(nameof(ICloneDraftFromVersionCommand))]
    [InlineData(nameof(IDiscardDraftCommand))]
    [InlineData(nameof(IPromoteDraftToVersionCommand))]
    public void The_four_lifecycle_contracts_still_exist(string contractName)
    {
        var contracts = LifecycleAndMutationContractsAssembly();
        Assert.Contains(contracts, t => t.Name == contractName);
    }

    [Theory]
    [InlineData("IAddActivityToDraftCommand")]
    [InlineData("IRemoveActivityFromDraftCommand")]
    [InlineData("IMoveActivityInDraftCommand")]
    [InlineData("IAddActivityInputToDraftCommand")]
    [InlineData("IUpdateActivityInputInDraftCommand")]
    [InlineData("IRemoveActivityInputFromDraftCommand")]
    [InlineData("IAddActivityOutputToDraftCommand")]
    [InlineData("IUpdateActivityOutputInDraftCommand")]
    [InlineData("IRemoveActivityOutputFromDraftCommand")]
    [InlineData("IAddConnectionToDraftCommand")]
    [InlineData("IRemoveConnectionFromDraftCommand")]
    [InlineData("IDeclareVariableInDraftCommand")]
    [InlineData("IUpdateVariableInDraftCommand")]
    [InlineData("IRemoveVariableFromDraftCommand")]
    [InlineData("IAddWorkflowInputToDraftCommand")]
    [InlineData("IUpdateWorkflowInputInDraftCommand")]
    [InlineData("IRemoveWorkflowInputFromDraftCommand")]
    [InlineData("IAddWorkflowOutputToDraftCommand")]
    [InlineData("IUpdateWorkflowOutputInDraftCommand")]
    [InlineData("IRemoveWorkflowOutputFromDraftCommand")]
    public void The_twenty_granular_mutation_contracts_are_gone(string deletedContractName)
    {
        var contracts = LifecycleAndMutationContractsAssembly();
        Assert.DoesNotContain(contracts, t => t.Name == deletedContractName);
    }

    private static Type[] LifecycleAndMutationContractsAssembly() =>
        typeof(IUpdateDraftCommand).Assembly
            .GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "Elsa.Workflows.Design.Persistence.Core.Contracts")
            .ToArray();
}
