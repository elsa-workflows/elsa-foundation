using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed class PostgresRootIngress(PostgresRootAdmission admission, string actorType,
    Func<Contract.BoundScope, Contract.ActorRef, bool>? authorizeTerminate = null,
    Func<Contract.BoundScope, Contract.ActorRef, bool>? authorizeDelete = null,
    (Requests Requests, string Node)? heldOperators = null) : Contract.IIngress
{
    private static readonly JsonSerializerOptions OperatorJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };

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
            return target is Contract.Target.Ref exact && Authorizes(scope, exact.Actor, kind)
                ? await OperateAsync(scope, exact.Actor, id, kind, payload, options, cancellationToken)
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

    private bool Authorizes(Contract.BoundScope scope, Contract.ActorRef actor, string kind) =>
        PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) && (kind switch
        {
            Contract.Kinds.Terminate => authorizeTerminate,
            Contract.Kinds.Delete => authorizeDelete,
            _ => null
        })?.Invoke(scope, actor) == true;

    private async ValueTask<Contract.Admission> OperateAsync(Contract.BoundScope scope, Contract.ActorRef actor, string id,
        string kind, string? payload, Contract.AddOptions? options, CancellationToken token, PostgresRootAdmission.OperatorReply? reply = null)
    {
        if (!FoundationMetadataLimits.IsOutsideMessageId(id) || payload is null || payload.Length > 2_048)
            return new(Contract.AdmissionOutcome.Refused);

        Contract.OperatorCommand? command;
        try { command = JsonSerializer.Deserialize<Contract.OperatorCommand>(payload, OperatorJson); }
        catch (JsonException) { return new(Contract.AdmissionOutcome.Refused); }

        if (command is not { Mode: Contract.RecoverMode.Resume, Handles: null, Reason: null })
            return new(Contract.AdmissionOutcome.Refused);
        if (command.ExpectedTarget is { } selected && selected != actor)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);
        if (command.ThenDelete && (kind != Contract.Kinds.Terminate || command.ExpectedTarget != actor ||
            authorizeDelete?.Invoke(scope, actor) != true))
            return new(Contract.AdmissionOutcome.Refused);
        if (command.Selection is not { Length: > 0 } selection)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
        var receipt = await admission.AdmitRootOperatorAsync(scope.Tenant, actor.Id, id, kind,
            options?.DueAt, options?.KeepFor, token, selection, command.ThenDelete, reply: reply);
        return receipt switch
        {
            { Duplicate: true } => new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(receipt.Reference)),
            { Duplicate: false } => new(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(receipt.Reference)),
            _ => new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection)
        };
    }

    public async ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
        string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default)
    {
        if (heldOperators is not { } host)
            throw new NotSupportedException("This generated-root profile has no held operator requests.");
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Inline || request.ReleaseBeforeCommit || request.Ambient is not null) return new(null, null, Error: "invalid operator request");
        if (add?.DueAt is not null) return new(null, null, Error: "requests cannot set a due time");
        if (target is not Contract.Target.Ref exact || !Authorizes(scope, exact.Actor, kind))
            return new(null, null, Error: "missing");

        var started = Stopwatch.GetTimestamp();
        var now = await admission.ReadReplyTimeAsync(cancellationToken);
        double Remaining() => (double)((decimal)request.Deadline - now) - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var remaining = Remaining();
        if (remaining <= 0) return new(null, null, Error: "timeout");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (remaining <= int.MaxValue) lifetime.CancelAfter(TimeSpan.FromMilliseconds(remaining));
        var open = host.Requests.OpenBound(id, request.Deadline, scope.Tenant, null);
        try
        {
            var route = new PostgresRootAdmission.OperatorReply(open.ReplyAddress(host.Node), open.Deadline);
            var accepted = await OperateAsync(scope, exact.Actor, id, kind, payload, add, lifetime.Token, route);
            if (Remaining() <= 0) return new(null, null, Error: "timeout");
            if (accepted.Outcome == Contract.AdmissionOutcome.Duplicate) return new(null, null, Error: "repeat");
            if (accepted.Outcome == Contract.AdmissionOutcome.Refused)
                return new(null, null, Error: accepted.Reason switch
                {
                    Contract.RefusalReason.StaleSelection => "stale selection",
                    Contract.RefusalReason.TargetMismatch => "target",
                    _ => "missing"
                });

            var completion = host.Requests.CompletionOf(open);
            while (!completion.IsCompleted && (remaining = Remaining()) > 0)
            {
                try { await completion.WaitAsync(TimeSpan.FromMilliseconds(Math.Min(remaining, int.MaxValue)), lifetime.Token); }
                catch (TimeoutException) { }
            }
            if (!completion.IsCompleted) host.Requests.Timeout(open);
            var answer = await completion.WaitAsync(cancellationToken);
            if (answer.Item is CreationResult refusal)
                return new(null, refusal.OwningOperation, Error: refusal.Result switch
                {
                    "Protected" => "protected",
                    "ProtectionUnavailable" => "protection unavailable",
                    "StaleSelection" => "stale selection",
                    "TargetMismatch" => "target",
                    _ => "missing"
                });
            return new(answer.Channel, answer.Item, (Contract.ActorStatus?)answer.Status, answer.Error);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new(null, null, Error: "timeout");
        }
        catch (PostgresRootAdmission.OperatorReplyExpired)
        {
            return new(null, null, Error: "timeout");
        }
        finally { host.Requests.Disconnect(open); }
    }

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
        Func<Contract.BoundScope, Contract.ActorRef, bool> authorizeTerminate, CancellationToken cancellationToken,
        Func<Contract.BoundScope, Contract.ActorRef, bool>? authorizeDelete = null, (Requests Requests, string Node)? heldOperators = null)
    {
        ArgumentNullException.ThrowIfNull(authorizeTerminate);
        PostgresRootAdmission.RequireActorType(actorType);
        ArgumentOutOfRangeException.ThrowIfLessThan(deduplicationWindow, TimeSpan.Zero);
        if (!postgres || Engine is not null) throw new NotSupportedException("Selected root ingress requires PostgreSQL.");
        await using var db = create();
        if (!db.Database.IsNpgsql() || !db.UsesRootTerminalSelection)
            throw new NotSupportedException("Selected root ingress requires the PostgreSQL selection profile.");
        await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
        if (authorizeDelete is not null && !db.UsesRootRetirement)
            throw new NotSupportedException("Selected deletion requires the PostgreSQL retirement profile.");
        if (heldOperators is { } host)
        {
            if (!db.UsesRootOperatorReplies) throw new NotSupportedException("Held operators require the PostgreSQL reply profile.");
            ArgumentNullException.ThrowIfNull(host.Requests);
            if (string.IsNullOrWhiteSpace(host.Node) || host.Node.Length > 256 || host.Node.Contains('|') ||
                host.Node.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(host.Node))
                throw new ArgumentException("Expected an issued reply node.", nameof(heldOperators));
        }
        return new PostgresRootIngress(new(create, deduplicationWindow, lockOrdinaryWrites: true), actorType,
            authorizeTerminate, authorizeDelete, heldOperators);
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
