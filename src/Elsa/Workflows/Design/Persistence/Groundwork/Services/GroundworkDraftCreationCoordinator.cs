using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Owns the single Groundwork draft-origination path shared by fresh creation and clone-from-version.
/// Each caller retains its own public request fingerprint while source expansion runs only after an
/// exact-replay marker lookup misses.
/// </summary>
public sealed class GroundworkDraftCreationCoordinator(
    IIdentityGenerator identityGenerator,
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    GroundworkDesignAtomicWrite atomicWrite,
    IPayloadSerializer payloadSerializer,
    IInlineEventPublisher inlineEventPublisher,
    IDeferredEventPublisher deferredEventPublisher,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
{
    internal async Task<string> ExecuteAsync<TRequest>(
        DesignOperationKey operationKey,
        string operationKind,
        TRequest requestMaterial,
        Func<CancellationToken, Task<GroundworkDraftCreationInput>> resolveInput,
        CancellationToken cancellationToken)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(requestMaterial);
        ArgumentNullException.ThrowIfNull(resolveInput);

        var documents = new GroundworkWorkflowDefinitionDraftDocumentStore(
            store,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        GroundworkDraftCreationInput? input = null;
        WorkflowDefinitionDraft? draft = null;
        IDistributedSynchronizationHandle? draftLock = null;
        GroundworkDesignAtomicCommandResult<GroundworkDraftCreationResult> outcome;

        try
        {
            outcome = await GroundworkDesignAtomicCommand.ExecuteAsync(
                atomicWrite,
                operationKey,
                operationKind,
                requestMaterial,
                [WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind],
                async (context, token) =>
                {
                    var acceptedInput = input ?? throw new InvalidOperationException(
                        "Draft origination input was not resolved before staging.");
                    var acceptedDraft = draft ?? throw new InvalidOperationException(
                        "Draft identity was not allocated before staging.");
                    var errors = await inlineEventPublisher.DeriveValidationErrorsAsync(acceptedDraft, token);
                    GroundworkEntityTimestamps.StampAdded(acceptedDraft, clock.UtcNow);
                    await context.SaveAsync(
                        documents.ToSaveRequest(acceptedDraft, acceptedInput.Layout) with { ExpectedVersion = 0 },
                        token);
                    return new GroundworkDraftCreationResult(acceptedDraft, errors.ToArray());
                },
                GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                cancellationToken,
                beforeAttempt: async token =>
                {
                    input = await resolveInput(token)
                        ?? throw new InvalidOperationException("Draft origination input resolver returned null.");
                    ArgumentException.ThrowIfNullOrWhiteSpace(input.WorkflowDefinitionId);
                    accessContextAccessor.Current.EnsureTenantScope(input.TenantId);
                    draft = new WorkflowDefinitionDraft
                    {
                        Id = identityGenerator.Generate(),
                        TenantId = input.TenantId,
                        WorkflowDefinitionId = input.WorkflowDefinitionId,
                        SourceVersionId = input.SourceVersionId,
                        State = input.State
                    };
                    draftLock = await lockProvider.AcquireLockAsync(
                        WorkflowDesignPersistenceLockKeys.DraftKey(draft.Id),
                        null,
                        token);
                });
        }
        finally
        {
            if (draftLock is not null)
                await draftLock.DisposeAsync();
        }

        if (outcome.ShouldPublishPostCommitOutcome)
        {
            var committed = outcome.Value;
            await deferredEventPublisher.Publish(
                new OnDraftCreated(
                    committed.Draft.Id,
                    committed.Draft.WorkflowDefinitionId,
                    committed.Draft.SourceVersionId),
                CancellationToken.None);
            await deferredEventPublisher.Publish(
                new OnDraftValidated(committed.Draft, committed.Errors),
                CancellationToken.None);
        }

        return outcome.Value.Draft.Id;
    }
}

internal sealed record GroundworkDraftCreationInput(
    string WorkflowDefinitionId,
    WorkflowDefinitionState State,
    IReadOnlyCollection<DesignMetadataRecord> Layout,
    string? SourceVersionId,
    string? TenantId = null);

internal sealed record GroundworkDraftCreationResult(
    WorkflowDefinitionDraft Draft,
    IReadOnlyList<ValidationError> Errors);
