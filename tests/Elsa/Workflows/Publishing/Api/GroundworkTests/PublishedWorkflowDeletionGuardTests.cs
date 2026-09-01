using Elsa.Persistence.Groundwork.V2.Testing;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.GroundworkTests;

/// <summary>
/// The publish-side permanent-deletion preflight: a definition that is still live in the publishing
/// vertical (active slot publication or live Published source reference) must not be hard-deleted,
/// because that would strand the runtime executable and its Published reference.
/// </summary>
public sealed class PublishedWorkflowDeletionGuardTests
{
    private const string DefinitionId = "definition-1";
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryWorkflowActivationAuthority _slots = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _references = new();
    private readonly PublishedWorkflowDeletionGuard _guard;

    public PublishedWorkflowDeletionGuardTests() =>
        _guard = new(_slots, _references, TimeProvider.System);

    private Task ActivateSlot() =>
        _slots.TryActivateAsync(new WorkflowActivationSlotRequest(
            DefinitionId,
            "default",
            "publication-1",
            WorkflowActivationSource.Publishing,
            0,
            Now)).AsTask();

    private Task SaveReference(string sourceReferenceId = "reference-1", DateTimeOffset? deletedAt = null) =>
        _references.SaveAsync(new WorkflowExecutableSourceReference(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: "artifact-1",
            SourceKind: WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            SourceId: "version-1",
            SourceVersion: "1.0.0",
            DefinitionId: DefinitionId,
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1.0.0",
            CreatedAt: Now,
            PublishedAt: Now,
            Scope: WorkflowExecutableReferenceScope.Published,
            DeletedAt: deletedAt)).AsTask();

    [Fact]
    public async Task Vetoes_while_a_publication_slot_holds_an_active_publication()
    {
        await ActivateSlot();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _guard.EnsureCanDeleteAsync(DefinitionId));

        Assert.Contains("active publication", exception.Message);
        Assert.Contains(DefinitionId, exception.Message);
    }

    [Fact]
    public async Task Vetoes_while_a_live_published_source_reference_exists_even_without_a_slot()
    {
        // The desync case observed in the wild: no slot authority left, but the runtime still serves the reference.
        await SaveReference();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _guard.EnsureCanDeleteAsync(DefinitionId));

        Assert.Contains("live published executable source reference", exception.Message);
    }

    [Fact]
    public async Task Allows_deletion_once_the_definition_is_unpublished()
    {
        await ActivateSlot();
        await SaveReference();

        await _slots.TryDeactivateAsync(
            DefinitionId,
            "default",
            WorkflowActivationSource.Publishing,
            1,
            Now);
        await _references.RetireAsync("reference-1", Now, "publication-unpublished");

        await _guard.EnsureCanDeleteAsync(DefinitionId);
    }

    [Fact]
    public async Task Ignores_live_references_of_other_definitions_and_retired_or_testrun_references()
    {
        await SaveReference(deletedAt: Now);
        await _references.SaveAsync(new WorkflowExecutableSourceReference(
            SourceReferenceId: "other-definition-reference",
            ArtifactId: "artifact-2",
            SourceKind: WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            SourceId: "version-2",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-2",
            DefinitionVersionId: "version-2",
            ArtifactVersion: "1.0.0",
            CreatedAt: Now,
            PublishedAt: Now,
            Scope: WorkflowExecutableReferenceScope.Published));

        await _guard.EnsureCanDeleteAsync(DefinitionId);
    }

    /// <summary>
    /// The end-to-end invariant: the Groundwork permanent-delete command refuses a definition with a live
    /// Published reference until it is unpublished, then deletes cleanly.
    /// </summary>
    [Fact]
    public async Task Permanent_delete_is_refused_until_the_definition_is_unpublished()
    {
        await using var persistence = GroundworkV2TestPersistence.Create(
            "memory",
            WorkflowsDesignStorageManifest.CreateUnits());
        var payloads = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var access = persistence.Access();
        var storage = new GroundworkDesignStorage(persistence.Sessions, access);
        var atomicWrite = new GroundworkDesignAtomicWrite(storage);
        var definitions = new GroundworkWorkflowDefinitionStore(persistence.Sessions, access);
        await new GroundworkMaterializeWorkflowDefinitionCommand(atomicWrite, new FixedClock(), access).Execute(
            new DesignOperationKey("published-delete-seed"),
            new WorkflowDefinition { Id = DefinitionId, Name = "Published", DeletedAt = Now });
        await ActivateSlot();
        await SaveReference();
        var delete = new GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
            storage,
            atomicWrite,
            payloads,
            definitions,
            new GroundworkWorkflowDefinitionDraftStore(storage, payloads, access),
            new GroundworkWorkflowDefinitionVersionStore(persistence.Sessions, definitions, payloads, access),
            new GroundworkWorkflowDefinitionVersionLayoutStore(persistence.Sessions, access),
            access,
            [_guard]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            delete.Execute(new DesignOperationKey("published-delete-blocked"), DefinitionId));
        Assert.NotNull(await definitions.FindByIdAsync(DefinitionId));

        await _slots.TryDeactivateAsync(
            DefinitionId,
            "default",
            WorkflowActivationSource.Publishing,
            1,
            Now);
        await _references.RetireAsync("reference-1", Now, "publication-unpublished");

        await delete.Execute(new DesignOperationKey("published-delete-complete"), DefinitionId);
        Assert.Null(await definitions.FindByIdAsync(DefinitionId));
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
