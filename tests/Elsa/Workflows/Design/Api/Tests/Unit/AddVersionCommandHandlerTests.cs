using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Versions.Add;
using AddVersionEndpoint = Elsa.Workflows.Design.Api.Endpoints.Versions.Add.Endpoint;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class AddVersionCommandHandlerTests
{
    [Fact]
    public async Task Forwards_the_caller_operation_key_and_authored_state()
    {
        var addCommand = new RecordingAddCommand("persisted-version", "3.0.0");
        var handler = new AddVersionEndpoint(addCommand, new StubVersionStore());

        await handler.HandleAsync(
            new AddVersion("publish-request-42", "definition-1", new WorkflowDefinitionStateView()),
            CancellationToken.None);

        Assert.Equal(new DesignOperationKey("publish-request-42"), addCommand.OperationKey);
        Assert.Equal("definition-1", addCommand.DefinitionId);
        Assert.NotNull(addCommand.State);
    }

    [Fact]
    public async Task Reads_the_authoritative_version_returned_by_persistence()
    {
        var versionStore = new StubVersionStore();
        var handler = new AddVersionEndpoint(
            new RecordingAddCommand("first-committed-id", "2.0.0"),
            versionStore);

        var result = await handler.HandleAsync(
            new AddVersion("replayed-request", "definition-1", new WorkflowDefinitionStateView()),
            CancellationToken.None);

        Assert.Equal("first-committed-id", versionStore.LastRequestedVersionId);
        Assert.Equal("2.0.0", result.Version);
    }

    private sealed class RecordingAddCommand(string versionId, string version)
        : IAddWorkflowDefinitionVersionCommand
    {
        public DesignOperationKey? OperationKey { get; private set; }
        public string? DefinitionId { get; private set; }
        public WorkflowDefinitionState? State { get; private set; }

        public Task<WorkflowDefinitionVersionAdded> Execute(
            DesignOperationKey operationKey,
            string definitionId,
            WorkflowDefinitionState state,
            CancellationToken cancellationToken = default)
        {
            OperationKey = operationKey;
            DefinitionId = definitionId;
            State = state;
            return Task.FromResult(new WorkflowDefinitionVersionAdded(definitionId, versionId, version));
        }
    }

    private sealed class StubVersionStore : IWorkflowDefinitionVersionStore
    {
        public string? LastRequestedVersionId { get; private set; }

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(
            string versionId,
            CancellationToken cancellationToken = default)
        {
            LastRequestedVersionId = versionId;
            return Task.FromResult(new WorkflowDefinitionVersion("definition-1", "2.0.0")
            {
                Id = versionId,
                State = WorkflowDefinitionState.Empty,
                Definition = new WorkflowDefinition { Id = "definition-1", Name = "Definition" }
            });
        }

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            string definitionId,
            string semVerSortKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
