using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Add;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// Root-cause regression coverage for elsa-foundation#951: commit "Harden Groundwork design persistence
/// commands" made <c>OperationKey</c> a required non-empty positional parameter, so a create request that
/// does not carry one (every request from the Studio create dialog) threw
/// <see cref="ArgumentException"/> ("Value cannot be null. (Parameter 'value')") when the handler built a
/// <see cref="DesignOperationKey"/>, surfacing as an opaque 400. Creation must succeed with a
/// server-generated key when none is supplied, and preserve a supplied key for retry de-duplication.
/// </summary>
public sealed class AddDefinitionOperationKeyTests
{
    private readonly RecordingAddWorkflowDefinitionCommand _persistence = new();
    private readonly Endpoint _handler;

    public AddDefinitionOperationKeyTests()
    {
        var identities = new SequentialIdentityGenerator();
        _handler = new Endpoint(
            new WorkflowDefinitionFactory(identities),
            new WorkflowDefinitionDraftFactory(identities),
            _persistence);
    }

    [Fact]
    public async Task Creation_without_an_operation_key_succeeds_with_a_generated_key()
    {
        await _handler.HandleAsync(
            new AddDefinition(OperationKey: null, "WF", Description: null),
            CancellationToken.None);

        Assert.NotNull(_persistence.OperationKey);
        Assert.False(string.IsNullOrWhiteSpace(_persistence.OperationKey!.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Creation_with_a_blank_operation_key_succeeds_with_a_generated_key(string blank)
    {
        await _handler.HandleAsync(
            new AddDefinition(blank, "WF", Description: null),
            CancellationToken.None);

        Assert.NotNull(_persistence.OperationKey);
        Assert.False(string.IsNullOrWhiteSpace(_persistence.OperationKey!.Value));
    }

    [Fact]
    public async Task Creation_preserves_a_supplied_operation_key()
    {
        await _handler.HandleAsync(
            new AddDefinition("create-request-1", "WF", Description: null),
            CancellationToken.None);

        Assert.Equal(new DesignOperationKey("create-request-1"), _persistence.OperationKey);
    }

    private sealed class RecordingAddWorkflowDefinitionCommand : IAddWorkflowDefinitionCommand
    {
        public DesignOperationKey? OperationKey { get; private set; }

        public Task<WorkflowDefinitionCreated> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition workflowDefinition,
            WorkflowDefinitionDraft draft,
            CancellationToken cancellation) =>
            throw new InvalidOperationException("The API must use the layout-aware creation operation.");

        public Task<WorkflowDefinitionCreated> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition workflowDefinition,
            WorkflowDefinitionDraft draft,
            IReadOnlyCollection<DesignMetadataRecord> layout,
            CancellationToken cancellation)
        {
            OperationKey = operationKey;
            return Task.FromResult(new WorkflowDefinitionCreated(workflowDefinition.Id, draft.Id));
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"id-{++_next}";
    }
}
