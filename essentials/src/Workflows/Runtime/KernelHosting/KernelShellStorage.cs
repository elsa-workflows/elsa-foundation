using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal abstract class KernelShellStorage
{
    public abstract bool TakesSelfSends { get; }
    public abstract bool TakesTimers { get; }
    public abstract bool TakesChildren { get; }
    public abstract bool TakesHttpWaits { get; }
    public abstract Task InstallAsync(CancellationToken token);
    public abstract ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token);
    public abstract ValueTask<long> ReadClockAsync(CancellationToken token);
    public abstract (IHostedService Service, IAsyncDisposable Lifetime) CreateOwner(IActorType actor, ILoggerFactory logs,
        Func<ActorHeader, TimeSpan?> retentionWindow);

    public static KernelShellStorage Create(string provider, string? sqlitePath, string? postgresConnectionString)
    {
        var node = $"foundation-kernel-{Environment.ProcessId}-{Guid.NewGuid():N}";
        return provider?.Trim().ToLowerInvariant() switch
        {
            "sqlite" when sqlitePath is not null && Path.IsPathFullyQualified(sqlitePath) && string.IsNullOrWhiteSpace(postgresConnectionString) =>
                new Sqlite(node, sqlitePath),
            "postgresql" when !string.IsNullOrWhiteSpace(postgresConnectionString) && string.IsNullOrWhiteSpace(sqlitePath) =>
                new Postgres(node, postgresConnectionString),
            "sqlite" => throw new InvalidOperationException("Set SqlitePath to an absolute path to a dedicated database and leave PostgresConnectionString empty."),
            "postgresql" => throw new InvalidOperationException("Set PostgresConnectionString for the dedicated kernel database and leave SqlitePath empty."),
            _ => throw new InvalidOperationException("Use SQLite or PostgreSQL for WorkflowsRuntimeKernelHosting's Provider.")
        };
    }

    private sealed class Sqlite(string node, string path) : KernelShellStorage
    {
        private readonly SqliteEmbeddedFoundation _foundation = new(path, node, WorkflowActor.ActorType);

        public override bool TakesSelfSends => SqliteEmbeddedHosting.TakesSelfSends;
        public override bool TakesTimers => SqliteEmbeddedHosting.TakesTimers;
        public override bool TakesChildren => SqliteEmbeddedHosting.TakesChildren;
        public override bool TakesHttpWaits => SqliteEmbeddedHosting.TakesHttpWaits;
        public override Task InstallAsync(CancellationToken token) => _foundation.InstallShippedAsync(token);
        public override ValueTask<long> ReadClockAsync(CancellationToken token) => _foundation.ReadClockAsync(token);

        public override async ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token) =>
            new(await _foundation.OpenIngressAsync(token), _foundation.CreateReads(), _foundation.AdmitRetentionDeleteAsync);

        public override (IHostedService, IAsyncDisposable) CreateOwner(IActorType actor, ILoggerFactory logs,
            Func<ActorHeader, TimeSpan?> retentionWindow)
        {
            var owner = _foundation.CreateHostedServiceWithRetention(actor, Environment.FailFast,
                logger: logs.CreateLogger<SqliteEmbeddedHostedService>(), retentionWindow: retentionWindow);
            return (owner, owner);
        }
    }

    private sealed class Postgres : KernelShellStorage
    {
        private readonly DbContextOptions<KernelDb> _options;
        private readonly PostgresDirectRequests _requests;
        private readonly string _node;

        public Postgres(string node, string connectionString)
        {
            _node = node;
            _options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connectionString).Options;
            _requests = PostgresPreviewHosting.CreateRequests(OpenDatabase, node, WorkflowActor.ActorType);
        }

        public override bool TakesSelfSends => PostgresPreviewHosting.TakesSelfSends(OpenDatabase);
        public override bool TakesTimers => PostgresPreviewHosting.TakesTimers(OpenDatabase);
        public override bool TakesChildren => PostgresPreviewHosting.TakesChildren(OpenDatabase);
        public override bool TakesHttpWaits => PostgresPreviewHosting.TakesHttpWaits(OpenDatabase);
        public override Task InstallAsync(CancellationToken token) => PostgresPreviewHosting.InstallShippedAsync(_options, token);
        public override ValueTask<long> ReadClockAsync(CancellationToken token) => _requests.ReadClockAsync(token);

        public override async ValueTask<KernelShellFoundation> OpenAsync(CancellationToken token)
        {
            var foundation = await PostgresPreviewHosting.OpenFoundationAsync(OpenDatabase, WorkflowActor.ActorType, _requests,
                cancellationToken: token);
            return new(foundation, foundation, foundation.AdmitRetentionDeleteAsync);
        }

        public override (IHostedService, IAsyncDisposable) CreateOwner(IActorType actor, ILoggerFactory logs,
            Func<ActorHeader, TimeSpan?> retentionWindow)
        {
            var owner = new PostgresPreviewHostedService(OpenDatabase, _node, actor, Environment.FailFast,
                logger: logs.CreateLogger<PostgresPreviewHostedService>(), requests: _requests)
            {
                RetentionWindow = retentionWindow
            };
            return (owner, owner);
        }

        private KernelDb OpenDatabase() => PostgresPreviewHosting.CreateShippedContext(_options);
    }
}

internal sealed record KernelShellFoundation(IIngress Ingress, IActorReads Reads,
    Func<BoundScope, ActorRef, string, CancellationToken, ValueTask<Admission>> AdmitRetentionDelete);
