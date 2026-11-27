using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Microsoft.Data.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public static partial class SqliteEmbeddedHosting
{
    // Share this edge with ingress and the running owner, including admissions made before it starts.
    // A successor can recover durable work through a different edge. An earlier wait may then time out.
    internal static SqliteDirectRequests CreateRequests(string databasePath, string node, string actorType,
        TimeSpan? deduplicationWindow = null, RootAdmissionPressure? pressure = null, StoreFormat? qualificationFormat = null,
        ProtectRootStartAsync? protectRootStart = null)
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
        return new(file.Path, node, actorType, window, pressure, qualificationFormat, protectRootStart);
    }

    internal sealed class SqliteDirectRequests : IReplyTransport
    {
        private readonly string path;
        private readonly string node;
        private readonly string actorType;
        private readonly Admission admission;
        private readonly HeldRootRequests edge;

        internal SqliteDirectRequests(string path, string node, string actorType, TimeSpan window,
            RootAdmissionPressure? pressure = null, StoreFormat? qualificationFormat = null,
            ProtectRootStartAsync? protectRootStart = null)
        {
            (this.path, this.node, this.actorType, DeduplicationWindow) = (path, node, actorType, window);
            Pressure = pressure;
            QualificationFormat = qualificationFormat;
            admission = new(this, path, window, protectRootStart);
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

        internal Task<RootStartProof> ConfirmHeldStartAsync(Contract.BoundScope scope, Contract.ActorRef actor, string issuance,
            CancellationToken token) => admission.ConfirmHeldStartAsync(scope, actor, issuance, token);

        internal Task<int> PruneHeldStartsAsync(Contract.BoundScope scope, CancellationToken token) => admission.PruneHeldStartsAsync(scope, token);

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

        private sealed class Admission(SqliteDirectRequests owner, string path, TimeSpan window, ProtectRootStartAsync? protectRootStart) : IHeldRootAdmission
        {
            public bool SupportsEvents => owner.QualificationFormat is { } format
                ? format == StoreFormat.RootLiveEventQualification || format == StoreFormat.RootPlaybackDescendantsQualification ||
                  format == StoreFormat.RootManagedSchedulesQualification || format == StoreFormat.RootPreparedStartsQualification
                : TakesHttpWaits;

            public Task<HeldRootAdmission> PublishAsync(HeldRootRequest request, string key, string? payload, CancellationToken token) =>
                WriteAsync(request.Tenant, RootAdmissionKind.Input, (admission, attempt, ct) =>
                    admission.PublishRequestAsync(request, key, payload, ct, attempt), token);

            public async ValueTask<long> ReadClockAsync(CancellationToken token)
            {
                using var file = AcquireFile(path);
                return await ForFile(file).ReadReplyTimeAsync(token);
            }

            public Task<string?> StartAsync(HeldRootRequest request, StartSpec spec, CancellationToken token) =>
                protectRootStart is null
                    ? WriteAsync(request.Tenant, RootAdmissionKind.Start, (admission, attempt, ct) =>
                        admission.StartRequestAsync(request, spec, ct, attempt), token)
                    : OnWriteWorkerAsync(() => StartProtectedAsync(request, spec, protectRootStart, token), token);

            private async Task<string?> StartProtectedAsync(HeldRootRequest request, StartSpec spec,
                ProtectRootStartAsync protect, CancellationToken token)
            {
                using var attempt = owner.Pressure?.Begin(request.Tenant, RootAdmissionKind.Start);
                var prepared = await WriteWithFileAsync(RootAdmissionKind.Start, attempt,
                    (admission, ct) => admission.PrepareHeldStartAsync(request, spec, attempt, ct), token);
                if (prepared.Stage is not { } stage || prepared.Offer is not { } offer)
                    return prepared.Existing;
                if (!await protect(offer, token))
                {
                    await WriteWithFileAsync(RootAdmissionKind.Start, attempt,
                        (admission, ct) => admission.DisarmHeldStartAsync(stage, ct), token);
                    throw new RootAdmissionRefused(Contract.RefusalReason.TargetMismatch, TimeSpan.Zero);
                }
                token.ThrowIfCancellationRequested();
                return await WriteWithFileAsync(RootAdmissionKind.Start, attempt,
                    (admission, ct) => admission.StartRequestAsync(request, spec, ct, attempt, stage), token);
            }

            internal Task<RootStartProof> ConfirmHeldStartAsync(Contract.BoundScope scope, Contract.ActorRef actor, string issuance,
                CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ConfirmHeldStartAsync(scope, actor, issuance, ct), token), token);

            internal Task<int> PruneHeldStartsAsync(Contract.BoundScope scope, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.PruneHeldStartsAsync(scope, ct), token), token);

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

            private Task<T> WriteAsync<T>(string tenant, RootAdmissionKind kind,
                Func<SqliteRootAdmission, RootAdmissionPressure.Attempt?, CancellationToken, Task<T>> admit, CancellationToken token) =>
                OnWriteWorkerAsync(async () =>
                {
                    using var attempt = owner.Pressure?.Begin(tenant, kind);
                    return await WriteWithFileAsync(kind, attempt, (admission, ct) => admit(admission, attempt, ct), token);
                }, token);

            private async Task<T> WriteWithFileAsync<T>(RootAdmissionKind kind, RootAdmissionPressure.Attempt? attempt,
                Func<SqliteRootAdmission, CancellationToken, Task<T>> admit, CancellationToken token)
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var file = AcquireFile(path, requests: owner);
                        if (attempt?.IsBusy == true) return await admit(ForFile(file), token);
                        var lane = attempt is not null && kind == RootAdmissionKind.ShedLoad ? file.ReplyOperatorWrites : file.ReplyWrites;
                        await lane.WaitAsync(token);
                        try { return await admit(ForFile(file), token); }
                        finally { lane.Release(); }
                    }
                    catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
                    {
                        attempt?.RolledBack();
                        await Task.Delay(25, token); // SQLITE_BUSY or SQLITE_LOCKED, with the original deadline.
                    }
                }
            }

            private static async Task<T> OnWriteWorkerAsync<T>(Func<Task<T>> work, CancellationToken token)
            {
                // SQLite waits synchronously for BEGIN IMMEDIATE. The worker keeps its leases until it rolls back.
                var write = Task.Run(work, token);
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
