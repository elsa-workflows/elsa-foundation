using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Runtime.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kernel.Foundation.Postgres;

/// <summary>Runs one PostgreSQL Core Preview node for the lifetime of its application host.</summary>
public sealed class PostgresPreviewHostedService : IHostedService, IAsyncDisposable
{
    private readonly NodeHostedService _service;

    /// <summary>Creates a hosted PostgreSQL preview node.</summary>
    /// <param name="logger">Each fault the node or this service records about its stop is logged once, at Error: a lost
    /// fence or a drain fault, a dispose that returned at its bound, and a fault of the shutdown itself. Once a deadline has
    /// killed the drain, an error recorded later (by an operation the kill interrupted, possibly after the stop returned) is
    /// logged at Warning, beside the kill's own Warning. A <see cref="StopAsync"/> that joins a stop already under way kills
    /// nothing: its deadline ends only its own wait.</param>
    /// <param name="requests">The held callers the node answers, on a store with held replies (<see cref="PostgresPreviewHosting.StartAsync"/>).</param>
    public PostgresPreviewHostedService(Func<KernelDb> create, string node, IActorType actor, Action<string> endProcess,
        LoopOptions? options = null, ILogger<PostgresPreviewHostedService>? logger = null, PostgresDirectRequests? requests = null)
        : this(create, node, actor, endProcess, options, logger, requests, auxiliaryActor: null)
    {
    }

    /// <summary>Creates one owner for the primary actor and a distinct auxiliary actor on a qualified store.</summary>
    public static PostgresPreviewHostedService CreateWithAuxiliaryActor(Func<KernelDb> create, string node, IActorType actor,
        IActorType auxiliaryActor, Action<string> endProcess, LoopOptions? options = null,
        ILogger<PostgresPreviewHostedService>? logger = null, PostgresDirectRequests? requests = null) =>
        CreateWithAuxiliaryActorAndRetention(create, node, actor, auxiliaryActor, endProcess, null, options, logger, requests);

    /// <summary>Creates the shared owner with the host's current retention policy.</summary>
    public static PostgresPreviewHostedService CreateWithAuxiliaryActorAndRetention(Func<KernelDb> create, string node, IActorType actor,
        IActorType auxiliaryActor, Action<string> endProcess, Func<ActorHeader, TimeSpan?>? retentionWindow,
        LoopOptions? options = null, ILogger<PostgresPreviewHostedService>? logger = null, PostgresDirectRequests? requests = null)
    {
        Kernel.Durable.ActorAdapter.RequireAuxiliaryActor(actor, auxiliaryActor);
        return new(create, node, actor, endProcess, options, logger, requests, auxiliaryActor) { RetentionWindow = retentionWindow };
    }

    /// <summary>Creates an owner that protects every prepared child birth before committing its parent checkpoint.</summary>
    public static PostgresPreviewHostedService CreateWithChildBirthProtection(Func<KernelDb> create, string node, IActorType actor,
        ProtectChildBirthsAsync protectChildBirths, Action<string> endProcess,
        Func<ActorHeader, TimeSpan?>? retentionWindow = null, LoopOptions? options = null,
        ILogger<PostgresPreviewHostedService>? logger = null, PostgresDirectRequests? requests = null, IActorType? auxiliaryActor = null)
    {
        ArgumentNullException.ThrowIfNull(protectChildBirths);
        if (auxiliaryActor is not null) Kernel.Durable.ActorAdapter.RequireAuxiliaryActor(actor, auxiliaryActor);
        return new(create, node, actor, endProcess, options, logger, requests, auxiliaryActor, protectChildBirths)
            { RetentionWindow = retentionWindow };
    }

    /// <summary>Creates the PostgreSQL owner with inline start promotion.</summary>
    public static PostgresPreviewHostedService CreateInlineQualification(Func<KernelDb> create, string node, IActorType actor,
        IActorType? auxiliaryActor, PostgresDirectRequests requests, ProtectChildBirthsAsync protectChildren, InlineRootRequests inlineStarts,
        Action<string> endProcess, Func<ActorHeader, TimeSpan?> retentionWindow, ILogger<PostgresPreviewHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(protectChildren);
        ArgumentNullException.ThrowIfNull(inlineStarts);
        if (auxiliaryActor is not null) Kernel.Durable.ActorAdapter.RequireAuxiliaryActor(actor, auxiliaryActor);
        return new(create, node, actor, endProcess, null, logger, requests, auxiliaryActor, protectChildren, inlineStarts)
            { RetentionWindow = retentionWindow };
    }

    /// <summary>Confirms an exact protected child using the same installed store as the owner.</summary>
    public static ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(Func<KernelDb> create, BoundScope scope,
        ActorRef child, string issuance, CancellationToken cancellationToken = default) =>
        EfStore.ConfirmRootChildBirthAsync(create, scope, child, issuance, cancellationToken);

    private PostgresPreviewHostedService(Func<KernelDb> create, string node, IActorType actor, Action<string> endProcess,
        LoopOptions? options, ILogger<PostgresPreviewHostedService>? logger, PostgresDirectRequests? requests, IActorType? auxiliaryActor,
        ProtectChildBirthsAsync? protectChildBirths = null, InlineRootRequests? inlineStarts = null)
    {
        ArgumentNullException.ThrowIfNull(endProcess);
        if (requests is not null && requests.Node != node)
            throw new ArgumentException($"The held callers wait for node '{requests.Node}', and this node is '{node}'.", nameof(requests));

        _service = new NodeHostedService("PostgreSQL preview", node, endProcess, (options ?? new LoopOptions()).DisposeDeadline,
            (fault, escalate, token) =>
            {
                if (StartNode is { } start) return start(fault, token);
                Kernel.Durable.IReplyTransport? replies = requests;
                if (replies is not null && ObserveReplies is { } observe) replies = observe(replies);
                return PostgresPreviewHosting.StartAsync(create, node, actor, escalate, options, token, fault, replies,
                    retentionWindow: RetentionWindow, auxiliaryActor: auxiliaryActor, protectChildBirths: protectChildBirths, inlineStarts: inlineStarts,
                    localRequests: requests);
            }, logger);
    }

    /// <summary>Evaluates the host's current policy when the owner handles a retention command.</summary>
    public Func<ActorHeader, TimeSpan?>? RetentionWindow { get; init; }

    internal Func<Action<Exception>, CancellationToken, Task<RuntimeNode>>? StartNode { get; init; }
    internal Func<Kernel.Durable.IReplyTransport, Kernel.Durable.IReplyTransport>? ObserveReplies { get; init; }
    internal Action? AfterNodeStop { get => _service.AfterNodeStop; init => _service.AfterNodeStop = value; }
    internal void NodeFault(Exception fault) => _service.NodeFault(fault);

    /// <summary>The grace the deadline's kill gets to end the stop before the process policy runs.</summary>
    public static readonly TimeSpan KillGrace = NodeHostedService.KillGrace;

    /// <summary>The shutdown fault, if any, or the last fault recorded by the node.</summary>
    public Exception? LastError => _service.LastError;

    public Task StartAsync(CancellationToken cancellationToken) => _service.StartAsync(cancellationToken);

    /// <summary>Stops the node. An owning caller's deadline kills the drain. A joining caller's deadline ends its wait.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => _service.StopAsync(cancellationToken);

    /// <summary>Stops the node and kills an unfinished drain at <see cref="LoopOptions.DisposeDeadline"/>.</summary>
    public ValueTask DisposeAsync() => _service.DisposeAsync();
}
