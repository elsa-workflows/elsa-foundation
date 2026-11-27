using System.Buffers.Binary;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed record KernelRetentionStatus(string State, ActorRetentionWindows Effective, ActorRetentionWindows Proposed,
    long? AsOf, long? AffectedRuns, int ConfirmationThreshold, string? Confirmation);

/// <summary>Applies a configured policy only after the store has quoted its effect on existing runs.</summary>
internal sealed class KernelRetentionActivation(BoundScope tenant, ActorRetentionWindows proposed, string? configuredConfirmation)
{
    private const int ConfirmationThreshold = 100;
    private static readonly ActorRetentionWindows Indefinite = new();

    private KernelRetentionStatus _status = new(proposed == Indefinite ? "disabled" : "evaluating",
        Indefinite, proposed, null, null, ConfirmationThreshold, null);

    internal bool Enabled => proposed != Indefinite;
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

    internal async ValueTask<bool> InitializeAsync(IActorReads reads, CancellationToken token)
    {
        if (reads is not IActorRetentionReads projection || reads is not IActorRetentionConfirmation protection)
            throw new InvalidOperationException("The installed actor store does not provide guarded retention quotes.");

        var previous = Indefinite;
        if (configuredConfirmation is { Length: > 0 } confirmation &&
            Change.Decode(await protection.UnprotectRetentionQuoteAsync(tenant, confirmation, token)) is { } approved &&
            (approved.Active || approved.Proposed == proposed))
        {
            var historical = await projection.ReadRetentionQuoteAsync(tenant, WorkflowActor.ActorType, approved.Previous,
                approved.Proposed, asOf: approved.AsOf, cancellationToken: token);
            if (historical.AsOf == approved.AsOf && historical.NewlyEligible <= approved.AffectedRuns)
            {
                previous = approved.Proposed;
                if (previous == proposed)
                {
                    var receipt = approved.Active
                        ? confirmation
                        : await protection.ProtectRetentionQuoteAsync(tenant, (approved with { Active = true }).Encode(), token);
                    SetStatus("active", previous, approved, receipt);
                    return true;
                }
            }
        }

        var quote = await projection.ReadRetentionQuoteAsync(tenant, WorkflowActor.ActorType, previous, proposed, cancellationToken: token);
        var active = quote.NewlyEligible <= ConfirmationThreshold;
        var change = new Change(previous, proposed, quote.AsOf, quote.NewlyEligible, active);
        var preview = await protection.ProtectRetentionQuoteAsync(tenant, change.Encode(), token);
        var effective = active ? proposed : SafePendingPolicy(previous, proposed);
        SetStatus(active ? "active" : "confirmation-required", effective, change, preview);
        return effective != Indefinite;
    }

    internal void Unavailable() =>
        Volatile.Write(ref _status, new("unavailable", Indefinite, proposed, null, null, ConfirmationThreshold, null));

    private static ActorRetentionWindows SafePendingPolicy(ActorRetentionWindows previous, ActorRetentionWindows proposed) =>
        new(Longer(previous.Finished, proposed.Finished), Longer(previous.Cancelled, proposed.Cancelled),
            Longer(previous.Faulted, proposed.Faulted));

    private static TimeSpan? Longer(TimeSpan? previous, TimeSpan? proposed) =>
        previous is null || proposed is null ? null : previous >= proposed ? previous : proposed;

    private void SetStatus(string state, ActorRetentionWindows effective, Change change, string confirmation) =>
        Volatile.Write(ref _status, new(state, effective, proposed, change.AsOf, change.AffectedRuns, ConfirmationThreshold, confirmation));

    private sealed record Change(ActorRetentionWindows Previous, ActorRetentionWindows Proposed, long AsOf, long AffectedRuns, bool Active)
    {
        private const int Size = 9 * sizeof(long);

        internal byte[] Encode()
        {
            long[] values = [
                AsOf, AffectedRuns, Active ? 1 : 0,
                Previous.Finished?.Ticks ?? -1, Previous.Cancelled?.Ticks ?? -1, Previous.Faulted?.Ticks ?? -1,
                Proposed.Finished?.Ticks ?? -1, Proposed.Cancelled?.Ticks ?? -1, Proposed.Faulted?.Ticks ?? -1
            ];
            var bytes = new byte[Size];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * sizeof(long)), values[i]);
            return bytes;
        }

        internal static Change? Decode(byte[]? bytes)
        {
            if (bytes?.Length != Size) return null;
            Span<long> values = stackalloc long[9];
            for (var i = 0; i < values.Length; i++)
                values[i] = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(i * sizeof(long)));

            if (values[0] <= 0 || values[1] < 0 || values[2] is not (0 or 1)) return null;
            foreach (var ticks in values[3..])
                if (ticks < -1) return null;

            return new(
                new(Window(values[3]), Window(values[4]), Window(values[5])),
                new(Window(values[6]), Window(values[7]), Window(values[8])),
                values[0], values[1], values[2] == 1);

            static TimeSpan? Window(long ticks) => ticks == -1 ? null : TimeSpan.FromTicks(ticks);
        }
    }
}
