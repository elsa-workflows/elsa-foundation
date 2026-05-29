using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-013 + SC-022 + Unit C FR-023. End-to-end exercise of the delete-and-re-add validation
/// lifecycle: introducing a forbidden condition results in the persisted sibling carrying
/// the error; removing the condition causes the next mutation to clear it (the sibling is
/// rewritten wholesale, not appended to).
/// </summary>
/// <remarks>
/// Uses the actual <see cref="OrphanActivityValidator"/> wired into the
/// <c>CapturingDomainEventSender.OnSend</c> hook — this exercises the validator's real logic
/// end-to-end against the pipeline's persistence flow, rather than re-testing the sibling
/// wholesale-rewrite mechanism (which <c>ValidationSiblingPersistenceTests</c> already covers
/// with a stub error).
/// </remarks>
public sealed class ValidationLifecycleTests
{
    [Fact]
    public async Task Orphan_activity_lifecycle_round_trips_through_sibling()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // Wire the real OrphanActivityValidator into the capturing sender's hook so every
        // OnDraftValidating dispatch runs the production validator code against the snapshot.
        var validator = new OrphanActivityValidator();
        host.DomainEventSender.OnSend = evt =>
        {
            if (evt is OnDraftValidating validating)
                validator.Handle(validating, CancellationToken.None).GetAwaiter().GetResult();
        };

        var draftId = await CreateDraft(host);

        // 1. Add an orphan activity (no connections, not IsStart) → validator emits an error.
        await AddActivity(host, draftId, Node("orphan"));

        await AssertSiblingErrors(host, draftId, expectedTypes: ["Graph/OrphanActivity"]);

        // 2. Add a start activity AND wire the orphan to it → orphan condition is gone.
        // (Each mutation triggers a fresh validation pass; the sibling is rewritten wholesale.)
        await AddActivity(host, draftId, Node("start", isStart: true));
        await AddConnection(host, draftId, "start", "orphan");

        await AssertSiblingErrors(host, draftId, expectedTypes: []);
    }

    private static async Task<string> CreateDraft(WorkflowsDesignTestHost host)
    {
        await host.EnsureDefinition("wf-1");
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute("wf-1");
    }

    private static async Task AddActivity(WorkflowsDesignTestHost host, string draftId, ActivityNode activity)
    {
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAddActivityToDraftCommand>().Execute(draftId, activity);
    }

    private static async Task AddConnection(WorkflowsDesignTestHost host, string draftId, string sourceNodeId, string targetNodeId)
    {
        var connection = new ActivityConnection(
            new ActivityPortConnection(sourceNodeId, "out"),
            new ActivityPortConnection(targetNodeId, "in"));

        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAddConnectionToDraftCommand>().Execute(draftId, connection);
    }

    private static async Task AssertSiblingErrors(WorkflowsDesignTestHost host, string draftId, string[] expectedTypes)
    {
        using var ctx = host.CreateContext();
        var sibling = await ctx.WorkflowDefinitionDraftValidations
            .FirstAsync(v => v.WorkflowDefinitionDraftId == draftId);

        var actualTypes = sibling.Errors.Select(e => e.Type).ToArray();

        Assert.Equal(expectedTypes.Length, actualTypes.Length);
        foreach (var expected in expectedTypes)
            Assert.Contains(expected, actualTypes);
    }

    private static ActivityNode Node(string nodeId, bool isStart = false) => new(
        NodeId: nodeId,
        ActivityVersionId: "av-1",
        Inputs: [],
        Outputs: [],
        IsContainer: false,
        IsStart: isStart,
        IsTerminal: false,
        ChildActivities: []
    );
}
