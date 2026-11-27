using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
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
                return PostgresPreviewHosting.StartAsync(create, node, actor, escalate, options, token, fault, replies);
            }, logger);
    }

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
