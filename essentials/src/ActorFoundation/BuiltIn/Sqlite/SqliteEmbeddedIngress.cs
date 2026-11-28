using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public static partial class SqliteEmbeddedHosting
{
    /// <summary>Opens durable starts and ordinary sends to exact references on an installed SQLite shipped store.</summary>
    public static Task<Contract.IIngress> OpenFoundationAsync(string databasePath, string actorType,
        TimeSpan? deduplicationWindow = null, CancellationToken cancellationToken = default) =>
        OpenFoundationCoreAsync(databasePath, actorType, null, deduplicationWindow, cancellationToken);

    internal static Task<Contract.IIngress> OpenFoundationWithRequestsAsync(string databasePath, string actorType, SqliteDirectRequests requests,
        CancellationToken cancellationToken = default) =>
        OpenFoundationCoreAsync(databasePath, actorType, requests, requests.DeduplicationWindow, cancellationToken);

    /// <summary>
    /// Opens ingress with operator commands on an installed SQLite shipped store. The host authorizes each command
    /// against its bound scope and exact actor reference.
    /// </summary>
    public static Task<Contract.IIngress> OpenFoundationWithOperatorsAsync(string databasePath, string actorType,
        Func<Contract.BoundScope, Contract.ActorRef, string, bool> authorizeOperator, TimeSpan? deduplicationWindow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizeOperator);
        return OpenFoundationCoreAsync(databasePath, actorType, null, deduplicationWindow, cancellationToken, authorizeOperator);
    }

    internal static async Task<Contract.IIngress> OpenFoundationWithOperatorsAsync(string databasePath, string actorType,
        Func<Contract.BoundScope, Contract.ActorRef, string, bool> authorizeOperator, SqliteDirectRequests requests,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await OpenFoundationCoreAsync(databasePath, actorType, requests, requests.DeduplicationWindow,
                cancellationToken, authorizeOperator);
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("SQLite operator ingress could not acquire its verification lock.", error);
        }
    }

    internal static async ValueTask<Contract.Admission> AdmitRetentionDeleteAsync(string databasePath, Contract.BoundScope scope,
        Contract.ActorRef actor, string selection, TimeSpan deduplicationWindow, CancellationToken token, StoreFormat? format = null)
    {
        try
        {
            using var file = AcquireFile(databasePath);
            var admission = new SqliteRootAdmission(() => KernelDb.ForSqliteRootShipped(file.Options, format), deduplicationWindow,
                file.AdmissionDefinitions);
            return await admission.RetentionDeleteAsync(scope.Tenant, actor, selection, token);
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
        {
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("SQLite retention admission could not acquire its write lock.", error);
        }
    }

    internal static async ValueTask<Contract.ScheduleControlResult> ApplyScheduleControlAsync(string path, StoreFormat? format,
        string tenant, string scheduleActorType, ScheduleControlIntent intent, CancellationToken token)
    {
        try
        {
            using var file = AcquireFile(path);
            var admission = new SqliteRootAdmission(() => KernelDb.ForSqliteRootShipped(file.Options, format), TimeSpan.Zero,
                file.AdmissionDefinitions, scheduleActorType: scheduleActorType);
            return await admission.ApplyScheduleControlAsync(tenant, intent, token);
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
        {
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("SQLite schedule control could not acquire its write lock.", error);
        }
    }

    private static async Task<Contract.IIngress> OpenFoundationCoreAsync(string databasePath, string actorType, SqliteDirectRequests? requests,
        TimeSpan? deduplicationWindow, CancellationToken cancellationToken,
        Func<Contract.BoundScope, Contract.ActorRef, string, bool>? authorizeOperator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        RequireFoundationActorType(actorType);
        var window = deduplicationWindow ?? TimeSpan.FromHours(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        using var file = AcquireFile(databasePath, requests: requests);
        requests?.RequireBinding(file.Path, actorType);
        var format = requests?.QualificationFormat;
        var eventHost = requests?.PublicEventHost ?? (format is null ? new(actorType, false) : null);
        var pressure = requests?.Pressure;
        bool publicEvents;
        using (var db = KernelDb.ForSqliteRootShipped(file.Options, format))
        {
            publicEvents = db.UsesRootPublicEventStarts;
            if (db.UsesRootDueBacklog) pressure ??= RootAdmissionPressure.CreateDefault();
        }
        var admission = CreateAdmission(file, window, pressure, format, eventHost);
        await admission.VerifyAsync(cancellationToken);
        var path = file.Path;
        if (pressure is not null && requests?.Pressure is null)
        {
            await ReadPressureAsync(cancellationToken);
            pressure.RefreshOnDemand(ReadPressureAsync);
        }
        return new Ingress(path, actorType, window, requests, pressure, eventHost, authorizeOperator, publicEvents);

        async Task ReadPressureAsync(CancellationToken token)
        {
            using var lease = AcquireFile(path);
            var store = new EfStore(() => KernelDb.ForSqliteRootRuntime(lease.Options, lease.AdmissionDefinitions, qualificationFormat: format), postgres: false)
                { AdmissionPressure = pressure };
            await store.PrimeAdmissionPressureAsync(token);
        }
    }

    private static void RequireFoundationActorType(string actorType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        if (!FoundationMetadataLimits.FitsName(actorType))
            throw new ArgumentException("Invalid SQLite root actor type.", nameof(actorType));
    }

    private static SqliteRootAdmission CreateAdmission(FileLease file, TimeSpan window, RootAdmissionPressure? pressure, StoreFormat? format,
        RootPublicEventHost? publicEventHost = null) =>
        new(() => KernelDb.ForSqliteRootShipped(file.Options, format, publicEventHost), window, file.AdmissionDefinitions, pressure);

    private sealed class Ingress(string path, string actorType, TimeSpan window, SqliteDirectRequests? requests, RootAdmissionPressure? pressure,
        RootPublicEventHost? eventHost, Func<Contract.BoundScope, Contract.ActorRef, string, bool>? authorizeOperator, bool publicEvents) : Contract.IIngress
    {
        public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
            string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
        {
            try { return await SendCoreAsync(scope, target, id, kind, payload, options, cancellationToken); }
            catch (RootAdmissionRefused refusal) { return refusal.Admission; }
            catch (SqliteException error) when ((kind == Contract.Kinds.Recover || target is Contract.Target.Event) && error.SqliteErrorCode is 5 or 6)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.NodeBusy, RetryAfter: TimeSpan.FromSeconds(1));
            }
        }

        private async ValueTask<Contract.Admission> SendCoreAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
            string? payload, Contract.AddOptions? options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(kind);
            cancellationToken.ThrowIfCancellationRequested();
            var format = requests?.QualificationFormat;
            if (target is Contract.Target.Event publish && publicEvents)
            {
                using var writer = AcquireFile(path);
                var publications = CreateAdmission(writer, window, pressure, format, eventHost);
                return await publications.PublishPublicEventAsync(scope.Tenant, publish.CorrelationKey, id, payload, options, cancellationToken);
            }
            if (Contract.Kinds.IsCommand(kind))
            {
                var (actor, command, refusal) = ReadCommand(scope, target, kind, payload);
                if (command is null) return new(Contract.AdmissionOutcome.Refused, Reason: refusal);
                using var write = AcquireFile(path);
                var operators = CreateAdmission(write, window, pressure, format, eventHost);
                return await operators.OperateAsync(scope.Tenant, actor, id, kind, command.Selection!, options?.DueAt, options?.KeepFor,
                    cancellationToken, thenDelete: command.ThenDelete);
            }

            using var file = AcquireFile(path);
            var admission = CreateAdmission(file, window, pressure, format, eventHost);
            if (target is Contract.Target.Start start)
            {
                var spec = start.Spec;
                if (kind != Contract.Kinds.Start || payload is not null || spec.Type != actorType || spec.Ephemeral ||
                    !Enum.IsDefined(spec.History))
                    throw new NotSupportedException("SQLite starts durable roots of its configured actor type.");
                if (!FoundationMetadataLimits.IsOutsideMessageId(id)) return new(Contract.AdmissionOutcome.Refused);
                try
                {
                    return await admission.StartAsync(scope.Tenant, id, new(spec.Type, spec.TypeData, spec.Input, History: (History)spec.History),
                        options?.DueAt, options?.KeepFor, cancellationToken);
                }
                catch (MetadataLimitExceeded)
                {
                    return new(Contract.AdmissionOutcome.Refused);
                }
            }

            if (target is Contract.Target.Key keyed)
                return await admission.AdmitKeyedAsync(scope.Tenant, keyed, id, kind, payload, options?.DueAt, options?.KeepFor,
                    actorType, cancellationToken);
            if (target is not Contract.Target.Ref direct || FoundationMetadataLimits.IsReservedKind(kind))
                throw new NotSupportedException("SQLite supports ordinary input addressed to a root reference.");
            return await admission.SendAsync(scope.Tenant, direct.Actor, id, kind, payload, options?.DueAt, options?.KeepFor, cancellationToken);
        }

        public ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind, string? payload,
            Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default)
        {
            if (requests is null) throw new NotSupportedException("SQLite held requests require the Embedded owner's reply edge.");
            if (target is Contract.Target.Event or Contract.Target.Key || !Contract.Kinds.IsCommand(kind))
                return requests.RequestAsync(scope, target, id, kind, payload, add, request, cancellationToken);

            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (kind == Contract.Kinds.Recover)
                return ValueTask.FromResult(new Contract.Reply(null, null, Error: "missing"));
            if (request.Inline || request.ReleaseBeforeCommit || request.Ambient is not null)
                return ValueTask.FromResult(new Contract.Reply(null, null, Error: "invalid operator request"));
            if (add?.DueAt is not null)
                return ValueTask.FromResult(new Contract.Reply(null, null, Error: "requests cannot set a due time"));
            var (actor, command, refusal) = ReadCommand(scope, target, kind, payload);
            return command is not null ? requests.RequestOperatorAsync(scope, actor, id, kind, command, add?.KeepFor, request, cancellationToken)
                : ValueTask.FromResult(new Contract.Reply(null, null, Error: refusal switch
                {
                    Contract.RefusalReason.StaleSelection => "stale selection",
                    Contract.RefusalReason.TargetMismatch => "target",
                    _ => "missing"
                }));
        }

        private (Contract.ActorRef Actor, Contract.OperatorCommand? Command, Contract.RefusalReason? Refusal) ReadCommand(
            Contract.BoundScope scope, Contract.Target target, string kind, string? payload)
        {
            if (kind is not (Contract.Kinds.Terminate or Contract.Kinds.Delete or Contract.Kinds.Recover) || target is not Contract.Target.Ref exact ||
                !FoundationRootCreator.IsGeneratedReference(exact.Actor.Id) || authorizeOperator?.Invoke(scope, exact.Actor, kind) != true ||
                payload is not { Length: > 0 and <= 2_048 })
                return default;
            var command = RootAdmissionWire.ReadOperatorCommand(payload);
            if (command is not { Mode: Contract.RecoverMode.Resume, Handles: null, Reason: null }) return default;
            if (command.ExpectedTarget is { } expected && expected != exact.Actor)
                return (exact.Actor, null, Contract.RefusalReason.TargetMismatch);
            if (command.ThenDelete && (kind != Contract.Kinds.Terminate || command.ExpectedTarget != exact.Actor ||
                authorizeOperator?.Invoke(scope, exact.Actor, Contract.Kinds.Delete) != true)) return default;
            return command.Selection is { Length: > 0 } ? (exact.Actor, command, null)
                : (exact.Actor, null, Contract.RefusalReason.StaleSelection);
        }

        public async ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scope);
            cancellationToken.ThrowIfCancellationRequested();
            RootPublicEventStarts.Validate(actorType, key, wait, spec);
            using var file = AcquireFile(path);
            var admission = CreateAdmission(file, window, pressure, requests?.QualificationFormat, eventHost);
            await admission.SetPublicEventStartAsync(scope.Tenant, key, wait, spec, cancellationToken);
        }
    }
}
