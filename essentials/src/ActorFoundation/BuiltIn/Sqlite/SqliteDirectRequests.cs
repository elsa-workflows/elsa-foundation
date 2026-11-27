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
        TimeSpan? deduplicationWindow = null, RootAdmissionPressure? pressure = null, StoreFormat? qualificationFormat = null)
    {
        RequireFoundationActorType(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        if (node.Length > 256 || node.Contains('|') || node.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(node))
            throw new ArgumentException("Invalid SQLite reply node.", nameof(node));
        var window = deduplicationWindow ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        using var file = AcquireFile(databasePath);
        using var db = KernelDb.ForSqliteRootShipped(file.Options, qualificationFormat);
        if (db.UsesRootDueBacklog) pressure ??= RootAdmissionPressure.CreateDefault();
        return new(file.Path, node, actorType, window, pressure, qualificationFormat);
    }

    internal sealed class SqliteDirectRequests : IReplyTransport
    {
        private readonly string path;
        private readonly string node;
        private readonly string actorType;
        private readonly Admission admission;
        private readonly HeldRootRequests edge;

        internal SqliteDirectRequests(string path, string node, string actorType, TimeSpan window,
            RootAdmissionPressure? pressure = null, StoreFormat? qualificationFormat = null)
        {
            (this.path, this.node, this.actorType, DeduplicationWindow) = (path, node, actorType, window);
            Pressure = pressure;
            QualificationFormat = qualificationFormat;
            admission = new(this, path, window);
            edge = new(admission, node, actorType);
        }

        internal TimeSpan DeduplicationWindow { get; }
        internal RootAdmissionPressure? Pressure { get; }
        internal StoreFormat? QualificationFormat { get; }

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
            DuringLocalOwnershipAsync(() => edge.RequestAsync(scope, target, id, kind, payload, add, request, token), token);

        internal ValueTask<Contract.Reply> RequestOperatorAsync(Contract.BoundScope scope, Contract.ActorRef actor, string id, string kind,
            Contract.OperatorCommand command, TimeSpan? keepFor, Contract.RequestOptions request, CancellationToken token) =>
            DuringLocalOwnershipAsync(() => edge.RequestOperatorAsync(scope, id, request, keepFor,
                (caller, ct) => admission.OperateAsync(actor, kind, command, caller, ct),
                ct => ReadOperatorStatusAsync(scope.Tenant, actor.Id, ct), token), token);

        internal ValueTask<Contract.ActorStatus?> ReadOperatorStatusAsync(string tenant, string reference, CancellationToken token) =>
            ObserveOperatorStatusAsync(ct => admission.ReadOperatorStatusAsync(tenant, reference, ct), token);

        internal static async ValueTask<Contract.ActorStatus?> ObserveOperatorStatusAsync(
            Func<CancellationToken, ValueTask<Contract.ActorStatus?>> read, CancellationToken token)
        {
            // The timeout is already settled. A SQLite command can wait synchronously despite its cancellation token.
            var allowance = TimeSpan.FromMilliseconds(100);
            using var observation = CancellationTokenSource.CreateLinkedTokenSource(token);
            observation.CancelAfter(allowance);
            var readToken = observation.Token;
            var pending = Task.Run(async () => await read(readToken), readToken);
            try { return await pending.WaitAsync(allowance, token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
            catch (Exception error) when (error is TimeoutException or SqliteException or StoreException ||
                error is IOException and not SqliteHostLockException) { return null; }
            finally
            {
                if (!pending.IsCompletedSuccessfully) _ = ObserveAbandonedAsync(pending);
            }
        }

        private async ValueTask<Contract.Reply> DuringLocalOwnershipAsync(Func<ValueTask<Contract.Reply>> request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (AcquireFile(path, requests: this)) token.ThrowIfCancellationRequested();
            try { return await request(); }
            catch (SqliteHostLockException)
            {
                return new(null, null, Error: "lost");
            }
        }

        private static async Task ObserveAbandonedAsync(Task operation)
        {
            try { await operation; }
            catch (Exception) { } // The caller's wait has already settled.
        }

        private sealed class Admission(SqliteDirectRequests owner, string path, TimeSpan window) : IHeldRootAdmission
        {
            public async ValueTask<long> ReadClockAsync(CancellationToken token)
            {
                using var file = AcquireFile(path);
                return await ForFile(file).ReadReplyTimeAsync(token);
            }

            public Task<string?> StartAsync(HeldRootRequest request, StartSpec spec, CancellationToken token) =>
                WriteAsync(request.Tenant, RootAdmissionKind.Start, (admission, attempt, ct) =>
                    admission.StartRequestAsync(request, spec, ct, attempt), token);

            public Task<HeldRootAdmission> SendAsync(HeldRootRequest request, string reference, string kind, string? payload,
                CancellationToken token) =>
                WriteAsync(request.Tenant, RootAdmissionKind.Input, (admission, attempt, ct) =>
                    admission.SendRequestAsync(request, reference, kind, payload, ct, attempt), token);

            internal Task<Contract.Admission?> OperateAsync(Contract.ActorRef actor, string kind, Contract.OperatorCommand command,
                HeldRootRequest request, CancellationToken token) =>
                WriteAsync(request.Tenant, RootAdmissionPressure.Operator(kind), (admission, attempt, ct) =>
                    admission.OperateHeldAsync(actor, kind, command, request, ct, attempt), token);

            public async ValueTask<Contract.ActorStatus?> ReadStatusAsync(string tenant, string reference, CancellationToken token)
            {
                using var file = AcquireFile(path);
                var store = new EfStore(() => KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions,
                    owner.QualificationFormat), postgres: false);
                return (Contract.ActorStatus?)(await store.ReadFirstRootAsync(tenant, reference, token))?.Status;
            }

            internal async ValueTask<Contract.ActorStatus?> ReadOperatorStatusAsync(string tenant, string reference, CancellationToken token)
            {
                using var file = AcquireFile(path);
                return await ForFile(file).ReadOperatorStatusAsync(tenant, reference, token);
            }

            private SqliteRootAdmission ForFile(FileLease file) =>
                CreateAdmission(file, window, owner.Pressure, owner.QualificationFormat);

            private async Task<T> WriteAsync<T>(string tenant, RootAdmissionKind kind,
                Func<SqliteRootAdmission, RootAdmissionPressure.Attempt?, CancellationToken, Task<T>> admit, CancellationToken token)
            {
                // SQLite waits synchronously for BEGIN IMMEDIATE. Keep the caller's wait cancellable and give the
                // background operation its own leases so it can roll back after the caller has gone.
                var write = Task.Run(async () =>
                {
                    using var attempt = owner.Pressure?.Begin(tenant, kind);
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            using var file = AcquireFile(path, requests: owner);
                            if (attempt?.IsBusy == true) return await admit(ForFile(file), attempt, token);
                            var lane = attempt is not null && kind == RootAdmissionKind.ShedLoad ? file.ReplyOperatorWrites : file.ReplyWrites;
                            await lane.WaitAsync(token);
                            try { return await admit(ForFile(file), attempt, token); }
                            finally { lane.Release(); }
                        }
                        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
                        {
                            attempt?.RolledBack();
                            await Task.Delay(25, token); // SQLITE_BUSY or SQLITE_LOCKED, with the original deadline.
                        }
                    }
                }, token);
                try { return await write.WaitAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    _ = ObserveAbandonedAsync(write);
                    throw;
                }
            }

        }
    }
}
