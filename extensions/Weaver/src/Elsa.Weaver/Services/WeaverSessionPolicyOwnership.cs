using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Weaver.Services;

/// <summary>The kinds of admitted work that own a session's current policy.</summary>
internal enum WeaverSessionClaimKind
{
    Prompt,
    Foreground,
    Preparation,
    Proposal,
    Pause
}

/// <summary>
/// An opaque, server-only ownership token on one session row. Identity is by reference: releasing an
/// old token never releases a successor. Never accepted from or emitted to transport or module payloads.
/// </summary>
internal sealed class WeaverSessionClaim
{
    internal WeaverSessionClaim(string sessionId, WeaverSessionClaimKind kind, WeaverSessionPolicyState state, DateTimeOffset? deadline)
    {
        SessionId = sessionId;
        Kind = kind;
        State = state;
        Deadline = deadline;
    }

    public string SessionId { get; }
    public WeaverSessionClaimKind Kind { get; }

    /// <summary>The policy snapshot and revision that govern the claimed work; read only after acquisition.</summary>
    public WeaverSessionPolicyState State { get; }

    // Mutated only under the owning row monitor.
    internal string? Key { get; set; }
    internal DateTimeOffset? Deadline { get; }
    internal bool Dispatched { get; set; }
    /// <summary>This process dispatched the proposal and its executor has not returned yet; only then is the lease renewed for it.</summary>
    internal bool Executing { get; set; }
}

/// <summary>
/// A default lifecycle service that acquires its claims from <see cref="PolicyOwner"/>. The owner is the concrete
/// default session service: it owns the session rows, so it is never replaced independently of them.
/// </summary>
internal interface IWeaverSessionPolicyParticipant
{
    /// <summary>The owner this instance and its claim-holding collaborators participate with, or null.</summary>
    InMemoryWeaverSessionService? PolicyOwner { get; }
}

internal static class WeaverSessionPolicyParticipation
{
    internal static InMemoryWeaverSessionService? SameOwner(InMemoryWeaverSessionService? owner, params object[] participants)
        => owner is not null && participants.All(participant => participant is IWeaverSessionPolicyParticipant candidate
            && ReferenceEquals(candidate.PolicyOwner, owner)) ? owner : null;
}

/// <summary>
/// Preview policy readback and conditional autonomy update. Supported only when the resolved session
/// service, orchestrator, invoker and proposal service are default participants of one owner instance.
/// </summary>
internal sealed class DefaultWeaverSessionPolicyService(IServiceProvider services) : IWeaverSessionPolicyService
{
    private static readonly WeaverSessionPolicyResult Unsupported = new(WeaverSessionPolicyOutcome.Unsupported, null);

    public Task<WeaverSessionPolicyResult> GetAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Owner() is { } owner ? owner.GetPolicyAsync(sessionId, actorId, tenantId, cancellationToken) : Task.FromResult(Unsupported);
    }

    public async Task<WeaverSessionPolicyResult> ChangeAutonomyAsync(WeaverSessionAutonomyChangeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (Owner() is not { } owner) return Unsupported;
        // An operation whose outcome could not be recorded still holds its dispatched claim; it is settled from its record
        // first (proposal gate before session gate), so the busy check sees what the record says. Only for a defined mode
        // and the session's own actor and tenant, so the #3440 refusal order is unchanged and a foreign caller settles nothing.
        if (Enum.IsDefined(request.RequestedMode)
            && services.GetRequiredService<IWeaverProposalService>() is InMemoryWeaverProposalService proposals
            && await owner.FindOwnerAsync(request.SessionId, cancellationToken) is { } sessionOwner
            && sessionOwner.ActorId == request.ActorId && sessionOwner.TenantId == request.TenantId)
            await proposals.ReconcileSessionAsync(request.SessionId, cancellationToken);
        return await owner.ChangeAutonomyAsync(request, cancellationToken);
    }

    // Participants are resolved lazily so that an unrelated provider conflict surfaces from its own consumer.
    private InMemoryWeaverSessionService? Owner()
    {
        var sessions = services.GetRequiredService<IWeaverSessionService>();
        var owner = sessions as InMemoryWeaverSessionService;
        return WeaverSessionPolicyParticipation.SameOwner(owner, sessions,
            services.GetRequiredService<IWeaverStreamingService>(),
            services.GetRequiredService<IWeaverToolInvoker>(),
            services.GetRequiredService<IWeaverProposalService>());
    }
}

/// <summary>Whether a restart can recover a composition's sessions.</summary>
internal enum WeaverDurableRecoverySupport
{
    Supported,
    Unsupported
}

/// <summary>
/// Durable recovery is supported only when the default owner participates with every default lifecycle service, keeps
/// the paused turn on its own session record, and writes through a record store that fences every write. A replaced
/// <see cref="IWeaverTurnStateStore"/> or a store that cannot fence is Unsupported: with the in-memory default it keeps the
/// in-process behaviour and bootstrap reports <c>unsupported</c>; with a durable store selected, <see cref="WeaverDurableRecoveryValidator"/>
/// fails shell activation.
/// </summary>
internal sealed class WeaverDurableRecovery(IServiceProvider services)
{
    public WeaverDurableRecoverySupport Evaluate()
    {
        var sessions = services.GetRequiredService<IWeaverSessionService>();
        var owner = WeaverSessionPolicyParticipation.SameOwner(sessions as InMemoryWeaverSessionService, sessions,
            services.GetRequiredService<IWeaverStreamingService>(),
            services.GetRequiredService<IWeaverToolInvoker>(),
            services.GetRequiredService<IWeaverProposalService>());
        return owner is not null && owner.RecordStore.SupportsFencing
            && services.GetRequiredService<IWeaverTurnStateStore>() is InMemoryWeaverTurnStateStore { Owner: { } pauses } && ReferenceEquals(pauses, owner)
            ? WeaverDurableRecoverySupport.Supported
            : WeaverDurableRecoverySupport.Unsupported;
    }

    /// <summary>Whether a record store other than the process-lifetime default is selected (for example, the EF store).</summary>
    public bool DurableStoreSelected => services.GetService<IWeaverSessionRecordStore>() is { } store and not InMemoryWeaverSessionRecordStore;

    /// <summary>
    /// What a restart does to this composition's sessions, as bootstrap reports it: <c>durable</c> (a durable store backs a
    /// supported composition), <c>processLifetime</c> (the in-memory default: sessions survive reloads while the process
    /// lives, and a restart ends them) or <c>unsupported</c> (the composition cannot recover its sessions).
    /// </summary>
    public string Describe() => Evaluate() == WeaverDurableRecoverySupport.Unsupported ? WeaverRecoveryModes.Unsupported
        : DurableStoreSelected ? WeaverRecoveryModes.Durable : WeaverRecoveryModes.ProcessLifetime;
}

/// <summary>The default <see cref="IWeaverRecoverySupport"/>: the composition's <see cref="WeaverDurableRecovery"/>.</summary>
internal sealed class DefaultWeaverRecoverySupport(WeaverDurableRecovery recovery) : IWeaverRecoverySupport
{
    public string Describe() => recovery.Describe();
}

/// <summary>
/// At shell activation, refuses a durable record store configured into a composition that cannot use it for recovery (a
/// replaced session, streaming, invoker, proposal or paused-turn service, or a store that cannot fence). Such a composition
/// would rehydrate sessions from the durable store while losing paused turns and claims, so it fails activation with a
/// clear diagnostic instead of running partly durable. A composition whose lifecycle services cannot be resolved here is
/// reported by its own consumer.
/// </summary>
internal sealed class WeaverDurableRecoveryValidator(IServiceScopeFactory scopes, ILogger<WeaverDurableRecoveryValidator>? logger = null) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool refused;
        try
        {
            using var scope = scopes.CreateScope();
            var recovery = scope.ServiceProvider.GetRequiredService<WeaverDurableRecovery>();
            refused = recovery.DurableStoreSelected && recovery.Evaluate() == WeaverDurableRecoverySupport.Unsupported;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger?.LogDebug("Durable Weaver recovery support could not be evaluated at activation ({FailureType}).", failure.GetType().Name);
            return Task.CompletedTask;
        }
        if (refused)
            throw new InvalidOperationException(
                $"A durable '{nameof(IWeaverSessionRecordStore)}' is selected, but this composition cannot use it for durable recovery: the " +
                $"session, streaming, invoker and proposal services must be the default participants of one session owner, " +
                $"'{nameof(IWeaverTurnStateStore)}' must be the default, and the store must fence its writes. Remove the replacement, or do not " +
                "select a durable store.");
        return Task.CompletedTask;
    }
}
