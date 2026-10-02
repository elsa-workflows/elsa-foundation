using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Resolves the withheld secret references in an activation's input snapshot into a transient copy of it, for
/// <see cref="ActivityActivator"/>. The request's snapshot is never changed, so nothing resolved is written back to
/// state, and nothing resolved is kept: every activation resolves again.
/// </summary>
/// <remarks>
/// Only <see cref="IRuntimeSecretResolver"/> is optional: a host that composes none parks the activity with the
/// missing-resolver activation failure instead of faulting it.
/// </remarks>
public sealed class ActivitySecretInputResolver(
    IWorkflowExecutionPartitionAccessor partitionAccessor,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IRuntimeValueConversionExecutor valueConversionExecutor,
    IRuntimeSecretResolver? secretResolver = null)
{
    private readonly IRuntimeSecretResolver? _secretResolver = secretResolver;

    /// <summary>
    /// Refuses, before the activity exists, every withheld input of <paramref name="snapshot"/> that this activation
    /// cannot resolve, rather than hydrating null and letting the activity run on a missing value: any withheld input
    /// of a strategy that does not hydrate, which would otherwise pass it on silently, and a value withheld because its
    /// policy requires encryption, which cannot be recovered. Both fault the activity. Only then is a secret reference
    /// in a host that composes no resolver refused, as a missing capability that parks the activity, so an input that
    /// no composition could resolve is never reported as one that composing the resolver would repair. An activator
    /// composed without <paramref name="inputResolver"/> resolves nothing, so it refuses as a host without a resolver.
    /// </summary>
    /// <returns>The secret inputs left to resolve once the activity is hydrated, or <see langword="null"/> when there are none.</returns>
    internal static PendingSecretInputs? Prepare(
        ActivitySecretInputResolver? inputResolver,
        ActivityInputSnapshot snapshot,
        bool hydratesInputs)
    {
        var secrets = new List<SecretInput>();
        foreach (var (key, envelope) in snapshot.Values
                     .Where(item => item.Value.Presence == ValuePresence.Withheld)
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!hydratesInputs || envelope.WithheldValue is not { Kind: WithheldValueKind.SecretReference, Secret: { } reference } withheld)
                throw SecretBindingDiagnostics.WithheldInputNotResolved(key);
            secrets.Add(new(key, envelope, reference, withheld.ConversionPlan));
        }

        if (secrets.Count == 0)
            return null;
        if (inputResolver?._secretResolver is not { } resolver)
            throw new RuntimeSecretResolverNotFoundException(secrets[0].Key);

        return new(inputResolver, resolver, secrets);
    }

    /// <summary>
    /// The tenant to resolve under is the partition the execution runs under, which is the scope its own rows are stored
    /// in. When the instance records a tenant, it must be that partition: a disagreement refuses with
    /// <see cref="RuntimeSecretResolutionException.TenantMismatch"/> before any secret is read, rather than reading
    /// another tenant's secret. An instance the partition cannot read is refused as well, and a global or across-scope
    /// context has no partition, which the accessor refuses.
    /// </summary>
    private async ValueTask<string> ReadExecutingTenantAsync(
        string workflowExecutionId,
        string referenceName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var partition = partitionAccessor.Current.Value;
        var instance = await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow execution '{workflowExecutionId}' is not found in the partition it runs under, so its secret inputs are not resolved.");
        if (instance.TenantId is { } instanceTenantId && !StringComparer.Ordinal.Equals(instanceTenantId, partition))
            throw new RuntimeSecretResolutionException(referenceName, RuntimeSecretResolutionException.TenantMismatch, isRetryable: false);

        return partition;
    }

    /// <summary>
    /// Resolves one reference. A resolver that throws anything but a cancellation has broken its contract; what it threw
    /// is dropped rather than wrapped, because its message may carry the value or store-private detail, and the
    /// activation reports <see cref="RuntimeSecretResolutionException.ResolverFailed"/> instead.
    /// </summary>
    private static async ValueTask<string> ResolveAsync(
        IRuntimeSecretResolver resolver,
        string tenantId,
        RuntimeSecretReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeSecretResolution resolution;
        try
        {
            resolution = await resolver.ResolveAsync(new RuntimeSecretResolutionRequest(tenantId, reference), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RuntimeSecretResolutionException(reference.Name, RuntimeSecretResolutionException.ResolverFailed, isRetryable: false);
        }

        // A resolver that reports a failure because the activation was canceled has not found the secret unusable.
        cancellationToken.ThrowIfCancellationRequested();
        if (!resolution.Succeeded)
            throw new RuntimeSecretResolutionException(reference.Name, resolution.FailureCode!, resolution.IsRetryable);

        return resolution.Value!;
    }

    /// <summary>
    /// Converts the resolved text with the plan pinned at publish, from a fresh inline envelope: the withheld envelope
    /// itself has no value to convert. A failure, and an envelope that carries no plan, is reported as
    /// <see cref="RuntimeSecretResolutionException.ConversionFailed"/>; the conversion's own exception is dropped
    /// rather than wrapped, because its message may describe the value it rejected. Publish pins only plans from text
    /// that cannot fail on a string, so only an artifact that skipped publish reaches the failure.
    /// </summary>
    private ValueEnvelope ConvertResolvedSecret(SecretInput secret, string value)
    {
        var plan = secret.ConversionPlan
            ?? throw new RuntimeSecretResolutionException(secret.Reference.Name, RuntimeSecretResolutionException.ConversionFailed, isRetryable: false);
        try
        {
            return valueConversionExecutor.Convert(
                ValueEnvelope.Inline(plan.SourceType, JsonSerializer.SerializeToElement(value), secret.Envelope.Policy),
                plan);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RuntimeSecretResolutionException(secret.Reference.Name, RuntimeSecretResolutionException.ConversionFailed, isRetryable: false);
        }
    }

    /// <summary>One withheld secret input, as <see cref="Prepare"/> accepted it.</summary>
    internal sealed record SecretInput(string Key, ValueEnvelope Envelope, RuntimeSecretReference Reference, ValueConversionPlan? ConversionPlan);

    /// <summary>
    /// The secret inputs <see cref="Prepare"/> accepted, with the resolver that resolves them. Created only once every
    /// withheld input is a resolvable secret reference and a resolver is composed.
    /// </summary>
    internal sealed class PendingSecretInputs(
        ActivitySecretInputResolver owner,
        IRuntimeSecretResolver resolver,
        IReadOnlyList<SecretInput> secrets)
    {
        /// <summary>
        /// Returns a transient copy of <paramref name="snapshot"/> whose secret inputs hold their resolved, converted
        /// values. <paramref name="snapshot"/> itself is not changed.
        /// </summary>
        public async ValueTask<ActivityInputSnapshot> ResolveAsync(
            string workflowExecutionId,
            ActivityInputSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            var tenantId = await owner.ReadExecutingTenantAsync(workflowExecutionId, secrets[0].Reference.Name, cancellationToken);
            var values = new Dictionary<string, ValueEnvelope>(snapshot.Values, StringComparer.Ordinal);
            foreach (var secret in secrets)
                values[secret.Key] = owner.ConvertResolvedSecret(secret, await ActivitySecretInputResolver.ResolveAsync(resolver, tenantId, secret.Reference, cancellationToken));

            return ActivityActivator.WithValues(snapshot, values);
        }
    }
}
