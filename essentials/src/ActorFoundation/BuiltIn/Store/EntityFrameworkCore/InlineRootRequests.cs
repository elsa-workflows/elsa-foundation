using Kernel.EntityFramework;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Hosting;

/// <summary>The owner's observed outcome for a qualified inline root start.</summary>
public enum InlineStartDisposition { Volatile, Committed, Refused, Unavailable, Unknown }

/// <summary>A qualified inline start receipt. Only a committed start includes an actor reference.</summary>
public readonly record struct InlineStartOutcome(InlineStartDisposition Disposition,
    Contract.ActorRef? Actor = null, Contract.ActorStatus? Status = null)
{
    /// <summary>The admission limit that refused this start, when applicable.</summary>
    public Contract.RefusalReason? RefusalReason { get; init; }

    /// <summary>The retry interval for a start that could not enter the owner's queue.</summary>
    public TimeSpan? RetryAfter { get; init; }
}

/// <summary>Binds qualified inline starts to one installed owner, tenant, and host activation.</summary>
public sealed class InlineRootRequests(Contract.BoundScope scope, string actorType,
    Func<Contract.StartSpec, object, bool> validate, ProtectRootStartAsync protect)
{
    private readonly object _gate = new();
    private readonly Contract.BoundScope? _additionalScope;
    private readonly Func<Contract.ActorStatus, bool>? _allowAdditionalWriteFreeCompletion;
    private (EfStore Store, Worker Worker, Action<Kernel.Runtime.InstanceRef> Wake)? _owner;
    private bool _closed;
    private int _pendingSettlements;
    private TaskCompletionSource? _settlementsDrained;

    /// <summary>Binds the primary scope and a second host-owned scope to the same inline owner.</summary>
    /// <remarks>The installed format must explicitly support the additional scope. Configure it once with a distinct isolated tenant.</remarks>
    public InlineRootRequests(Contract.BoundScope scope, string actorType,
        Func<Contract.StartSpec, object, bool> validate, ProtectRootStartAsync protect,
        Contract.BoundScope additionalScope, Func<Contract.ActorStatus, bool> allowAdditionalWriteFreeCompletion)
        : this(scope, actorType, validate, protect)
    {
        ArgumentNullException.ThrowIfNull(additionalScope);
        ArgumentNullException.ThrowIfNull(allowAdditionalWriteFreeCompletion);
        if (!additionalScope.IsHostIsolated || additionalScope.Tenant == scope.Tenant)
            throw new ArgumentException("The additional inline scope must name a distinct host-isolated tenant.", nameof(additionalScope));
        _additionalScope = additionalScope;
        _allowAdditionalWriteFreeCompletion = allowAdditionalWriteFreeCompletion;
    }

    internal Task PrepareAsync(EfStore store, CancellationToken token)
    {
        if (store.AdmissionPressure is null) throw new InvalidOperationException("Inline starts require the owner's node admission pressure.");
        return store.EnableInlinePromotionAsync(scope,
            (spec, material) => spec.Model == actorType &&
                validate(new(spec.Model, spec.Definition, spec.Input, spec.Ephemeral, (Contract.History)spec.History), material),
            protect, TimeSpan.FromHours(1), token, _additionalScope, _allowAdditionalWriteFreeCompletion);
    }

    internal void Attach(EfStore store, Worker worker, Action<Kernel.Runtime.InstanceRef> wake)
    {
        lock (_gate)
        {
            if (_closed || _owner is not null) throw new InvalidOperationException("The local start edge cannot be attached twice.");
            _owner = (store, worker, wake);
        }
    }

    /// <summary>Stops new inline starts while the owner drains already admitted work.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _owner = null;
        }
    }

    /// <summary>Waits for accepted additional-scope starts to finish settling after admission closes.</summary>
    public Task DrainSettlementsAsync(CancellationToken token)
    {
        lock (_gate)
        {
            if (!_closed) throw new InvalidOperationException("Close inline admissions before draining settlements.");
            return _pendingSettlements == 0 ? Task.CompletedTask :
                (_settlementsDrained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(token);
        }
    }

    /// <summary>Offers an exact start and its trusted local material to this owner, then observes its outcome.</summary>
    public ValueTask<InlineStartOutcome> StartAsync(string operationId, Contract.StartSpec start, object material,
        CancellationToken token) => StartCoreAsync(scope, operationId, start, material, null, token);

    /// <summary>Offers an exact start and its trusted local material to the additional scope bound at composition.</summary>
    /// <remarks>Settlement of an accepted owner receipt continues when the caller stops waiting.</remarks>
    public async ValueTask<InlineStartOutcome> StartAdditionalAsync(string operationId, Contract.StartSpec start, object material,
        Func<InlineStartOutcome, ValueTask<InlineStartOutcome>> settle, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(settle);
        if (_additionalScope is { } additional) return await StartCoreAsync(additional, operationId, start, material, settle, token);
        var outcome = await settle(new(InlineStartDisposition.Refused));
        token.ThrowIfCancellationRequested();
        return outcome;
    }

    private async ValueTask<InlineStartOutcome> StartCoreAsync(Contract.BoundScope selectedScope, string operationId,
        Contract.StartSpec start, object material, Func<InlineStartOutcome, ValueTask<InlineStartOutcome>>? settle, CancellationToken token)
    {
        var (receipt, unqueued) = Offer(selectedScope, operationId, start, material, settle, token);
        if (receipt is null)
        {
            // Before the queue accepts work, a cancelled caller still has a definite no-actor outcome.
            var outcome = settle is null ? unqueued : await settle(unqueued);
            token.ThrowIfCancellationRequested();
            return outcome;
        }

        try { return await receipt.WaitAsync(RootPreparedStarts.OneWayBudget, token); }
        catch (TimeoutException)
        {
            token.ThrowIfCancellationRequested();
            // Queueing succeeded. The owner can settle later, so the caller cannot retry it as a shed request.
            return new(InlineStartDisposition.Unknown);
        }
    }

    private (Task<InlineStartOutcome>? Receipt, InlineStartOutcome Unqueued) Offer(Contract.BoundScope selectedScope,
        string operationId, Contract.StartSpec start, object material, Func<InlineStartOutcome, ValueTask<InlineStartOutcome>>? settle,
        CancellationToken token)
    {
        if (token.IsCancellationRequested) return (null, new(InlineStartDisposition.Unavailable));
        if (start.Type != actorType || EfStore.CreateInlineRootSpec(operationId, start) is not { } trusted || !validate(start, material))
            return (null, new(InlineStartDisposition.Refused));
        lock (_gate)
        {
            if (token.IsCancellationRequested || _owner is not { } owner) return (null, new(InlineStartDisposition.Unavailable));
            using var attempt = owner.Store.AdmissionPressure!.BeginInlineStart(selectedScope.Tenant);
            try { attempt.RequireInlineTenantRate(); }
            catch (RootAdmissionRefused refusal)
            {
                var admission = refusal.Admission;
                return (null, new(InlineStartDisposition.Unavailable)
                    { RefusalReason = admission.Reason, RetryAfter = admission.RetryAfter });
            }
            if (owner.Store.QueueInlineRoot(owner.Worker, operationId, trusted, material, owner.Wake, selectedScope) is not { } admitted)
                return (null, new(InlineStartDisposition.Unavailable));
            // Queued authored work spends its start permit even if it finishes without a checkpoint.
            attempt.Committed();
            var outcome = ReadOutcomeAsync(admitted.Reference, admitted.Outcome);
            if (settle is null) return (outcome, default);
            _pendingSettlements++;
            return (SettleAcceptedAsync(outcome, settle), default);
        }
    }

    private async Task<InlineStartOutcome> SettleAcceptedAsync(Task<InlineStartOutcome> outcome,
        Func<InlineStartOutcome, ValueTask<InlineStartOutcome>> settle)
    {
        try
        {
            // The owner can finish before Offer releases its gate. Start the persistence callback outside it.
            var result = await outcome.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            return await settle(result);
        }
        finally
        {
            lock (_gate)
                if (--_pendingSettlements == 0) _settlementsDrained?.TrySetResult();
        }
    }

    private static async Task<InlineStartOutcome> ReadOutcomeAsync(Contract.ActorRef actor, Task<InlineRootResult> outcome)
    {
        var result = await outcome;
        var status = (Contract.ActorStatus?)result.Status;
        return result.Disposition switch
        {
            InlineRootDisposition.WriteFree => new(InlineStartDisposition.Volatile, Status: status),
            InlineRootDisposition.Committed => new(InlineStartDisposition.Committed, actor, status),
            InlineRootDisposition.Refused => new(InlineStartDisposition.Refused) { RefusalReason = result.RefusalReason },
            InlineRootDisposition.OwnershipLost => new(InlineStartDisposition.Unknown),
            _ => throw new InvalidOperationException("The local owner returned an unknown start outcome.")
        };
    }
}
