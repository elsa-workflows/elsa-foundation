using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Workflows.Runtime.Services;

/// <summary>Attempts one Rules-owned terminal delivery; only native durability proof permits acknowledgement.</summary>
internal sealed class RulesNativeDeliveryExecution(
    IRuleRunStore runs,
    IRuleRunReader reader,
    IPersistenceOperationScopeFactory scopes)
{
    public async ValueTask<bool> ExecuteOneAsync(string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        cancellationToken.ThrowIfCancellationRequested();
        var claimed = await runs.ClaimDeliveryAsync(new(owner, [RulesNativeBoundary.FormatVersion]), cancellationToken);
        if (claimed.Disposition is RuleDeliveryClaimDisposition.NoEligibleDelivery or RuleDeliveryClaimDisposition.Unavailable)
            return false;
        if (claimed.Disposition != RuleDeliveryClaimDisposition.Claimed || claimed.Lease is not { } lease || lease.Owner != owner)
            throw RulesNativeBoundary.Invalid();

        // An interrupted or unproven attempt leaves this lease and obligation untouched. Its expiry is
        // the store's existing retry path; there is no second mailbox or acknowledgement on dispatch.
        var read = await reader.ReadDeliveryAsync(lease, cancellationToken);
        if (read.Disposition is RuleDeliveryReadDisposition.LeaseLost or RuleDeliveryReadDisposition.NotFound or RuleDeliveryReadDisposition.Unavailable)
            return false;
        if (read.Disposition != RuleDeliveryReadDisposition.Ready || read.Envelope is not { } envelope ||
            envelope.DeliveryId != lease.DeliveryId)
            throw RulesNativeBoundary.Invalid();

        var found = await reader.FindAsync(lease.Identity, cancellationToken);
        if (found.Disposition is RuleRunReadDisposition.NotFound or RuleRunReadDisposition.Unavailable)
            return false;
        if (found.Disposition != RuleRunReadDisposition.Found || found.View is not { } view ||
            view.Identity != lease.Identity || view.TerminalId != envelope.Result.TerminalId ||
            view.TerminalKind != envelope.Result.Kind || view.Status != StatusFor(envelope.Result.Kind) ||
            envelope.CanonicalPayloadSha256 != RuleRecordCodec.Hash(
                RuleRecordCodec.DeliveryBytes(envelope.DeliveryId, envelope.Destination, envelope.Result)))
            throw RulesNativeBoundary.Invalid();

        var before = await InspectAsync(lease.Identity, view.Fingerprint, envelope, cancellationToken);
        var proof = before.Proof;
        if (proof is null)
        {
            if (before.Bookmark is null)
                return false;
            // Owned hosting tests freeze a real in-flight delivery claim here, after the terminal result exists and
            // before the parent is resumed, by naming a hold file in ELSA_HOLD_RULES_NATIVE_DELIVERY. Production hosts never set it.
            await HoldNativeDeliveryForTestAsync(Environment.GetEnvironmentVariable(NativeDeliveryHoldVariable), cancellationToken);
            await DispatchAsync(lease.Identity.Scope, envelope, cancellationToken);
            // A separate operation scope avoids retaining the dispatcher's in-flight execution view.
            proof = (await InspectAsync(lease.Identity, view.Fingerprint, envelope, cancellationToken)).Proof;
            if (proof is null)
                return false;
            if (proof.Kind == RuleDeliveryProofKind.AlreadyConsumed)
                proof = new(RuleDeliveryProofKind.Consumed, proof.DestinationIdentity, proof.TerminalId,
                    proof.PayloadSha256, proof.ParentCommitOrTerminalId);
        }

        // The store rechecks its lease and invokes the native validator against the authoritative
        // retained invocation before committing the acknowledgement. This earlier observation is not authority.
        var confirmation = await runs.AcknowledgeDeliveryAsync(lease, new(proof), cancellationToken);
        if (confirmation.Identity != lease.Identity)
            throw RulesNativeBoundary.Invalid();
        if (confirmation.Disposition is not (RuleTransitionDisposition.Acknowledged or RuleTransitionDisposition.Replay))
            return false;
        if (confirmation.TransitionId != envelope.DeliveryId || confirmation.TerminalId != envelope.Result.TerminalId)
            throw RulesNativeBoundary.Invalid();
        return true;
    }

    private async ValueTask<RulesNativeDeliveryInspection> InspectAsync(
        RuleRunIdentity identity,
        RuleInvocationFingerprint fingerprint,
        RuleTerminalEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using var scope = await scopes.CreateAsync(new(identity.Scope.PersistencePartition), cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<RulesNativeDeliveryInspector>()
            .InspectAsync(identity, fingerprint, envelope, cancellationToken);
    }

    private async ValueTask DispatchAsync(
        RuleScope ruleScope,
        RuleTerminalEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var address = envelope.Destination;
        using var terminal = JsonDocument.Parse(RuleRecordCodec.TerminalBytes(envelope.Result));
        var completion = new RunRulesCompletion(RulesNativeBoundary.FormatVersion, address,
            envelope.DeliveryId, terminal.RootElement.Clone(), InvalidCode: null);
        await using var scope = await scopes.CreateAsync(new(ruleScope.PersistencePartition), cancellationToken);
        var dispatched = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>()
            .DispatchAsync(new BookmarkResumeDispatchRequest(
                address.WorkflowExecutionId, address.StimulusType, address.StimulusHash,
                JsonSerializer.SerializeToElement(completion), envelope.DeliveryId,
                requestedBy: "rules.native-delivery", metadata: null,
                payloadType: RulesNativeBoundary.CompletionType, providerId: RulesNativeBoundary.ProviderId,
                bookmarkId: address.BookmarkId, testRunDelivery: null), cancellationToken: cancellationToken);
        if (dispatched.WorkflowExecutionId != address.WorkflowExecutionId)
            throw RulesNativeBoundary.Invalid();
        if (dispatched.Bookmark is { } bookmark)
            RulesNativeBoundary.EnsureBookmark(address, bookmark);
        if (dispatched.Resolution is { } resolution)
        {
            RulesNativeBoundary.EnsureBookmark(address, resolution.Bookmark);
            if (resolution.ExecutableNode.ExecutableNodeId != address.NodeId ||
                resolution.ResumeTarget.ResumeTargetId != address.ResumeTargetId ||
                resolution.ResumeTarget.ExecutableNodeId != address.NodeId)
                throw RulesNativeBoundary.Invalid();
        }
    }

    internal const string NativeDeliveryHoldVariable = "ELSA_HOLD_RULES_NATIVE_DELIVERY";
    internal const string HoldClaimMode = "hold-claim";

    /// <summary>
    /// When <paramref name="holdPath"/> names an existing file whose text is <c>hold-claim</c>, writes a sibling
    /// <c>.engaged</c> marker and then waits until cancelled, so the claimed delivery lease stays unacknowledged exactly
    /// as a crash between claim and dispatch would leave it. Any other path or content returns at once.
    /// </summary>
    internal static async ValueTask HoldNativeDeliveryForTestAsync(string? holdPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(holdPath) || !File.Exists(holdPath))
            return;
        string mode;
        try { mode = File.ReadAllText(holdPath).Trim(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("The configured Rules native delivery test hold could not be read.", error);
        }
        if (mode != HoldClaimMode)
            return;
        try { File.WriteAllText(holdPath + ".engaged", DateTimeOffset.UtcNow.ToString("O")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* The marker only helps the test observe the hold. */ }
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private static RuleRunStatus StatusFor(RuleTerminalKind kind) => kind switch
    {
        RuleTerminalKind.Succeeded => RuleRunStatus.Succeeded,
        RuleTerminalKind.Refused => RuleRunStatus.Refused,
        RuleTerminalKind.Faulted => RuleRunStatus.Faulted,
        RuleTerminalKind.Cancelled => RuleRunStatus.Cancelled,
        _ => throw RulesNativeBoundary.Invalid()
    };
}
