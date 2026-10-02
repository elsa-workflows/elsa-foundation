using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.CredentialLiteralTestSupport;

namespace Elsa.Workflows.Design.Tests.Unit.CredentialLiteral;

/// <summary>
/// Spec 188, FR-008 at Drafts/Promote, over the real EF store, in a host whose promotion command runs the in-lock
/// validation gate and in one composed without it (research R7): the endpoint admits the draft it reads, so a draft
/// holding a credential literal is refused by admission whatever the command's composition; and the command promotes
/// only the content the endpoint admitted, so a draft changed between admission and the promotion lock is refused with a
/// conflict and no version is written.
/// </summary>
public sealed class CredentialLiteralPromoteAdmissionTests
{
    /// <summary>Whether the host composes the inline event publisher, and with it the promotion command's in-lock gate.</summary>
    public static TheoryData<bool> Hosts => new() { true, false };

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_draft_holding_a_credential_literal_is_refused_by_admission_and_no_version_is_written(bool composeInlinePublisher)
    {
        await using var host = await DesignAdmissionTestHost.CreateAsync(composeInlinePublisher);
        var draftId = await SeedDraftAsync(host);
        await host.StoreDraftDirectlyAsync(draftId, CredentialBoundAs("Literal"));

        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() => host.PromoteAsync(draftId));

        Assert.Equal($"{NodeId}/inputs/{CredentialKey}", Assert.Single(refusal.Findings).Path);
        Assert.Equal(0, (await host.CountRowsAsync()).Versions);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_draft_without_a_credential_literal_is_promoted(bool composeInlinePublisher)
    {
        await using var host = await DesignAdmissionTestHost.CreateAsync(composeInlinePublisher);
        var draftId = await SeedDraftAsync(host);

        var version = await host.PromoteAsync(draftId);

        Assert.Equal(1, (await host.CountRowsAsync()).Versions);
        Assert.Equal(NodeId, version.State.RootActivity!.NodeId);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_draft_changed_between_admission_and_the_promotion_lock_is_refused_with_a_conflict(bool composeInlinePublisher)
    {
        var edit = new EditBetweenAdmissionAndLock();
        await using var host = await DesignAdmissionTestHost.CreateAsync(composeInlinePublisher, edit.Decorate);
        var draftId = await SeedDraftAsync(host);
        edit.Arm(draftId, CredentialBoundAs("Literal"));

        await Assert.ThrowsAsync<WorkflowDraftChangedException>(() => host.PromoteAsync(draftId));

        Assert.True(edit.Fired);
        Assert.Equal(0, (await host.CountRowsAsync()).Versions);
    }

    [Fact]
    public async Task In_the_standard_host_a_draft_with_an_uncataloged_node_is_refused_at_promote_with_a_conflict()
    {
        // The documented residual (contract, Known gaps): admission cannot judge the node, the in-lock gate's
        // UnknownActivityVersionValidator reports it, and the translator answers DraftHasValidationErrorsException with 409.
        await using var host = await DesignAdmissionTestHost.CreateAsync();
        var draftId = await SeedDraftAsync(host);
        await host.StoreDraftDirectlyAsync(draftId, State(UncatalogedActivityVersionId, Bind(CredentialKey, "Literal")));

        await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() => host.PromoteAsync(draftId));

        Assert.Equal(0, (await host.CountRowsAsync()).Versions);
    }

    [Fact]
    public async Task A_replay_after_an_admissible_edit_returns_the_original_version()
    {
        await using var host = await DesignAdmissionTestHost.CreateAsync();
        var draftId = await SeedDraftAsync(host);
        var first = await host.PromoteAsync(draftId, operationKey: "promote-once");
        await host.ReplaceDraftAsync(draftId, State(ActivityVersionId, Bind(CredentialKey, "Secret"), Bind(SensitiveKey, "Literal")));

        var replay = await host.PromoteAsync(draftId, operationKey: "promote-once");

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, (await host.CountRowsAsync()).Versions);
    }

    [Fact]
    public async Task A_replay_after_an_edit_that_stored_a_credential_literal_is_refused_by_admission()
    {
        await using var host = await DesignAdmissionTestHost.CreateAsync();
        var draftId = await SeedDraftAsync(host);
        await host.PromoteAsync(draftId, operationKey: "promote-once");
        await host.StoreDraftDirectlyAsync(draftId, CredentialBoundAs("Literal"));

        await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() => host.PromoteAsync(draftId, operationKey: "promote-once"));
    }

    [Fact]
    public async Task A_replay_after_the_draft_was_discarded_returns_the_original_version()
    {
        await using var host = await DesignAdmissionTestHost.CreateAsync();
        var draftId = await SeedDraftAsync(host);
        var first = await host.PromoteAsync(draftId, operationKey: "promote-once");
        await host.InScopeAsync(async services =>
        {
            await services.GetRequiredService<IDiscardDraftCommand>().Execute(DesignOperationKey.CreateOrGenerate(null), draftId);
            return true;
        });

        var replay = await host.PromoteAsync(draftId, operationKey: "promote-once");

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, (await host.CountRowsAsync()).Versions);
    }

    [Fact]
    public async Task A_first_promotion_of_a_missing_draft_is_not_found()
    {
        await using var host = await DesignAdmissionTestHost.CreateAsync();

        await Assert.ThrowsAsync<EntityNotFoundException>(() => host.PromoteAsync("missing-draft", operationKey: "promote-once"));

        Assert.Equal(0, (await host.CountRowsAsync()).Versions);
    }

    private static async Task<string> SeedDraftAsync(DesignAdmissionTestHost host) =>
        (await host.AddDefinitionAsync(CredentialBoundAs("Secret"))).Draft!.Id;

    /// <summary>
    /// Replaces the draft store with one that, on the first read of the armed draft and before returning what it read,
    /// stores other content into that draft from another scope: a concurrent Drafts/Replace landing after promote's
    /// admission and before its promotion lock. The caller still receives the content it read. Every read returns what
    /// the database holds at that moment, as a store without an identity map does, so a second read by the caller would
    /// see the later write (the EF store's tracking query would hand back the first, stale entity instead).
    /// </summary>
    private sealed class EditBetweenAdmissionAndLock
    {
        private (string DraftId, WorkflowDefinitionState State)? _armed;

        public bool Fired { get; private set; }

        public void Arm(string draftId, WorkflowDefinitionState state) => _armed = (draftId, state);

        public void Decorate(IServiceCollection services) =>
            services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionDraftStore>(provider => new EditingDraftStore(
                provider.GetRequiredService<EfWorkflowDefinitionDraftStore>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                this)));

        private async Task EditAfterReadAsync(string draftId, IServiceScopeFactory scopes)
        {
            if (_armed is not { } armed || armed.DraftId != draftId)
                return;

            _armed = null;
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IUpdateDraftCommand>().Execute(
                DesignOperationKey.CreateOrGenerate(null),
                new UpdateDraftRequest(draftId, armed.State, []));
            Fired = true;
        }

        private sealed class EditingDraftStore(EfWorkflowDefinitionDraftStore inner, IServiceScopeFactory scopes, EditBetweenAdmissionAndLock edit)
            : IWorkflowDefinitionDraftStore
        {
            public async Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
            {
                WorkflowDefinitionDraft? draft;
                await using (var read = scopes.CreateAsyncScope())
                    draft = await read.ServiceProvider.GetRequiredService<EfWorkflowDefinitionDraftStore>().FindByIdAsync(draftId, cancellationToken);
                await edit.EditAfterReadAsync(draftId, scopes);
                return draft;
            }

            public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
                inner.FindByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken);

            public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
                inner.ListByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken);

            public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
                inner.FindLayoutByDraftIdAsync(draftId, cancellationToken);

            public Task<IReadOnlyCollection<ActivityPresentationRecord>> FindActivityPresentationByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
                inner.FindActivityPresentationByDraftIdAsync(draftId, cancellationToken);

            public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
                inner.FindWithLayoutByIdAsync(draftId, cancellationToken);
        }
    }
}
