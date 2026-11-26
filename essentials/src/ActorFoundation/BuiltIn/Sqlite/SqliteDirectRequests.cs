using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public static partial class SqliteEmbeddedHosting
{
    // Share this edge with ingress and the running owner, including admissions made before it starts.
    // A successor can recover durable work through a different edge. An earlier wait may then time out.
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
            admission = new(this, path, window);
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

        internal async ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind, string? payload,
            Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (AcquireFile(path, requests: this)) token.ThrowIfCancellationRequested();
            try { return await edge.RequestAsync(scope, target, id, kind, payload, add, request, token); }
            catch (SqliteHostLockException)
            {
                return new(null, null, Error: "lost");
            }
        }

        private sealed class Admission(SqliteDirectRequests owner, string path, TimeSpan window) : IHeldRootAdmission
        {
            public async ValueTask<long> ReadClockAsync(CancellationToken token)
            {
                using var file = AcquireFile(path);
                return await ForFile(file).ReadReplyTimeAsync(token);
            }

            public Task<string?> StartAsync(HeldRootRequest request, StartSpec spec, CancellationToken token) =>
                WriteAsync((admission, ct) => admission.StartRequestAsync(request, spec, ct), token);

            public Task<HeldRootAdmission> SendAsync(HeldRootRequest request, string reference, string kind, string? payload,
                CancellationToken token) =>
                WriteAsync((admission, ct) => admission.SendRequestAsync(request, reference, kind, payload, ct), token);

            public async ValueTask<Contract.ActorStatus?> ReadStatusAsync(string tenant, string reference, CancellationToken token)
            {
                using var file = AcquireFile(path);
                var store = new EfStore(() => KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions), postgres: false);
                return (Contract.ActorStatus?)(await store.ReadFirstRootAsync(tenant, reference, token))?.Status;
            }

            private SqliteRootAdmission ForFile(FileLease file) =>
                new(() => KernelDb.ForSqliteRootShipped(file.Options), window, file.AdmissionDefinitions);

            private async Task<T> WriteAsync<T>(Func<SqliteRootAdmission, CancellationToken, Task<T>> admit, CancellationToken token)
            {
                // SQLite waits synchronously for BEGIN IMMEDIATE. Keep the caller's wait cancellable and give the
                // background operation its own leases so it can roll back after the caller has gone.
                var write = Task.Run(async () =>
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            using var file = AcquireFile(path, requests: owner);
                            await file.ReplyWrites.WaitAsync(token);
                            try { return await admit(ForFile(file), token); }
                            finally { file.ReplyWrites.Release(); }
                        }
                        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
                        {
                            await Task.Delay(25, token); // SQLITE_BUSY or SQLITE_LOCKED, with the original deadline.
                        }
                    }
                }, token);
                try { return await write.WaitAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    _ = ObserveCancelledWriteAsync(write);
                    throw;
                }
            }

            private static async Task ObserveCancelledWriteAsync(Task write)
            {
                try { await write; }
                catch (Exception) { } // The caller already observed cancellation. The operation owns its rollback.
            }
        }
    }
}
