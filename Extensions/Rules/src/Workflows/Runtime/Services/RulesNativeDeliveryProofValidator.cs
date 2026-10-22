using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Workflows.Runtime.Services;

/// <summary>Rechecks a proposed delivery proof against the Rules row and a fresh native persistence scope.</summary>
internal sealed class RulesNativeDeliveryProofValidator(IPersistenceOperationScopeFactory scopes)
    : IRuleDeliveryProofValidator
{
    public async ValueTask<RuleDeliveryProofValidationResult> ValidateAsync(
        RuleInvocationEnvelope invocation,
        RuleTerminalEnvelope envelope,
        RuleDeliveryProof proof,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(proof);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryValidateRetainedEnvelope(invocation, envelope, proof, out var fingerprint))
            return Invalid;

        try
        {
            await using var scope = await scopes.CreateAsync(
                new PersistenceScope(invocation.Identity.Scope.PersistencePartition), cancellationToken);
            var inspector = scope.ServiceProvider.GetRequiredService<RulesNativeDeliveryInspector>();
            var inspection = await inspector.InspectAsync(invocation.Identity, fingerprint, envelope, cancellationToken);
            if (inspection.Proof is not { } observed || !ProofsEquivalent(proof, observed))
                return Invalid;
            return Equivalent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Provider, scope, and external-payload failures are not negative native evidence. The store must
            // retain the delivery and retry rather than treating an unavailable read as proof of mismatch.
            return Unavailable;
        }
    }

    private static bool TryValidateRetainedEnvelope(
        RuleInvocationEnvelope invocation,
        RuleTerminalEnvelope envelope,
        RuleDeliveryProof proof,
        out RuleInvocationFingerprint fingerprint)
    {
        fingerprint = null!;
        try
        {
            var destination = invocation.NativeDestination;
            if (destination is null || destination != envelope.Destination ||
                invocation.Identity.InvocationId != RulesNativeBoundary.RunId(invocation.Identity.Scope,
                    destination.WorkflowExecutionId, destination.ActivityExecutionId))
                return false;

            var invocationBytes = RuleRecordCodec.InvocationBytes(invocation);
            fingerprint = new(RuleRecordCodec.Hash(invocationBytes));
            var deliveryBytes = RuleRecordCodec.DeliveryBytes(envelope.DeliveryId, envelope.Destination, envelope.Result);
            if (RuleRecordCodec.Hash(deliveryBytes) != envelope.CanonicalPayloadSha256)
                return false;

            return Enum.IsDefined(proof.Kind) &&
                   proof.DestinationIdentity == RuleRecordCodec.DestinationIdentity(envelope.Destination) &&
                   proof.TerminalId == envelope.Result.TerminalId &&
                   proof.PayloadSha256 == envelope.CanonicalPayloadSha256;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                           NotSupportedException or JsonException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static bool ProofsEquivalent(RuleDeliveryProof proposed, RuleDeliveryProof observed)
    {
        var sameEvidenceKind = proposed.Kind == observed.Kind ||
                               (proposed.Kind is RuleDeliveryProofKind.Consumed or RuleDeliveryProofKind.AlreadyConsumed) &&
                               (observed.Kind is RuleDeliveryProofKind.Consumed or RuleDeliveryProofKind.AlreadyConsumed);
        return sameEvidenceKind &&
               proposed.DestinationIdentity == observed.DestinationIdentity &&
               proposed.TerminalId == observed.TerminalId &&
               proposed.PayloadSha256 == observed.PayloadSha256 &&
               proposed.ParentCommitOrTerminalId == observed.ParentCommitOrTerminalId;
    }

    private static RuleDeliveryProofValidationResult Equivalent { get; } =
        new(RuleDeliveryProofValidationDisposition.Equivalent);

    private static RuleDeliveryProofValidationResult Invalid { get; } =
        new(RuleDeliveryProofValidationDisposition.Invalid);

    private static RuleDeliveryProofValidationResult Unavailable { get; } =
        new(RuleDeliveryProofValidationDisposition.Unavailable);
}
