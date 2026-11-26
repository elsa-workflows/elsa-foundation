using Kernel.Runtime.Hosting;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

/// <summary>Composes one shipped Embedded owner and its caller interface over a dedicated SQLite file.</summary>
/// <remarks>Install the file with <see cref="SqliteEmbeddedHosting.InstallShippedAsync"/> before starting the owner or using the interfaces.</remarks>
public sealed class SqliteEmbeddedFoundation
{
    private readonly string path;
    private readonly string node;
    private readonly string actorType;
    private readonly Lazy<SqliteEmbeddedHosting.SqliteDirectRequests> requests;

    /// <summary>Binds the database, owner identity and actor type for the shipped Embedded composition.</summary>
    public SqliteEmbeddedFoundation(string databasePath, string node, string actorType)
    {
        path = Path.GetFullPath(databasePath);
        this.node = node;
        this.actorType = actorType;
        requests = new(() => SqliteEmbeddedHosting.CreateRequests(path, node, actorType), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>Creates the host service whose reply edge also serves <see cref="OpenIngressAsync"/>.</summary>
    public SqliteEmbeddedHostedService CreateHostedService(Contract.IActorType actor, Action<string> endProcess,
        LoopOptions? options = null, bool fenceInBatch = true, ILogger<SqliteEmbeddedHostedService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Type != actorType) throw new ArgumentException("The actor type must match the Embedded foundation.", nameof(actor));
        return SqliteEmbeddedHostedService.ForShipped(path, node, actor, endProcess, options, fenceInBatch, logger, () => requests.Value);
    }

    /// <summary>Opens durable ingress and held requests routed to this foundation's owner.</summary>
    public Task<Contract.IIngress> OpenIngressAsync(CancellationToken cancellationToken = default) =>
        SqliteEmbeddedHosting.OpenFoundationWithRequestsAsync(path, actorType, requests.Value, cancellationToken);

    /// <summary>Creates a paged reader of committed roots, inboxes and journals.</summary>
    public Contract.IActorReads CreateReads(int pageSize = 100) => SqliteEmbeddedHosting.CreateFoundationReads(path, pageSize);

    /// <summary>Reads the store clock used to set held-request deadlines.</summary>
    public ValueTask<long> ReadClockAsync(CancellationToken cancellationToken = default) => requests.Value.ReadClockAsync(cancellationToken);
}
