using Kernel.Durable;
using Kernel.EntityFramework;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public static partial class SqliteEmbeddedHosting
{
    internal static SqliteDirectRequests CreateRequests(string databasePath, string node, string actorType,
        TimeSpan? deduplicationWindow = null)
    {
        RequireFoundationActorType(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        if (node.Length > 256 || node.Contains('|') || node.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(node))
            throw new ArgumentException("Invalid SQLite reply node.", nameof(node));
        var window = deduplicationWindow ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        using var file = AcquireFile(databasePath);
        return new(file.Path, node, actorType, window);
    }

    internal sealed class SqliteDirectRequests : IReplyTransport
    {
        private readonly string path;
        private readonly string node;
        private readonly string actorType;
        private readonly Admission admission;
        private readonly HeldRootRequests edge;

        internal SqliteDirectRequests(string path, string node, string actorType, TimeSpan window)
        {
            (this.path, this.node, this.actorType, DeduplicationWindow) = (path, node, actorType, window);
            admission = new(path, window);
            edge = new(admission, node, actorType);
        }

        internal TimeSpan DeduplicationWindow { get; }

        internal void RequireBinding(string databasePath, string configuredType, string? owner = null)
        {
            using var file = AcquireFile(path);
            if (file.Path != databasePath || actorType != configuredType || owner is not null && owner != node)
                throw new ArgumentException("SQLite held requests must share the owner node, database and actor type.");
        }

        internal ValueTask<long> ReadClockAsync(CancellationToken token = default) => admission.ReadClockAsync(token);

        void IReplyTransport.Send(string destination, Reply reply) => edge.Send(destination, reply);

        internal ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind, string? payload,
            Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken token) =>
            edge.RequestAsync(scope, target, id, kind, payload, add, request, token);

        private sealed class Admission(string path, TimeSpan window) : IHeldRootAdmission
        {
            public async ValueTask<long> ReadClockAsync(CancellationToken token)
            {
                using var file = AcquireFile(path);
                return await ForFile(file).ReadReplyTimeAsync(token);
            }

            public async Task<string?> StartAsync(HeldRootRequest request, StartSpec spec, CancellationToken token)
            {
                using var file = AcquireFile(path);
                return await ForFile(file).StartRequestAsync(request, spec, token);
            }

            public async Task<HeldRootAdmission> SendAsync(HeldRootRequest request, string reference, string kind, string? payload,
                CancellationToken token)
            {
                using var file = AcquireFile(path);
                return await ForFile(file).SendRequestAsync(request, reference, kind, payload, token);
            }

            public async ValueTask<Contract.ActorStatus?> ReadStatusAsync(string tenant, string reference, CancellationToken token)
            {
                using var file = AcquireFile(path);
                var store = new EfStore(() => KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions), postgres: false);
                return (Contract.ActorStatus?)(await store.ReadFirstRootAsync(tenant, reference, token))?.Status;
            }

            private SqliteRootAdmission ForFile(FileLease file) =>
                new(() => KernelDb.ForSqliteRootShipped(file.Options), window, file.AdmissionDefinitions);
        }
    }
}
