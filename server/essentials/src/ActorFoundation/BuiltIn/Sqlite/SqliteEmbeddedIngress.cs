using Kernel.Durable;
using Kernel.EntityFramework;
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

    internal static Task<Contract.IIngress> OpenFoundationWithOperatorsAsync(string databasePath, string actorType,
        Func<Contract.BoundScope, Contract.ActorRef, string, bool> authorizeOperator, SqliteDirectRequests? requests = null,
        CancellationToken cancellationToken = default) =>
        OpenFoundationCoreAsync(databasePath, actorType, requests, requests?.DeduplicationWindow, cancellationToken, authorizeOperator);

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
        var admission = new SqliteRootAdmission(() => KernelDb.ForSqliteRootShipped(file.Options), window, file.AdmissionDefinitions);
        await admission.VerifyAsync(cancellationToken);
        return new Ingress(file.Path, actorType, window, requests, authorizeOperator);
    }

    private static void RequireFoundationActorType(string actorType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        if (!FoundationMetadataLimits.FitsName(actorType))
            throw new ArgumentException("Invalid SQLite root actor type.", nameof(actorType));
    }

    private sealed class Ingress(string path, string actorType, TimeSpan window, SqliteDirectRequests? requests,
        Func<Contract.BoundScope, Contract.ActorRef, string, bool>? authorizeOperator) : Contract.IIngress
    {
        public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
            string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(kind);
            cancellationToken.ThrowIfCancellationRequested();
            if (Contract.Kinds.IsCommand(kind))
            {
                if (kind != Contract.Kinds.Terminate || target is not Contract.Target.Ref exact ||
                    !FoundationRootCreator.IsGeneratedReference(exact.Actor.Id) || authorizeOperator?.Invoke(scope, exact.Actor, kind) != true ||
                    payload is not { Length: > 0 and <= 2_048 })
                    return new(Contract.AdmissionOutcome.Refused);
                var command = RootAdmissionWire.ReadOperatorCommand(payload);
                if (command is not { Mode: Contract.RecoverMode.Resume, Handles: null, Reason: null, ThenDelete: false })
                    return new(Contract.AdmissionOutcome.Refused);
                if (command.ExpectedTarget is { } expected && expected != exact.Actor)
                    return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);
                if (command.Selection is not { Length: > 0 } selection)
                    return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
                using var write = AcquireFile(path);
                var operators = new SqliteRootAdmission(() => KernelDb.ForSqliteRootShipped(write.Options), window, write.AdmissionDefinitions);
                return await operators.TerminateAsync(scope.Tenant, exact.Actor, id, selection, options?.DueAt, options?.KeepFor, cancellationToken);
            }

            using var file = AcquireFile(path);
            var admission = new SqliteRootAdmission(() => KernelDb.ForSqliteRootShipped(file.Options), window, file.AdmissionDefinitions);
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

            if (target is not Contract.Target.Ref direct || FoundationMetadataLimits.IsReservedKind(kind))
                throw new NotSupportedException("SQLite supports ordinary input addressed to a root reference.");
            return await admission.SendAsync(scope.Tenant, direct.Actor, id, kind, payload, options?.DueAt, options?.KeepFor, cancellationToken);
        }

        public ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind, string? payload,
            Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default) =>
            requests is not null ? requests.RequestAsync(scope, target, id, kind, payload, add, request, cancellationToken)
                : throw new NotSupportedException("SQLite held requests require the Embedded owner's reply edge.");

        public ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SQLite root ingress supports starts and ordinary sends.");
    }
}
