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

    internal InlineRootRequests(Contract.BoundScope scope, string actorType,
        Func<Contract.StartSpec, object, bool> validate, ProtectRootStartAsync protect,
        Contract.BoundScope additionalScope, Func<Contract.ActorStatus, bool> allowAdditionalWriteFreeCompletion)
        : this(scope, actorType, validate, protect)
    {
        if (additionalScope.Tenant == scope.Tenant)
            throw new ArgumentException("The additional inline scope must name a distinct tenant.", nameof(additionalScope));
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

    /// <summary>Offers an exact start and its trusted local material to this owner, then observes its outcome.</summary>
    public ValueTask<InlineStartOutcome> StartAsync(string operationId, Contract.StartSpec start, object material,
        CancellationToken token) => StartCoreAsync(scope, operationId, start, material, token);

    internal ValueTask<InlineStartOutcome> StartAdditionalAsync(string operationId, Contract.StartSpec start, object material,
        CancellationToken token) => _additionalScope is { } additional
        ? StartCoreAsync(additional, operationId, start, material, token)
        : ValueTask.FromResult(new InlineStartOutcome(InlineStartDisposition.Refused));

    private async ValueTask<InlineStartOutcome> StartCoreAsync(Contract.BoundScope selectedScope, string operationId,
        Contract.StartSpec start, object material, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (start.Type != actorType || EfStore.CreateInlineRootSpec(operationId, start) is not { } trusted || !validate(start, material))
            return new(InlineStartDisposition.Refused);
        (Contract.ActorRef Reference, Task<InlineRootResult> Outcome)? pending;
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_owner is not { } owner) return new(InlineStartDisposition.Unavailable);
            using var attempt = owner.Store.AdmissionPressure!.BeginInlineStart(selectedScope.Tenant);
            try { attempt.RequireInlineTenantRate(); }
            catch (RootAdmissionRefused refusal)
            {
                var admission = refusal.Admission;
                return new(InlineStartDisposition.Unavailable)
                    { RefusalReason = admission.Reason, RetryAfter = admission.RetryAfter };
            }
            pending = owner.Store.QueueInlineRoot(owner.Worker, operationId, trusted, material, owner.Wake, selectedScope);
            // Queued authored work spends its start permit even if it finishes without a checkpoint.
            if (pending is not null) attempt.Committed();
        }
        if (pending is not { } admitted) return new(InlineStartDisposition.Unavailable);

        try
        {
            var result = await admitted.Outcome.WaitAsync(RootPreparedStarts.OneWayBudget, token);
            var status = (Contract.ActorStatus?)result.Status;
            return result.Disposition switch
            {
                InlineRootDisposition.WriteFree => new(InlineStartDisposition.Volatile, Status: status),
                InlineRootDisposition.Committed => new(InlineStartDisposition.Committed, admitted.Reference, status),
                InlineRootDisposition.Refused => new(InlineStartDisposition.Refused) { RefusalReason = result.RefusalReason },
                InlineRootDisposition.OwnershipLost => new(InlineStartDisposition.Unknown),
                _ => throw new InvalidOperationException("The local owner returned an unknown start outcome.")
            };
        }
        catch (TimeoutException)
        {
            token.ThrowIfCancellationRequested();
            // Queueing succeeded. A late commit may win, so the caller cannot safely retry it as a shed request.
            return new(InlineStartDisposition.Unknown);
        }
    }
}
