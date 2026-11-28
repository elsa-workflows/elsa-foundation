using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed record KernelRetentionStatus(string State, ActorRetentionWindows Effective, ActorRetentionWindows Proposed,
    long? Revision, long? AsOf, long? AffectedRuns, int ConfirmationThreshold, string? Confirmation);

/// <summary>Applies a configured policy only after the store has quoted its effect on existing runs.</summary>
internal sealed class KernelRetentionActivation(BoundScope tenant, ActorRetentionWindows proposed, string? configuredConfirmation)
{
    private const int ConfirmationThreshold = 100;
    private static readonly ActorRetentionWindows Indefinite = new();

    private KernelRetentionStatus _status = new("evaluating", Indefinite, proposed, null, null, null, ConfirmationThreshold, null);

    internal KernelRetentionStatus Status => Volatile.Read(ref _status);
    internal string Tenant => tenant.Tenant;

    internal TimeSpan? Window(ActorHeader header)
    {
        if (!StringComparer.Ordinal.Equals(header.Tenant, tenant.Tenant)) return null;

        var effective = Status.Effective;
        return header.Status switch
        {
            ActorStatus.Finished => effective.Finished,
            ActorStatus.Cancelled => effective.Cancelled,
            ActorStatus.Faulted => effective.Faulted,
            _ => null
        };
    }

    internal async ValueTask<bool> InitializeAsync(IActorRetentionPolicies policies, CancellationToken token)
    {
        var current = await policies.ReadRetentionPolicyAsync(tenant, token);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            // Commit policy extensions first so expired commands use the longer windows even while a shortening waits.
            var relaxed = SaferWindows(current.Windows, proposed);
            if (relaxed != current.Windows)
            {
                var extension = await policies.TryApplyRetentionPolicyAsync(tenant, current.Revision, relaxed, 0, cancellationToken: token);
                current = extension.Current;
                if (extension.Outcome == ActorRetentionPolicyChangeOutcome.Stale) continue;
                if (extension.Outcome == ActorRetentionPolicyChangeOutcome.OverLimit)
                    throw new InvalidOperationException("A retention policy extension cannot make runs eligible.");
                if (current.Windows == proposed)
                    return SetStatus(current, extension.Quote);
            }

            var result = await policies.TryApplyRetentionPolicyAsync(tenant, current.Revision, proposed, ConfirmationThreshold,
                configuredConfirmation, token);
            current = result.Current;
            if (result.Outcome == ActorRetentionPolicyChangeOutcome.Stale) continue;
            if (result.Outcome != ActorRetentionPolicyChangeOutcome.OverLimit)
                return SetStatus(current, result.Quote);

            var receipt = result.Confirmation ?? throw new InvalidOperationException("The store did not seal the retention quote.");
            return SetStatus(current, result.Quote, receipt);
        }

        throw new InvalidOperationException("The retention policy changed while the host evaluated its effect.");
    }

    internal async ValueTask<bool> RefreshAsync(IActorRetentionPolicies policies, CancellationToken token)
    {
        var current = await policies.ReadRetentionPolicyAsync(tenant, token);
        var status = Status;
        var enabled = current.Windows != Indefinite;
        if (current.Revision != status.Revision || current.Windows != status.Effective)
        {
            Volatile.Write(ref _status, new(enabled ? "active" : "disabled", current.Windows, proposed, current.Revision,
                null, null, ConfirmationThreshold, null));
        }
        return enabled;
    }

    internal async ValueTask<KernelRetentionStatus> ReadStatusAsync(IActorRetentionPolicies policies, CancellationToken token)
    {
        var current = await policies.ReadRetentionPolicyAsync(tenant, token);
        var status = Status;
        return current.Revision == status.Revision && current.Windows == status.Effective
            ? status
            : status with
            {
                State = status.State is "evaluating" or "unavailable" ? status.State : "policy-changed",
                Effective = current.Windows,
                Revision = current.Revision,
                AsOf = null,
                AffectedRuns = null,
                Confirmation = null
            };
    }

    internal void Unavailable() =>
        Volatile.Write(ref _status, Status with { State = "unavailable", AsOf = null, AffectedRuns = null, Confirmation = null });

    private static ActorRetentionWindows SaferWindows(ActorRetentionWindows previous, ActorRetentionWindows proposed) =>
        new(Longer(previous.Finished, proposed.Finished), Longer(previous.Cancelled, proposed.Cancelled),
            Longer(previous.Faulted, proposed.Faulted));

    private static TimeSpan? Longer(TimeSpan? previous, TimeSpan? proposed) =>
        previous is null || proposed is null ? null : previous >= proposed ? previous : proposed;

    private bool SetStatus(ActorRetentionPolicy current, ActorRetentionQuote quote, string? confirmation = null)
    {
        var enabled = current.Windows != Indefinite;
        var state = confirmation is not null ? "confirmation-required" : enabled ? "active" : "disabled";
        Volatile.Write(ref _status, new(state, current.Windows, proposed, current.Revision, quote.AsOf, quote.NewlyEligible,
            ConfirmationThreshold, confirmation));
        return enabled;
    }
}
