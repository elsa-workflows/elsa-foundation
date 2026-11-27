using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore : IRuntimePreparedStartHandoff
{
    public async ValueTask<RuntimeCommittedRecipient?> CommitPreparedStartAsync(RuntimeDeliverySourceContext source, string recipientId,
        CancellationToken cancellationToken = default)
    {
        RequirePreparedStarts();
        var item = await ReadSourceAsync(source, cancellationToken);
        var recipient = item.SubscriptionDelivery!.Recipients.SingleOrDefault(candidate => candidate.RecipientId == recipientId) ??
            throw new InvalidDataException("The prepared dispatch recipient was not accepted.");
        var progress = Progress(item);
        if (progress.Phase == RuntimeRecipientPhase.CancelledBeforeDispatch) return null;
        var seal = progress.Seal ?? CreatePreparedStartQualificationSeal(item, recipient);
        var committed = progress.Seal is null ? await CommitDispatchAsync(source, seal, cancellationToken) : item;
        return Progress(committed).Phase == RuntimeRecipientPhase.CancelledBeforeDispatch ? null :
            ReadPreparedStart(committed, new(seal.SourceOutboxItemId, seal.SourceFingerprint,
                seal.SelectionFingerprint, source.Scope, recipientId, seal.Fingerprint)).Committed;

        RuntimeRecipientProgress Progress(RuntimePostCommitOutboxItem current) =>
            current.SubscriptionDelivery!.Progress.Single(state => state.RecipientId == recipientId);
    }

    internal static RuntimeDispatchSeal CreatePreparedStartQualificationSeal(RuntimePostCommitOutboxItem item,
        RuntimeSubscriptionRecipient recipient) =>
        new(RuntimeDispatchSeal.PreparedStartQualificationVersion, item.OutboxItemId, item.SourceFingerprint,
            item.SubscriptionDelivery!.SelectionFingerprint, recipient.RecipientId, recipient.StartIdentity, item.RecordedAt,
            RuntimeDispatchSeal.PreparedStartQualificationProtocol, 1, PreparedStartCommand(recipient));

    private static JsonElement PreparedStartCommand(RuntimeSubscriptionRecipient recipient) =>
        JsonSerializer.SerializeToElement(new { recipientFingerprint = recipient.Fingerprint });

    private void ValidatePreparedStartSeal(RuntimeCommittedRecipient committed, WorkflowExecutable? material)
    {
        RequirePreparedStarts();
        var recipient = committed.Recipient;
        var item = committed.Item;
        if (item.StandaloneIngress is not { } source ||
            (item.SubscriptionDelivery?.WaitDelivery is not null || PreparedStandaloneHttp.IsHttp(source)) &&
                !PreparedStandaloneHttp.HasVerifiedEmptyWaits(item) ||
            recipient.ModelKey != "native" || recipient.ModelProtocol is not null ||
            recipient.Partition.Value != recipient.StartIdentity.Scope.Value)
            throw new NotSupportedException("Prepared start qualification requires standalone Native dispatch without an ordinary wait cohort.");
        if (material is null || material.Identity != recipient.Material || material.InputContract is not { } contract ||
            RuntimeIngressAdmission.ComputeInputContractFingerprint(contract) != recipient.InputContractFingerprint ||
            !JsonElement.DeepEquals(committed.Seal.Command, PreparedStartCommand(recipient)))
            throw new InvalidDataException("The prepared start seal differs from its exact accepted material and recipient.");
    }

    public async ValueTask<(long Generation, bool Disarmed)?> ReadPreparedStartAttemptAsync(RuntimeCommittedRecipientAuthority authority,
        CancellationToken cancellationToken = default)
    {
        RequirePreparedStarts();
        var committed = await ReadCommittedRecipientAsync(authority, cancellationToken);
        var attempt = ReadPreparedStart(committed.Item, authority).Progress.PreparedStartAttempt;
        return attempt is null ? null : (attempt.Generation, attempt.Disarmed);
    }

    /// <summary>Binds the exact offer received from the configured owner before the protection callback returns.</summary>
    /// <remarks>The caller captures the prior generation before requesting preparation. Binding never substitutes a newer offer.</remarks>
    public async ValueTask<long?> TryBindPreparedStartAsync(RuntimeDeliverySourceContext source,
        RuntimeCommittedRecipientAuthority authority, long previousGeneration, string tenant, string actorReference, string issuance,
        CancellationToken cancellationToken = default)
    {
        RequirePreparedStarts();
        ArgumentOutOfRangeException.ThrowIfNegative(previousGeneration);
        var prepared = new RuntimePreparedStartIdentity(tenant, actorReference, issuance);
        var proposed = new RuntimePreparedStartAttempt(checked(previousGeneration + 1), prepared, disarmed: false);
        var saved = await MutateAsync(source, item =>
        {
            var (committed, progress) = ReadPreparedStart(item, authority);
            if (prepared.Tenant != (committed.Recipient.TenantId ?? PersistenceScope.DefaultValue))
                throw new InvalidDataException("The prepared offer differs from the accepted recipient tenant.");
            if (progress.Phase != RuntimeRecipientPhase.DispatchCommitted ||
                (progress.PreparedStartAttempt?.Generation ?? 0) != previousGeneration || !proposed.Follows(progress.PreparedStartAttempt))
                return item;
            return SavePreparedStart(item, progress.WithPreparedStartAttempt(proposed));
        }, cancellationToken);
        var actual = ReadPreparedStart(saved, authority).Progress;
        return actual.Phase == RuntimeRecipientPhase.DispatchCommitted && actual.PreparedStartAttempt == proposed ? proposed.Generation : null;
    }

    /// <summary>Reconciles only the recorded attempt and retains its exact positive actor receipt.</summary>
    public async ValueTask<RuntimePostCommitOutboxItem> ReconcilePreparedStartAsync(RuntimeDeliverySourceContext source,
        RuntimeCommittedRecipientAuthority authority, long generation,
        CancellationToken cancellationToken = default)
    {
        var observer = RequirePreparedStarts();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        var current = await ReadSourceAsync(source, cancellationToken);
        var (committed, progress) = ReadPreparedStart(current, authority);
        if (progress.Phase != RuntimeRecipientPhase.DispatchCommitted ||
            progress.PreparedStartAttempt is not { Disarmed: false } expected || expected.Generation != generation)
            return current;

        // Provider evidence is monotone. Observe outside the Runtime transaction, then recheck claim and attempt
        // before either saving the receipt or enabling a subsequent preparation.
        var proof = await observer.ObserveAsync(new(source.Scope, committed.Recipient.TenantId),
            expected.Identity.ActorReference, expected.Identity.Issuance, cancellationToken);
        if (proof == RuntimePreparedStartProof.Unresolved) return current;
        return await MutateAsync(source, item =>
        {
            var (actual, state) = ReadPreparedStart(item, authority);
            if (state.Phase != RuntimeRecipientPhase.DispatchCommitted || state.PreparedStartAttempt != expected)
                return item;
            return proof switch
            {
                RuntimePreparedStartProof.Accepted => SavePreparedStart(item, state.AcceptTarget(new(
                    actual.Recipient.RecipientId, actual.Seal.Fingerprint, actual.Recipient.StartIdentity, actual.Recipient.Material,
                    RuntimeTargetAcceptanceKind.PreparedActorBirth, expected.Identity.ActorReference, time.GetUtcNow()))),
                RuntimePreparedStartProof.Disarmed => SavePreparedStart(item, state.WithPreparedStartAttempt(
                    new(expected.Generation, expected.Identity, disarmed: true))),
                _ => throw new InvalidDataException("The selected Foundation owner returned an unknown prepared start proof.")
            };
        }, cancellationToken);
    }

    private IRuntimePreparedStartAttemptProof RequirePreparedStarts() =>
        preparedStarts ?? throw new NotSupportedException("Prepared start attempt reconciliation is available only in private qualification.");

    private static (RuntimeCommittedRecipient Committed, RuntimeRecipientProgress Progress) ReadPreparedStart(
        RuntimePostCommitOutboxItem item, RuntimeCommittedRecipientAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var committed = RuntimeIngressAdmissionValidation.Committed(item, authority);
        if (committed.Seal.Version != RuntimeDispatchSeal.PreparedStartQualificationVersion)
            throw new InvalidDataException("The accepted recipient has no prepared start seal.");
        return (committed, item.SubscriptionDelivery!.Progress.Single(progress => progress.RecipientId == authority.RecipientId));
    }

    private static RuntimePostCommitOutboxItem SavePreparedStart(RuntimePostCommitOutboxItem item, RuntimeRecipientProgress progress) =>
        item.WithSubscriptionDelivery(item.SubscriptionDelivery!.WithProgress(progress));
}
