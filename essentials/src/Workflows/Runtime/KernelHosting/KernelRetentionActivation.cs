using System.Buffers.Binary;
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

    internal async ValueTask<bool> InitializeAsync(IActorRetentionPolicies policies, IActorReads reads, CancellationToken token)
    {
        if (reads is not IActorRetentionConfirmation protection)
            throw new InvalidOperationException("The installed actor store does not provide guarded retention policies.");

        var approved = configuredConfirmation is { Length: > 0 } confirmation
            ? Change.Decode(await protection.UnprotectRetentionQuoteAsync(tenant, confirmation, token))
            : null;
        var current = await policies.ReadRetentionPolicyAsync(tenant, token);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            // Commit policy extensions first so expired commands use the longer windows even while a shortening waits.
            var relaxed = SaferWindows(current.Windows, proposed);
            if (relaxed != current.Windows)
            {
                var extension = await policies.TryApplyRetentionPolicyAsync(tenant, current.Revision, relaxed, 0, token);
                current = extension.Current;
                if (extension.Outcome == ActorRetentionPolicyChangeOutcome.Stale) continue;
                if (extension.Outcome == ActorRetentionPolicyChangeOutcome.OverLimit)
                    throw new InvalidOperationException("A retention policy extension cannot make runs eligible.");
                if (current.Windows == proposed)
                    return SetStatus(current, extension.Quote);
            }

            var reviewedCount = approved is { } approvedChange && approvedChange.Revision == current.Revision &&
                approvedChange.Proposed == proposed ? Math.Max(ConfirmationThreshold, approvedChange.AffectedRuns) : ConfirmationThreshold;
            var result = await policies.TryApplyRetentionPolicyAsync(tenant, current.Revision, proposed, reviewedCount, token);
            current = result.Current;
            if (result.Outcome == ActorRetentionPolicyChangeOutcome.Stale) continue;
            if (result.Outcome != ActorRetentionPolicyChangeOutcome.OverLimit)
                return SetStatus(current, result.Quote);

            var change = new Change(current.Revision, proposed, result.Quote.AsOf, result.Quote.NewlyEligible);
            var receipt = await protection.ProtectRetentionQuoteAsync(tenant, change.Encode(), token);
            return SetStatus(current, result.Quote, receipt);
        }

        throw new InvalidOperationException("The retention policy changed while the host evaluated its effect.");
    }

    internal void Unavailable() =>
        Volatile.Write(ref _status, new("unavailable", Indefinite, proposed, null, null, null, ConfirmationThreshold, null));

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

    private sealed record Change(long Revision, ActorRetentionWindows Proposed, long AsOf, long AffectedRuns)
    {
        private const int Size = 6 * sizeof(long);

        internal byte[] Encode()
        {
            long[] values = [
                Revision, AsOf, AffectedRuns, Proposed.Finished?.Ticks ?? -1, Proposed.Cancelled?.Ticks ?? -1, Proposed.Faulted?.Ticks ?? -1
            ];
            var bytes = new byte[Size];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * sizeof(long)), values[i]);
            return bytes;
        }

        internal static Change? Decode(byte[]? bytes)
        {
            if (bytes?.Length != Size) return null;
            Span<long> values = stackalloc long[6];
            for (var i = 0; i < values.Length; i++)
                values[i] = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(i * sizeof(long)));
            if (values[0] < 0 || values[1] <= 0 || values[2] < 0) return null;
            foreach (var ticks in values[3..])
                if (ticks < -1) return null;

            return new(values[0], new(Window(values[3]), Window(values[4]), Window(values[5])), values[1], values[2]);

            static TimeSpan? Window(long ticks) => ticks == -1 ? null : TimeSpan.FromTicks(ticks);
        }
    }
}
