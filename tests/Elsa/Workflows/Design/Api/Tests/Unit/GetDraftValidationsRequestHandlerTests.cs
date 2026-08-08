using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// Pins the GET-validations read path (issue #927): the handler derives the FR-024 error set for the
/// current draft state through the validation gate and projects each <see cref="ValidationError"/>
/// onto the client-facing view, decomposing the R2 path into a node pointer + member reference.
/// </summary>
public sealed class GetDraftValidationsRequestHandlerTests
{
    private readonly WorkflowDefinitionDraft _draft = new()
    {
        Id = "draft-1",
        WorkflowDefinitionId = "definition-1",
        State = WorkflowDefinitionState.Empty
    };

    [Fact]
    public async Task Enumerates_derived_errors_and_decomposes_each_path_into_node_and_member()
    {
        var publisher = new ContributingPublisher(
            new ValidationError("n1/inputs/message", "InputOutput/MissingRequired", "Required input 'message' is missing."),
            new ValidationError(ValidationPaths.Workflow, "RootActivity/Missing", "Workflow has no root activity."));
        var handler = new GetDraftValidationsRequestHandler(new SingleDraftStore(_draft), publisher);

        var view = await handler.Handle(new GetDraftValidations("draft-1"), CancellationToken.None);

        Assert.Equal("draft-1", view.DraftId);
        Assert.Equal(2, view.Errors.Count);

        var required = Assert.Single(view.Errors, e => e.Code == "InputOutput/MissingRequired");
        Assert.Equal("Required input 'message' is missing.", required.Message);
        Assert.Equal("n1", required.NodeId);
        Assert.Equal("inputs/message", required.Member);
        Assert.Equal(DraftValidationErrorView.ErrorSeverity, required.Severity);

        var rootMissing = Assert.Single(view.Errors, e => e.Code == "RootActivity/Missing");
        Assert.Null(rootMissing.NodeId);
        Assert.Null(rootMissing.Member);
    }

    [Fact]
    public async Task Returns_an_empty_error_set_for_a_valid_draft()
    {
        var handler = new GetDraftValidationsRequestHandler(new SingleDraftStore(_draft), new ContributingPublisher());

        var view = await handler.Handle(new GetDraftValidations("draft-1"), CancellationToken.None);

        Assert.Empty(view.Errors);
    }

    [Fact]
    public async Task Throws_not_found_when_the_draft_does_not_exist()
    {
        var handler = new GetDraftValidationsRequestHandler(new SingleDraftStore(null), new ContributingPublisher());

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => handler.Handle(new GetDraftValidations("missing"), CancellationToken.None));
    }

    private sealed class ContributingPublisher(params ValidationError[] contributions) : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is DraftValidating validating)
                foreach (var error in contributions)
                    validating.Errors.Add(error);

            return Task.CompletedTask;
        }
    }

    private sealed class SingleDraftStore(WorkflowDefinitionDraft? draft) : IWorkflowDefinitionDraftStore
    {
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(draft);

        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(draft);

        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>(draft is null ? [] : [draft]);

        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DesignMetadataRecord>>([]);

        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(draft is null ? null : new DraftWithLayout(draft, []));
    }
}
