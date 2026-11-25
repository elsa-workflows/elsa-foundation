using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed class PostgresRootIngress(PostgresRootAdmission admission, string actorType,
    Func<Contract.BoundScope, Contract.ActorRef, bool>? authorizeTerminate = null,
    Func<Contract.BoundScope, Contract.ActorRef, bool>? authorizeDelete = null,
    (Requests Requests, string Node, Func<long> Clock)? heldOperators = null,
    Func<Contract.BoundScope, Contract.ActorRef, bool>? authorizeRecover = null) : Contract.IIngress
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
            Contract.Kinds.Recover => authorizeRecover,
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

        if (command is not { Mode: Contract.RecoverMode.Resume, Handles: null, Reason: null } ||
            kind == Contract.Kinds.Recover && options?.DueAt is not null)
            return new(Contract.AdmissionOutcome.Refused);
        if (command.ExpectedTarget is { } selected && selected != actor)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);
        if (command.ThenDelete && (kind != Contract.Kinds.Terminate || command.ExpectedTarget != actor ||
            authorizeDelete?.Invoke(scope, actor) != true))
            return new(Contract.AdmissionOutcome.Refused);
        if (command.Selection is not { Length: > 0 } selection)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
        PostgresRootAdmission.Receipt? receipt;
        try
        {
            receipt = await admission.AdmitRootOperatorAsync(scope.Tenant, actor.Id, id, kind,
                options?.DueAt, options?.KeepFor, token, selection, command.ThenDelete, reply: reply);
        }
        catch (PostgresRootAdmission.OperatorReplyCapacity)
        {
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.NodeBusy);
        }
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

        long Remaining()
        {
            var now = host.Clock();
            return request.Deadline > now ? request.Deadline - now : 0;
        }
        if (Remaining() == 0) return new(null, null, Error: "timeout");

        var open = host.Requests.OpenBound(id, request.Deadline, scope.Tenant, null);
        var completion = host.Requests.CompletionOf(open);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var initialWait = Remaining();
        if (initialWait <= int.MaxValue) lifetime.CancelAfter(TimeSpan.FromMilliseconds(initialWait));
        var route = new PostgresRootAdmission.OperatorReply(open.ReplyAddress(host.Node), open.Deadline);
        var admissionTask = OperateAsync(scope, exact.Actor, id, kind, payload, add, lifetime.Token, route).AsTask();
        var admitted = false;
        try
        {
            await WaitUntilDeadlineAsync(Task.WhenAny(admissionTask, completion));
            if (completion.IsCompleted)
            {
                admitted = admissionTask.IsCompletedSuccessfully && admissionTask.Result.Outcome == Contract.AdmissionOutcome.Accepted;
                return await PresentAsync(await completion);
            }
            if (!admissionTask.IsCompleted) return await TimeoutAsync();

            var accepted = await admissionTask;
            admitted = accepted.Outcome == Contract.AdmissionOutcome.Accepted;
            if (Remaining() == 0) return await TimeoutAsync();
            if (accepted.Outcome == Contract.AdmissionOutcome.Duplicate) return new(null, null, Error: "repeat");
            if (accepted.Outcome == Contract.AdmissionOutcome.Refused)
                return new(null, null, Error: accepted.Reason switch
                {
                    Contract.RefusalReason.StaleSelection => "stale selection",
                    Contract.RefusalReason.TargetMismatch => "target",
                    Contract.RefusalReason.NodeBusy => "operator reply capacity",
                    _ => "missing"
                });

            await WaitUntilDeadlineAsync(completion);
            return completion.IsCompleted ? await PresentAsync(await completion) : await TimeoutAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return await TimeoutAsync();
        }
        catch (PostgresRootAdmission.OperatorReplyExpired)
        {
            return await TimeoutAsync();
        }
        finally
        {
            host.Requests.Disconnect(open);
            if (admissionTask.IsCompleted)
            {
                lifetime.Dispose();
                ObserveFault(admissionTask);
            }
            else
            {
                // Cancel and dispose when the provider has unwound, independently of the settled caller.
                _ = StopAdmissionAsync(lifetime, admissionTask);
            }
        }

        async Task WaitUntilDeadlineAsync(Task signal)
        {
            while (!signal.IsCompleted && Remaining() is var timeLeft and > 0)
            {
                try { await signal.WaitAsync(TimeSpan.FromMilliseconds(Math.Min(timeLeft, int.MaxValue)), cancellationToken); }
                catch (TimeoutException) { }
            }
        }

        async ValueTask<Contract.Reply> TimeoutAsync()
        {
            host.Requests.Timeout(open);
            return await PresentAsync(await completion);
        }

        async ValueTask<Contract.Reply> PresentAsync(Reply answer)
        {
            if (answer.Error == "timeout" && admitted)
            {
                // Status is supplementary to the settled timeout and has its own short I/O budget.
                var allowance = TimeSpan.FromMilliseconds(100);
                var reading = ReadStatusAsync(allowance);
                try
                {
                    var status = await reading.WaitAsync(allowance, cancellationToken);
                    return new(null, null, (Contract.ActorStatus?)status, answer.Error);
                }
                catch (TimeoutException) { }
                finally { ObserveFault(reading); }
            }
            if (answer.Item is CreationResult refusal)
                return new(null, refusal.OwningOperation, Error: RefusalError(refusal.Result));
            return new(answer.Channel, answer.Item, (Contract.ActorStatus?)answer.Status, answer.Error);
        }

        async Task<InstanceStatus?> ReadStatusAsync(TimeSpan allowance)
        {
            using var observation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            observation.CancelAfter(allowance);
            try { return await admission.ReadOperatorTimeoutStatusAsync(scope.Tenant, exact.Actor.Id, observation.Token); }
            catch (OperationCanceledException) when (observation.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { return null; }
            catch (Exception error) when ((error is NpgsqlException or StoreFormatException or TimeoutException) &&
                !cancellationToken.IsCancellationRequested) { return null; }
        }
    }

    private static async Task StopAdmissionAsync(CancellationTokenSource lifetime, Task admissionTask)
    {
        using (lifetime)
        {
            try { await Task.WhenAll(lifetime.CancelAsync(), admissionTask); }
            catch (Exception)
            {
                // Any late provider or cancellation failure belongs to the completed request.
            }
        }
    }

    private static void ObserveFault(Task task)
    {
        if (!task.IsCompletedSuccessfully)
            _ = task.ContinueWith(static failed => _ = failed.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Event starts are outside this preview.");

    // The error an operator caller sees when the owner refused its held command.
    internal static string RefusalError(string result) => result switch
    {
        "Protected" => "protected",
        "ProtectionUnavailable" => "protection unavailable",
        "StaleSelection" => "stale selection",
        "TargetMismatch" => "target",
        "Parked" => "parked",
        _ => "missing"
    };
}

public sealed partial class EfStore
{
    /// <summary>
    /// Installs the ordinary or Native job generated-root preview on an initialized PostgreSQL store.
    /// Run during setup or with existing owners and callers stopped. Retains issued references and bookmarks.
    /// </summary>
    public async Task InstallPreviewRootSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!postgres || Engine is not null)
            throw new NotSupportedException("Generated-root preview installation requires PostgreSQL.");

        await using var db = create();
        if (!db.Database.IsNpgsql() || !db.SupportsCorePreview)
            throw new NotSupportedException("Installation requires the ordinary or Native job PostgreSQL Core Preview format.");
        await PostgresRootAdmission.InstallAsync(db, cancellationToken, exclusiveFormat: true);
    }

    // The host authenticates the caller and authorizes termination of the supplied exact reference.
    internal async ValueTask<Contract.IIngress> OpenSelectedRootIngressAsync(string actorType, TimeSpan deduplicationWindow,
        Func<Contract.BoundScope, Contract.ActorRef, bool> authorizeTerminate, CancellationToken cancellationToken,
        Func<Contract.BoundScope, Contract.ActorRef, bool>? authorizeDelete = null, (Requests Requests, string Node, Func<long> Clock)? heldOperators = null)
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
            ArgumentNullException.ThrowIfNull(host.Clock);
            if (string.IsNullOrWhiteSpace(host.Node) || host.Node.Length > 256 || host.Node.Contains('|') ||
                host.Node.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(host.Node))
                throw new ArgumentException("Expected an issued reply node.", nameof(heldOperators));
        }
        return new PostgresRootIngress(new(create, deduplicationWindow, lockOrdinaryWrites: true), actorType,
            authorizeTerminate, authorizeDelete, heldOperators);
    }

    /// <summary>
    /// Opens durable starts of the configured type and ordinary sends to exact PostgreSQL root references.
    /// The trusted host supplies each BoundScope and installs the selected generated-root store before opening it.
    /// </summary>
    public async ValueTask<Contract.IIngress> OpenPreviewRootIngressAsync(string actorType, TimeSpan deduplicationWindow,
        CancellationToken cancellationToken = default)
    {
        PostgresRootAdmission.RequireActorType(actorType);
        ArgumentOutOfRangeException.ThrowIfLessThan(deduplicationWindow, TimeSpan.Zero);
        if (!postgres || Engine is not null)
            throw new NotSupportedException("Generated-root preview ingress requires PostgreSQL.");

        await using var db = create();
        if (!db.Database.IsNpgsql() || !db.SupportsCorePreview)
            throw new NotSupportedException("Ingress requires the ordinary or Native job PostgreSQL Core Preview format.");
        await PostgresRootAdmission.RequireIngressIssuerAsync(db, cancellationToken);
        return new PostgresRootIngress(new(create, deduplicationWindow, lockOrdinaryWrites: true), actorType);
    }
}
