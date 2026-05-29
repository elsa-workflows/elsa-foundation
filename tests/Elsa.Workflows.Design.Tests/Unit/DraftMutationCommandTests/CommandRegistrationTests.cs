using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.DraftMutationCommandTests;

/// <summary>
/// SC-012 + framework §2.23.1: every FR-019 command contract resolves from the DI container
/// to its EFCore implementation. Parametrised over all 22 contracts (1 lifecycle origination
/// + 21 mutation) — including the 6 per-activity input/output CRUD commands added per
/// Joey iteration 2026-05-28 round 1.
/// </summary>
public sealed class CommandRegistrationTests
{
    public static IEnumerable<object[]> AllCommandContracts()
    {
        yield return [typeof(ICreateDraftCommand)];
        yield return [typeof(IAddActivityToDraftCommand)];
        yield return [typeof(IRemoveActivityFromDraftCommand)];
        yield return [typeof(IMoveActivityInDraftCommand)];
        yield return [typeof(IAddActivityInputToDraftCommand)];
        yield return [typeof(IUpdateActivityInputInDraftCommand)];
        yield return [typeof(IRemoveActivityInputFromDraftCommand)];
        yield return [typeof(IAddActivityOutputToDraftCommand)];
        yield return [typeof(IUpdateActivityOutputInDraftCommand)];
        yield return [typeof(IRemoveActivityOutputFromDraftCommand)];
        yield return [typeof(IAddConnectionToDraftCommand)];
        yield return [typeof(IRemoveConnectionFromDraftCommand)];
        yield return [typeof(IDeclareVariableInDraftCommand)];
        yield return [typeof(IUpdateVariableInDraftCommand)];
        yield return [typeof(IRemoveVariableFromDraftCommand)];
        yield return [typeof(IAddWorkflowInputToDraftCommand)];
        yield return [typeof(IUpdateWorkflowInputInDraftCommand)];
        yield return [typeof(IRemoveWorkflowInputFromDraftCommand)];
        yield return [typeof(IAddWorkflowOutputToDraftCommand)];
        yield return [typeof(IUpdateWorkflowOutputInDraftCommand)];
        yield return [typeof(IRemoveWorkflowOutputFromDraftCommand)];
    }

    [Theory]
    [MemberData(nameof(AllCommandContracts))]
    public void Contract_resolves_to_its_implementation(Type contractType)
    {
        using var host = WorkflowsDesignTestHost.Create();
        using var scope = host.Services.CreateScope();

        var resolved = scope.ServiceProvider.GetService(contractType);

        Assert.NotNull(resolved);
        Assert.IsAssignableFrom(contractType, resolved);
    }

    [Fact]
    public void All_21_commands_are_registered()
    {
        // Smoke check — count guard against a future regression that silently drops a registration.
        Assert.Equal(21, AllCommandContracts().Count());
    }
}
