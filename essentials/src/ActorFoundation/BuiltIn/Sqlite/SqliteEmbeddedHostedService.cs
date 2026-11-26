using Kernel.Durable;
using Kernel.Runtime.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

/// <summary>Runs an Embedded SQLite node for the lifetime of its application host.</summary>
public sealed class SqliteEmbeddedHostedService : IHostedService, IAsyncDisposable
{
    private readonly NodeHostedService _service;

    /// <summary>Runs the installed SQLite kernel qualification format.</summary>
    public SqliteEmbeddedHostedService(string databasePath, string node, IActorType actor, Action<string> endProcess,
        LoopOptions? options = null, bool fenceInBatch = true, ILogger<SqliteEmbeddedHostedService>? logger = null)
        : this(new("SQLite embedded", node, endProcess, (options ?? new LoopOptions()).DisposeDeadline,
            (fault, escalate, token) => SqliteEmbeddedHosting.StartAsync(databasePath, node, actor, escalate, options, fenceInBatch, token, fault),
            logger))
    {
    }

    private SqliteEmbeddedHostedService(NodeHostedService service) => _service = service;

    /// <summary>Runs the shipped SQLite format with durable creators and ordinary root turns.</summary>
    internal static SqliteEmbeddedHostedService ForFoundation(string databasePath, string node, Contract.IActorType actor,
        Action<string> endProcess, LoopOptions? options = null, bool fenceInBatch = true, ILogger<SqliteEmbeddedHostedService>? logger = null) =>
        new(new NodeHostedService("SQLite embedded", node, endProcess, (options ?? new LoopOptions()).DisposeDeadline,
            (fault, escalate, token) => SqliteEmbeddedHosting.StartFoundationAsync(databasePath, node, actor, escalate, options,
                fenceInBatch, token, fault), logger));

    /// <summary>The shutdown fault, if any, or the last fault recorded by the node.</summary>
    public Exception? LastError => _service.LastError;

    public Task StartAsync(CancellationToken cancellationToken) => _service.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => _service.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _service.DisposeAsync();
}
