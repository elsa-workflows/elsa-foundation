using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
// The enclosing namespace ends in `.UpdateDraftCommand`, which shadows the command type's
// simple name — alias the implementation type explicitly.
using CreateDraftCommandType = Elsa.Workflows.Design.Persistence.EFCore.Commands.CreateDraft;
using UpdateDraftCommandType = Elsa.Workflows.Design.Persistence.EFCore.Commands.UpdateDraft;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T027 (SC-006, SC-008) — the mutation pipeline is fully absorbed (US3). The former
/// <c>DraftMutationPipeline</c> type is gone entirely: <see cref="UpdateDraftCommand"/> owns its
/// mutation shell inline, and <see cref="CreateDraftCommandType"/> owns its creation shell inline
/// (FR-003, FR-007). No command takes a pipeline collaborator. The create/discard lifecycle still
/// round-trips unchanged (SC-008).
/// </summary>
public sealed class PipelineAbsorptionTests
{
    [Fact]
    public void DraftMutationPipeline_type_no_longer_exists()
    {
        var pipelineType = typeof(UpdateDraftCommandType).Assembly
            .GetType("Elsa.Workflows.Design.Persistence.EFCore.Services.DraftMutationPipeline");

        Assert.Null(pipelineType);
    }

    [Fact]
    public void No_Draft_command_takes_a_pipeline_collaborator()
    {
        // Every command owns its own shell; none injects a "*Pipeline" service.
        foreach (var commandType in new[] { typeof(UpdateDraftCommandType), typeof(CreateDraftCommandType) })
        {
            var ctor = Assert.Single(commandType.GetConstructors());
            Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType.Name.EndsWith("Pipeline"));
        }
    }

    [Fact]
    public async Task Lifecycle_create_then_discard_still_round_trips()
    {
        // SC-008: lifecycle commands are unaffected by the mutation-path absorption.
        using var host = WorkflowsDesignTestHost.Create();
        await host.EnsureDefinition("wf-1");

        string draftId;
        using (var scope = host.Services.CreateScope())
            draftId = await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute(WorkflowsDesignTestHost.TestOperationKey, "wf-1");

        using (var ctx = host.CreateContext())
            Assert.True(await ctx.WorkflowDefinitionDrafts.AnyAsync(d => d.Id == draftId));

        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IDiscardDraftCommand>().Execute(WorkflowsDesignTestHost.TestOperationKey, draftId);

        using var verifyCtx = host.CreateContext();
        Assert.False(await verifyCtx.WorkflowDefinitionDrafts.AnyAsync(d => d.Id == draftId));
    }
}
