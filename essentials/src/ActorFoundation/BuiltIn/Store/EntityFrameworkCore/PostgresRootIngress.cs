using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed class PostgresRootIngress(PostgresRootAdmission admission, string actorType,
    Func<Contract.BoundScope, Contract.ActorRef, bool>? authorizeTerminate = null) : Contract.IIngress
{
    private static readonly JsonSerializerOptions OperatorJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
        string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(kind);
        if (Contract.Kinds.IsCommand(kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return kind == Contract.Kinds.Terminate && target is Contract.Target.Ref exact &&
                authorizeTerminate?.Invoke(scope, exact.Actor) == true
                ? await TerminateAsync(scope, exact.Actor, id, payload, options, cancellationToken)
                : new(Contract.AdmissionOutcome.Refused);
        }
        if (target is Contract.Target.Start start)
        {
            var spec = start.Spec;
            if (kind != Contract.Kinds.Start || payload is not null || spec.Type != actorType || spec.Ephemeral ||
                !Enum.IsDefined(spec.History))
                throw new NotSupportedException("The preview starts durable roots of its configured actor type.");
            if (!FoundationMetadataLimits.IsOutsideMessageId(id))
                return new(Contract.AdmissionOutcome.Refused);
            try
            {
                var accepted = await admission.AdmitAsync(scope.Tenant, id,
                    new(spec.Type, spec.TypeData, spec.Input, History: (History)spec.History),
                    options?.DueAt, options?.KeepFor, cancellationToken);
                return new(accepted.Duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted,
                    new Contract.ActorRef(accepted.Reference));
            }
            catch (MetadataLimitExceeded)
            {
                return new(Contract.AdmissionOutcome.Refused);
            }
        }
        if (target is not Contract.Target.Ref direct || FoundationMetadataLimits.IsReservedKind(kind))
            throw new NotSupportedException("The preview supports ordinary input addressed to a root reference.");
        var admitted = await admission.AdmitDirectResultAsync(scope.Tenant, direct.Actor.Id, id, kind, payload,
            options?.DueAt, options?.KeepFor, cancellationToken);
        return admitted switch
        {
            PostgresRootAdmission.DirectResult.Accepted => new(Contract.AdmissionOutcome.Accepted, direct.Actor),
            PostgresRootAdmission.DirectResult.Duplicate => new(Contract.AdmissionOutcome.Duplicate, direct.Actor),
            PostgresRootAdmission.DirectResult.MissingTarget => new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch),
            _ => new(Contract.AdmissionOutcome.Refused)
        };
    }

    private async ValueTask<Contract.Admission> TerminateAsync(Contract.BoundScope scope, Contract.ActorRef actor, string id,
        string? payload, Contract.AddOptions? options, CancellationToken token)
    {
        if (!PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) || !FoundationMetadataLimits.IsOutsideMessageId(id) ||
            payload is null || payload.Length > 2_048)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);

        Contract.OperatorCommand? command;
        try { command = JsonSerializer.Deserialize<Contract.OperatorCommand>(payload, OperatorJson); }
        catch (JsonException) { return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection); }

        if (command is not { Selection.Length: > 0, ThenDelete: false, Mode: Contract.RecoverMode.Resume, Handles: null, Reason: null } ||
            command.ExpectedTarget is { } selected && selected != actor)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
        var receipt = await admission.AdmitRootOperatorAsync(scope.Tenant, actor.Id, id, Contract.Kinds.Terminate,
            options?.DueAt, options?.KeepFor, token, command.Selection);
        return receipt switch
        {
            { Duplicate: true } => new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(receipt.Reference)),
            { Duplicate: false } => new(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(receipt.Reference)),
            _ => new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection)
        };
    }

    public ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
        string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Held requests are outside the ordinary generated-root preview.");

    public ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Event starts are outside this preview.");
}

public sealed partial class EfStore
{
    /// <summary>
    /// Installs the ordinary generated-root preview on an initialized PostgreSQL store.
    /// Run during setup or with existing owners and callers stopped. Retains issued references and bookmarks.
    /// </summary>
    public async Task InstallPreviewRootSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!postgres || Engine is not null)
            throw new NotSupportedException("Generated-root preview installation requires PostgreSQL.");

        await using var db = create();
        if (!db.Database.IsNpgsql() || db.RequiredFormat != StoreFormat.Current)
            throw new NotSupportedException("Installation requires the ordinary PostgreSQL generated-root preview.");
        await PostgresRootAdmission.InstallAsync(db, cancellationToken, exclusiveFormat: true);
    }

    // The host authenticates the caller and authorizes termination of the supplied exact reference.
    internal async ValueTask<Contract.IIngress> OpenSelectedRootIngressAsync(string actorType, TimeSpan deduplicationWindow,
        Func<Contract.BoundScope, Contract.ActorRef, bool> authorizeTerminate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizeTerminate);
        PostgresRootAdmission.RequireActorType(actorType);
        ArgumentOutOfRangeException.ThrowIfLessThan(deduplicationWindow, TimeSpan.Zero);
        if (!postgres || Engine is not null) throw new NotSupportedException("Selected root ingress requires PostgreSQL.");
        await using var db = create();
        if (!db.Database.IsNpgsql() || !db.UsesRootTerminalSelection)
            throw new NotSupportedException("Selected root ingress requires the PostgreSQL selection profile.");
        await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
        return new PostgresRootIngress(new(create, deduplicationWindow, lockOrdinaryWrites: true), actorType, authorizeTerminate);
    }

    /// <summary>
    /// Opens durable starts of the configured type and ordinary sends to exact PostgreSQL root references.
    /// The trusted host supplies each BoundScope and installs the ordinary generated-root store before opening it.
    /// </summary>
    public async ValueTask<Contract.IIngress> OpenPreviewRootIngressAsync(string actorType, TimeSpan deduplicationWindow,
        CancellationToken cancellationToken = default)
    {
        PostgresRootAdmission.RequireActorType(actorType);
        ArgumentOutOfRangeException.ThrowIfLessThan(deduplicationWindow, TimeSpan.Zero);
        if (!postgres || Engine is not null)
            throw new NotSupportedException("Generated-root preview ingress requires PostgreSQL.");

        await using var db = create();
        if (!db.Database.IsNpgsql() || db.RequiredFormat != StoreFormat.Current)
            throw new NotSupportedException("Ingress requires the ordinary PostgreSQL generated-root preview.");
        await PostgresRootAdmission.OpenJournalCursorAsync(db, cancellationToken);
        return new PostgresRootIngress(new(create, deduplicationWindow, lockOrdinaryWrites: true), actorType);
    }
}
