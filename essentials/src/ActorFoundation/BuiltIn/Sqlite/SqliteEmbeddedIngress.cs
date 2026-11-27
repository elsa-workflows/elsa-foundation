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
        var pressure = requests?.Pressure;
        using (var db = KernelDb.ForSqliteRootShipped(file.Options, format))
            if (db.UsesRootDueBacklog) pressure ??= RootAdmissionPressure.CreateDefault();
        var admission = CreateAdmission(file, window, pressure, format, requests?.PublicEventHost);
        await admission.VerifyAsync(cancellationToken);
        var path = file.Path;
        if (pressure is not null && requests?.Pressure is null)
        {
            await ReadPressureAsync(cancellationToken);
            pressure.RefreshOnDemand(ReadPressureAsync);
        }
        return new Ingress(path, actorType, window, requests, pressure, authorizeOperator);

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
        Func<Contract.BoundScope, Contract.ActorRef, string, bool>? authorizeOperator) : Contract.IIngress
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
            if (target is Contract.Target.Event publish && (format == StoreFormat.RootPublicEventStartsQualification ||
                format == StoreFormat.RootPublicEventHostQualification ||
                format == StoreFormat.RootPagedPublicEventsQualification ||
