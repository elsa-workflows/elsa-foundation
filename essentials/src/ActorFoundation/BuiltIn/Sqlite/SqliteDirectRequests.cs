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
        ProtectRootStartAsync? protectRootStart = null, string? authoredStandaloneTenant = null)
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
        var publicEventHost = db.UsesRootPublicEventHost
            ? new RootPublicEventHost(actorType, protectRootStart is not null) { AuthoredStandaloneTenant = authoredStandaloneTenant }
            : null;
        return new(file.Path, node, actorType, window, db.UsesRootPublicEventStarts, db.UsesRootKeyedStarts, publicEventHost,
            pressure, qualificationFormat, protectRootStart);
    }

    internal sealed class SqliteDirectRequests : IReplyTransport
    {
        private readonly string path;
        private readonly string node;
        private readonly string actorType;
        private readonly Admission admission;
        private readonly HeldRootRequests edge;
        private readonly ProtectRootStartAsync? protectRootStart;

        internal SqliteDirectRequests(string path, string node, string actorType, TimeSpan window, bool supportsPublicEvents,
            bool supportsKeys, RootPublicEventHost? publicEventHost, RootAdmissionPressure? pressure = null, StoreFormat? qualificationFormat = null,
            ProtectRootStartAsync? protectRootStart = null)
        {
            (this.path, this.node, this.actorType, DeduplicationWindow) = (path, node, actorType, window);
            Pressure = pressure;
            QualificationFormat = qualificationFormat;
            PublicEventHost = publicEventHost;
            this.protectRootStart = protectRootStart;
            admission = new(this, path, window, protectRootStart, supportsPublicEvents, supportsKeys);
            edge = new(admission, node, actorType);
        }

        internal TimeSpan DeduplicationWindow { get; }
        internal RootAdmissionPressure? Pressure { get; }
        internal StoreFormat? QualificationFormat { get; }
        internal RootPublicEventHost? PublicEventHost { get; }
        internal Requests Pending => edge.Pending;
        internal HeldRootRequests HeldEdge => edge;

        internal void RequireBinding(string databasePath, string configuredType, string? owner = null)
        {
            using var file = AcquireFile(path);
            if (file.Path != databasePath || actorType != configuredType || owner is not null && owner != node)
                throw new ArgumentException("SQLite held requests must share the owner node, database and actor type.");
        }

        internal ValueTask<long> ReadClockAsync(CancellationToken token = default) => admission.ReadClockAsync(token);

        internal IProtectedRootStarts BindProtectedRootStarts(Contract.BoundScope scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (protectRootStart is not { } protect)
                throw new InvalidOperationException("Configure SQLite root start protection before binding one-way starts.");
            return BindProtectedRootStarts(scope, protect);
        }

        internal IProtectedRootStarts BindProtectedRootStarts(Contract.BoundScope scope, ProtectRootStartAsync protect) =>
            new BoundProtectedRootStarts(actorType,
                (operation, spec, token, _) => admission.StartProtectedOneWayAsync(scope, operation, spec, protect, token));

        internal Task<EventRegistrationResult> ApplyEventStartsAsync(Contract.BoundScope scope, string slot, long revision,
            IReadOnlyList<EventStartRegistration> registrations, CancellationToken token) =>
            admission.ApplyEventStartsAsync(scope.Tenant, slot, revision, registrations, token);

        internal IAuthoredStandaloneEventOutbox BindAuthoredStandaloneEvents(Contract.BoundScope scope) =>
            new BoundAuthoredStandaloneEventOutbox(
                (after, token) => admission.ReadAuthoredStandaloneEventsAsync(scope, after, token),
                (occurrence, fingerprint, token) => admission.AcknowledgeAuthoredStandaloneEventAsync(scope, occurrence, fingerprint, token));

        internal Task<IReadOnlyList<EventStartTarget>> ListEventStartsAsync(Contract.BoundScope scope, string? after, CancellationToken token) =>
            admission.ListEventStartsAsync(scope.Tenant, after, token);

        internal Task<long> ReadEventStartTailAsync(Contract.BoundScope scope, CancellationToken token) =>
            admission.ReadEventStartTailAsync(scope.Tenant, token);

        internal Task<IReadOnlyList<EventStartScanItem>> ReadEventStartPageAsync(Contract.BoundScope scope, long after, long through, int limit,
            CancellationToken token) => admission.ReadEventStartPageAsync(scope.Tenant, after, through, limit, token);

        internal Task<Contract.Admission> AdmitEventStartAsync(Contract.BoundScope scope, string target,
            Func<EventStartTarget, Contract.StartSpec> start, ProtectRootStartAsync protect, CancellationToken token) =>
            RootEventStarts.RelayAsync(target, start, ct => admission.ReadEventStartAsync(scope.Tenant, target, ct),
                (spec, ct) => admission.StartProtectedOneWayAsync(scope, target, spec, protect, ct, eventTarget: target), token);

        internal Task<int> PruneSettledEventStartsAsync(Contract.BoundScope scope, CancellationToken token) =>
            admission.PruneSettledEventStartsAsync(scope.Tenant, token);

        internal Task<bool> ConfirmEventStartRegistrationDrainedAsync(Contract.BoundScope scope, string slot, long revision,
            CancellationToken token) => admission.ConfirmEventStartRegistrationDrainedAsync(scope.Tenant, slot, revision, token);

        internal Task<RootStartProof> ConfirmHeldStartAsync(Contract.BoundScope scope, Contract.ActorRef actor, string issuance,
            CancellationToken token) => admission.ConfirmHeldStartAsync(scope, actor, issuance, token);

        internal Task<PreparedRootResponse?> ReadRetainedRootResponseAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            string issuance, string key, CancellationToken token) =>
            admission.ReadRetainedRootResponseAsync(scope, actor, issuance, key, token);

        internal Task<PreparedWaitReceipt> DeliverPreparedWaitsAsync(Contract.BoundScope scope, string operation, string sourceDigest,
            IReadOnlyList<PreparedWaitDelivery> deliveries, bool allowNew, CancellationToken token) =>
            admission.DeliverPreparedWaitsAsync(scope, operation, sourceDigest, deliveries, allowNew, token);

        internal Task<bool> HasAcceptedPreparedStartAsync(Contract.BoundScope scope, Contract.ActorRef actor, string issuance,
            CancellationToken token) => admission.HasAcceptedPreparedStartAsync(scope, actor, issuance, token);

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

        internal IDisposable AcquireReplyLease(CancellationToken cancellationToken = default) =>
            AcquireFile(path, this, cancellationToken);

        private async ValueTask<Contract.Reply> DuringLocalOwnershipAsync(Func<ValueTask<Contract.Reply>> request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (AcquireReplyLease(token)) token.ThrowIfCancellationRequested();
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

        private sealed class Admission(SqliteDirectRequests owner, string path, TimeSpan window, ProtectRootStartAsync? protectRootStart,
            bool supportsPublicEvents, bool supportsKeys) : IHeldRootAdmission
        {
            public bool SupportsEvents => SupportsPublicEvents || (owner.QualificationFormat is { } format
                ? format == StoreFormat.RootLiveEventQualification || format == StoreFormat.RootPlaybackDescendantsQualification ||
                  format == StoreFormat.RootManagedSchedulesQualification || format == StoreFormat.RootPreparedStartsQualification ||
                  format == StoreFormat.RootInlinePromotionQualification ||
                  format == StoreFormat.RootAuthoredEventsQualification || format == StoreFormat.RootEventStartsQualification ||
                  format == StoreFormat.RootEventStartReceiptsQualification || format == StoreFormat.RootEventStartScanQualification ||
                  format == StoreFormat.RootDirectTestRunQualification || format == StoreFormat.RootSharedActivationQualification ||
                  format == StoreFormat.RootKeyedStartsQualification || format is { SupportsIsolatedInlineQualification: true }
                : TakesHttpWaits);

            public Task<HeldRootAdmission> PublishAsync(HeldRootRequest request, string key, string? payload, CancellationToken token) =>
                WriteAsync(request.Tenant, RootAdmissionKind.Input, (admission, attempt, ct) =>
                    admission.PublishRequestAsync(request, key, payload, ct, attempt), token);

            public bool SupportsPublicEvents { get; } = supportsPublicEvents;

            public bool SupportsKeys { get; } = supportsKeys;

            public Task<Contract.Admission?> AdmitKeyAsync(HeldRootRequest request, Contract.Target.Key key, string kind,
                string? payload, CancellationToken token)
            {
                if (key.Spec is not null && protectRootStart is not null)
                    throw new NotSupportedException("Held caller-key starts do not support configured root-start protection.");
                return WriteAsync(request.Tenant, key.Spec is null ? RootAdmissionKind.Input : RootAdmissionKind.Start,
                    (admission, attempt, ct) => admission.AdmitKeyedRequestAsync(request, key, kind, payload, owner.actorType, ct, attempt), token);
            }

            public async ValueTask<long> ReadClockAsync(CancellationToken token)
            {
                using var file = AcquireFile(path);
                return await ForFile(file).ReadReplyTimeAsync(token);
            }

            public async Task<string?> StartAsync(HeldRootRequest request, StartSpec spec, CancellationToken token)
            {
                if (protectRootStart is null)
                    return await WriteAsync(request.Tenant, RootAdmissionKind.Start, (admission, attempt, ct) =>
                        admission.StartRequestAsync(request, spec, ct, attempt), token);
                var result = await OnWriteWorkerAsync(
                    () => StartProtectedAsync(request.Tenant, request.Caller.Id, spec, request, protectRootStart, token), token);
                return result?.Actor?.Id;
            }

            internal Task<Contract.Admission?> StartProtectedOneWayAsync(Contract.BoundScope scope, string operation, StartSpec spec,
                ProtectRootStartAsync protect, CancellationToken token, string? eventTarget = null) =>
                // SQLite's bounded synchronous commands may keep running briefly after cancellation. Await
                // the worker so the linked token stays registered and a landed COMMIT can still be reported.
                Task.Run(() => StartProtectedAsync(scope.Tenant, operation, spec, null, protect, token, eventTarget), token);

            private async Task<Contract.Admission?> StartProtectedAsync(string tenant, string operation, StartSpec spec,
                HeldRootRequest? request, ProtectRootStartAsync protect, CancellationToken token, string? eventTarget = null)
            {
                using var attempt = owner.Pressure?.Begin(tenant, RootAdmissionKind.Start);
                var prepared = await WriteWithFileAsync(RootAdmissionKind.Start, attempt,
                    (admission, ct) => admission.PrepareProtectedStartAsync(tenant, operation, spec, request, attempt, ct, eventTarget), token);
                if (prepared.Stage is not { } stage || prepared.Offer is not { } offer)
                    return Duplicate(prepared.Existing);
                var protectedStart = await RootPreparedStarts.ProtectAsync(protect, offer, token);
                if (!protectedStart)
                {
                    var (expired, duplicate) = await WriteWithFileAsync(RootAdmissionKind.Start, attempt,
                        (admission, ct) => admission.DisarmHeldStartAsync(stage, ct), token);
                    if (expired || duplicate is not null) return expired ? null : Duplicate(duplicate);
                    throw new RootAdmissionRefused(Contract.RefusalReason.TargetMismatch, TimeSpan.Zero);
                }
                token.ThrowIfCancellationRequested();
                return await WriteWithFileAsync(RootAdmissionKind.Start, attempt,
                    (admission, ct) => admission.StartProtectedAsync(tenant, operation, spec, request, ct, attempt, stage, eventTarget), token);

                static Contract.Admission? Duplicate(string? existing) => existing is null ? null :
                    new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(existing));
            }

            internal Task<EventRegistrationResult> ApplyEventStartsAsync(string tenant, string slot, long revision,
                IReadOnlyList<EventStartRegistration> registrations, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ApplyEventStartsAsync(tenant, slot, revision, registrations, ct), token), token);

            internal Task<IReadOnlyList<AuthoredStandaloneEvent>> ReadAuthoredStandaloneEventsAsync(Contract.BoundScope scope,
                AuthoredStandaloneEventCursor? after, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ReadAuthoredStandaloneEventsAsync(scope, after, ct), token), token);

            internal Task<bool> AcknowledgeAuthoredStandaloneEventAsync(Contract.BoundScope scope, string occurrence,
                string fingerprint, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.AcknowledgeAuthoredStandaloneEventAsync(scope, occurrence, fingerprint, ct), token), token);

            internal Task<IReadOnlyList<EventStartTarget>> ListEventStartsAsync(string tenant, string? after, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ListEventStartsAsync(tenant, after, ct), token), token);

            internal Task<long> ReadEventStartTailAsync(string tenant, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ReadEventStartTailAsync(tenant, ct), token), token);

            internal Task<IReadOnlyList<EventStartScanItem>> ReadEventStartPageAsync(string tenant, long after, long through, int limit,
                CancellationToken token) => OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ReadEventStartPageAsync(tenant, after, through, limit, ct), token), token);

            internal Task<(EventStartTarget? Pending, string? Settled)> ReadEventStartAsync(string tenant, string target, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ReadEventStartAsync(tenant, target, ct), token), token);

            internal Task<int> PruneSettledEventStartsAsync(string tenant, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.PruneSettledEventStartsAsync(tenant, ct), token), token);

            internal Task<bool> ConfirmEventStartRegistrationDrainedAsync(string tenant, string slot, long revision, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ConfirmEventStartRegistrationDrainedAsync(tenant, slot, revision, ct), token), token);

            internal Task<RootStartProof> ConfirmHeldStartAsync(Contract.BoundScope scope, Contract.ActorRef actor, string issuance,
                CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ConfirmHeldStartAsync(scope, actor, issuance, ct), token), token);

            internal Task<PreparedRootResponse?> ReadRetainedRootResponseAsync(Contract.BoundScope scope, Contract.ActorRef actor,
                string issuance, string key, CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.ReadRetainedRootResponseAsync(scope, actor, issuance, key, ct), token), token);

            internal Task<PreparedWaitReceipt> DeliverPreparedWaitsAsync(Contract.BoundScope scope, string operation, string sourceDigest,
                IReadOnlyList<PreparedWaitDelivery> deliveries, bool allowNew, CancellationToken token) =>
                WriteAsync(scope.Tenant, RootAdmissionKind.Input, (admission, attempt, ct) =>
                    admission.DeliverPreparedWaitsAsync(scope, operation, sourceDigest, deliveries, allowNew, attempt, ct), token);

            internal Task<bool> HasAcceptedPreparedStartAsync(Contract.BoundScope scope, Contract.ActorRef actor, string issuance,
                CancellationToken token) =>
                OnWriteWorkerAsync(() => WriteWithFileAsync(RootAdmissionKind.Start, null,
                    (admission, ct) => admission.HasAcceptedPreparedStartAsync(scope, actor, issuance, ct), token), token);

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
                CreateAdmission(file, window, owner.Pressure, owner.QualificationFormat, owner.PublicEventHost);

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
                        using var file = AcquireFile(path, owner, token);
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
