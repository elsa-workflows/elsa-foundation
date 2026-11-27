using System.Runtime.CompilerServices;
using Kernel.Foundation.Hosting;
using Contract = Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal ProtectChildBirthsAsync? ProtectChildBirths { get; init; }
    private byte[]? _rootChildIssuanceKey;
    private bool _takesAttachedTestRuns;
    private Func<string, CreationAddress, string>? _rootChildReference;
    private Func<string, string>? _rootChildMessageHandle;
    private const string RootChildNoticePrefix = "!kernel-child-notice/1:";
    private const string RootChildInputPrefix = "!kernel-child-input/1:";
    private sealed record StoredChildNotice(string Id, string Payload, bool Final);
    private sealed record StoredChildInput(string Id, string Kind, string? Payload);

    internal async Task EnableRootChildrenAsync(CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootChildren || ProtectChildBirths is not null && !db.UsesRootRetirement)
        {
            if (ProtectChildBirths is not null)
                throw new NotSupportedException("Child birth protection requires a profile with owned child births and retirement.");
            return;
        }
        byte[] key;
        if (db.UsesSqliteRootRuntime)
        {
            await RequireSqliteOperationalFormatAsync(db, token);
            key = await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token);
        }
        else
        {
            await using var pinned = await BeginPinnedRootOperationAsync(db, token);
            if (pinned is null) await RequireStoreVersionsAsync(db, token);
            key = await PostgresRootAdmission.ReadRootIssuerKeyAsync(db, token);
        }
        if (ProtectChildBirths is not null) _rootChildIssuanceKey = key;
        _takesAttachedTestRuns = db.UsesRootAttachedTestRuns;
        _rootChildReference = (tenant, address) => FoundationRootCreator.ReferenceFor(key, tenant, address);
        _rootChildMessageHandle = seed => FoundationSelfSend.Handle(key, "child-" + seed);
    }

    private sealed record RootChildStart(string Reference, Message Creator);

    private sealed record RootChildEffects(string ParentReference, string? Children, RootChildStart[] Starts, Message[] Inputs, Message? Notice,
        IReadOnlyList<PendingChildBirth>? PendingBirths)
    {
        internal RootChildCascade? Cascade { get; init; }
        internal bool BirthsProtected { private get; set; }

        internal async Task<bool> BindAsync(KernelDb db, string tenant, CancellationToken token)
        {
            if (!db.UsesRootChildren || PendingBirths is not null && !BirthsProtected) return false;
            if (Starts.Length != 0)
            {
                if (!await BindRootChildStartsAsync(db, tenant, Starts, token)) return false;
                if (db.UsesRootScheduleLineage && !await RootScheduleLineage.BindChildrenAsync(db, tenant, ParentReference,
                    Starts.Select(start => start.Reference).ToArray(), token)) return false;
            }
            await BindRootChildInputsAsync(db, tenant, Inputs, token);
            if (Cascade is { } cascade && !await OpenRootChildCascadeAsync(db, cascade, token)) return false;
            if (Notice is not { } notice) return true;
            var final = DurableKeyJson.Read<FoundationChildNotice>(notice.Payload!).Final;
            var stored = RootChildNoticePrefix + DurableKeyJson.Write(new StoredChildNotice(notice.Id, notice.Payload!, final));
            if (!RootAdmissionWire.FitsEnvelope(stored, null))
                throw new StoreException(Failure.Definite, "The child result exceeds its storage budget.");
            var target = notice.Target;
            // A finalized parent's registry still needs its final notice, even though the model has ended.
            FormattableString insert = $"""
                INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Start","Sender","Done","Seen","Attempts","Trial","JobGeneration"/*seq-column*/)
                SELECT {notice.InboxKey.Id}, {InboxKey.FoundationOccurrenceScope}, b."Actor", b."Partition", {Kinds.ChildFinished}, 0,
                    {stored}, {notice.Sender}, false, 0, 0, 0, 0/*seq*/
                FROM "FoundationRootBirths" b
                JOIN "Instances" i ON i."Partition" = b."Partition" AND i."Id" = b."Actor" AND i."FoundationReference" = b."Reference"
                WHERE b."Tenant" = {tenant} AND b."Actor" = {target.Id} AND b."Partition" = {target.Partition}
                    AND b."Creation" = {notice.TargetCreation} AND NOT i."Retired"
                    AND (i."FoundationTerminalRevision" IS NULL OR i."Children" IS NOT NULL AND {final})
                """;
            await db.Database.ExecuteSqlInterpolatedAsync(RootChildDialectQuery(db, insert), token);
            return true;
        }
    }

    private static FormattableString RootChildDialectQuery(KernelDb db, FormattableString query)
    {
        var sqlite = db.UsesSqliteRootRuntime;
        var sql = query.Format
            .Replace("/*seq-column*/", sqlite ? """, "Seq" """ : "", StringComparison.Ordinal)
            .Replace("/*seq*/", sqlite ? """, (SELECT coalesce(max("Seq"), 0) + 1 FROM "Messages")""" : "", StringComparison.Ordinal)
            .Replace("/*json-cast*/", sqlite ? "" : "::jsonb", StringComparison.Ordinal)
            .Replace("/*text-cast*/", sqlite ? "" : "::text", StringComparison.Ordinal)
            .Replace("/*lock*/", sqlite ? "" : " FOR UPDATE", StringComparison.Ordinal);
        return FormattableStringFactory.Create(sql, query.GetArguments());
    }

    private static async Task<bool> BindRootChildStartsAsync(KernelDb db, string tenant, RootChildStart[] starts, CancellationToken token)
    {
        var envelopes = Json.Write(starts.Select((start, ordinal) =>
        {
            var (reference, creator) = start;
            var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
            var (header, payload) = RootAdmissionWire.EncodeCreator(creator);
            if (!RootAdmissionWire.FitsEnvelope(header, payload))
                throw new StoreException(Failure.Definite, "The child creator exceeds its storage budget.");
            return new
            {
                Ordinal = ordinal, Reference = reference, Actor = creator.Target.Id, creator.Target.Partition,
                Creator = creator.InboxKey.Id, Fingerprint = fingerprint, Header = header, Payload = payload, creator.Sender
            };
        }));
        if (db.UsesSqliteRootRuntime)
            return await BindSqliteRootChildStartsAsync(db, tenant, envelopes, starts.Length, token);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH inputs AS MATERIALIZED (
                SELECT * FROM jsonb_to_recordset({envelopes}::jsonb) AS input("Ordinal" integer, "Reference" text,
                    "Actor" text, "Partition" integer, "Creator" text, "Fingerprint" text, "Header" text, "Payload" text, "Sender" text)
            ), birth AS (
                INSERT INTO "FoundationRootBirths" ("Tenant","Reference","Actor","Partition","Creation","Creator","Fingerprint")
                SELECT {tenant}, "Reference", "Actor", "Partition", 1, "Creator", "Fingerprint" FROM inputs
                RETURNING "Reference", "Actor", "Partition", "Creator"
            )
            INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Payload","Start","Sender","Done","Seen","Attempts","Trial","JobGeneration")
            SELECT birth."Creator", 0, birth."Actor", birth."Partition", {Kinds.Start}, 0, input."Payload", input."Header",
                input."Sender", false, 0, 0, 0, 0
            FROM birth JOIN inputs input ON input."Reference" = birth."Reference" ORDER BY input."Ordinal"
            """, token) == starts.Length;
    }

    private static async Task BindRootChildInputsAsync(KernelDb db, string tenant, Message[] inputs, CancellationToken token)
    {
        if (inputs.Length == 0) return;
        var envelopes = Json.Write(inputs.Select((input, ordinal) => new
        {
            Ordinal = ordinal, input.InboxKey.Id, Actor = input.Target.Id, input.Target.Partition, input.Sender, input.TargetCreation, input.Kind,
            Header = EncodeChildInput(input)
        }));
        if (db.UsesSqliteRootRuntime)
        {
            await BindSqliteRootChildInputsAsync(db, tenant, envelopes, token);
            return;
        }
        // The child may still be a creator. A terminal or retired child no longer needs the input.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Start","Sender","Done","Seen","Attempts","Trial","JobGeneration")
            SELECT input."Id", {InboxKey.FoundationOccurrenceScope}, b."Actor", b."Partition", input."Kind", 0,
                input."Header", input."Sender", false, 0, 0, 0, 0
            FROM jsonb_to_recordset({envelopes}::jsonb) AS input("Ordinal" integer, "Id" text, "Actor" text, "Partition" integer,
                "Sender" text, "TargetCreation" bigint, "Kind" text, "Header" text)
            JOIN "FoundationRootBirths" b ON b."Tenant" = {tenant} AND b."Actor" = input."Actor"
                AND b."Partition" = input."Partition" AND b."Creation" = input."TargetCreation"
            LEFT JOIN "Instances" i ON i."Id" = b."Actor" AND i."Partition" = b."Partition" AND i."FoundationReference" = b."Reference"
            WHERE i."Parent" = input."Sender" AND i."FoundationTerminalRevision" IS NULL AND NOT i."Retired"
                OR i."Id" IS NULL AND EXISTS (SELECT 1 FROM "Messages" creator
                    WHERE creator."Id" = b."Creator" AND creator."Target" = b."Actor" AND creator."Partition" = b."Partition"
                        AND creator."Sender" = input."Sender" AND creator."InboxScope" = 0 AND NOT creator."Done")
            ORDER BY input."Ordinal"
            """, token);
    }

    private RootChildEffects? PrepareRootChildEffects(WorkerRoot root, InstanceRow row, IReadOnlyList<Message> sends,
        PartitionFence? fence = null)
    {
        if (_rootChildReference is not { } reference) return null;
        var starts = new List<RootChildStart>();
        var inputs = new List<Message>();
        Message? notice = null;
        foreach (var message in sends)
        {
            if (message.BindFoundationChild)
            {
                if (message is not { Kind: Kinds.Start, Start: { Creation: { Generation: 1, Previous: 0 } } start, TargetCreation: 1 } ||
                    message.InboxScope != InboxKey.ChildCreatorScope || message.Sender != root.Actor || message.SenderCreation != 1 ||
                    start.Parent is not null && (start.Parent != root.Actor || start.ParentCreation != 1) ||
                    start.Parent is null && start.ParentCreation != 0 || message.Target.Partition != root.Partition ||
                    !message.Target.ChildAddress || !CreationProtocol.IsPhysicalAddress(message.Target.Id))
                    throw RootShape();
                var child = new CreationAddress(message.Target.Id, 1);
                starts.Add(new(reference(root.Tenant, child), (message with { StorageId = "root:" + child.Id, Payload = start.Input })
                    .WithInboxScope(0)));
            }
            else if (message.CascadeOrigin)
            {
                if (message is not { Kind: Kinds.Terminate, HasIssuedTicket: true, TargetCreation: 1, SenderCreation: 1,
                        Start: null, Payload: null, Due: null, KeepUntil: null, ReplyTo: null } || message.Sender != root.Actor ||
                    _rootChildMessageHandle is not { } issue) throw RootShape();
                var receiver = reference(root.Tenant, new(message.Target.Id, 1));
                var id = issue("cascade:" + message.Id);
                inputs.Add(message with { Id = id, StorageId = ChildOccurrence(receiver, id) });
            }
            else if (message.FoundationRootChildInput)
            {
                if (!message.ActorAuthored || !FoundationSelfSend.IsHandle(message.Id) ||
                    !(FoundationMetadataLimits.IsOrdinaryKind(message.Kind) || message.Kind == Kinds.Terminate && message.Payload is null) ||
                    message.Due is not null || message.TargetCreation != 1 || message.Sender != root.Actor || message.SenderCreation != 1 ||
                    message.InboxKey != InboxKey.Issued(CreationProtocol.Origin(root.Actor, 1) + "#" + message.Id))
                    throw RootShape();
                var child = new CreationAddress(message.Target.Id, 1);
                inputs.Add(message with { StorageId = ChildOccurrence(reference(root.Tenant, child), message.Id) });
            }
            else if (message.Kind == Kinds.ChildFinished)
            {
                if (notice is not null || row.Parent is null || row.Parent != message.Target.Id ||
                    message.TargetCreation != row.ParentCreation || message.Sender != row.Id || message.SenderCreation != row.Creation ||
                    message.Id != CreationProtocol.TerminalNoticeId(row.Id, row.Creation, row.Revision) || message.Payload is null)
                    throw RootShape();
                notice = message with { StorageId = ChildNoticeOccurrence(reference(root.Tenant, new(row.Parent, row.ParentCreation)),
                    message.Id, sqlite: !postgres) };
            }
            else throw RootShape();
        }
        var pending = ProtectChildBirths is not null && starts.Count > 0
            ? PrepareChildBirthProtection(root, row, fence ?? throw RootShape(), starts)
            : null;
        return new(root.Reference, row.Children, starts.ToArray(), inputs.ToArray(), notice, pending);
    }

    private PendingChildBirth[] PrepareChildBirthProtection(WorkerRoot parent, InstanceRow row, PartitionFence fence, List<RootChildStart> starts)
    {
        var key = _rootChildIssuanceKey ?? throw RootShape();
        return starts.Select(child =>
        {
            var creator = child.Creator;
            var spec = creator.Start!;
            var fingerprint = CreationProtocol.Fingerprint(spec, creator.Kind, creator.Payload) ?? throw RootShape();
            var birth = new FoundationChildIssuance(parent.Tenant, parent.Actor, parent.Fingerprint, row.Revision, creator.Target.Id, fingerprint);
            return new PendingChildBirth(parent.Tenant, new Contract.ActorRef(child.Reference), spec.Model, spec.Definition,
                FoundationChildIssuance.Issue(key, birth, fence))
            {
                Parent = _takesAttachedTestRuns && spec.Parent is not null && row.FoundationDispatchHeader is { } header
                    ? new(new(parent.Reference), row.Model, header.Header.TypeData, IsRoot: row.Parent is null)
                    : null
            };
        }).ToArray();
    }

    private Task<bool> ProtectPreparedChildBirthsAsync(RootCheckpoints checkpoints, CancellationToken token)
    {
        if (ProtectChildBirths is null) return Task.FromResult(true);
        var prepared = checkpoints.Commands.Select(command => command switch
        {
            FirstRootCheckpoint first => first.ChildEffects,
            RootTurnCheckpoint turn => turn.ChildEffects,
            RootParkOpening park => park.ChildEffects,
            _ => null
        }).OfType<RootChildEffects>().Where(children => children.PendingBirths is not null).ToArray();
        return ProtectRootChildEffectsAsync(prepared, token);
    }

    private async Task<bool> ProtectRootChildEffectsAsync(RootChildEffects[] prepared, CancellationToken token)
    {
        if (prepared.Length == 0 || ProtectChildBirths is not { } protect) return true;

        // Preparation has disposed its read transactions. No Foundation locks span this host callback.
        var births = Array.AsReadOnly(prepared.SelectMany(children => children.PendingBirths!).ToArray());
        var result = await protect(births, token);
        token.ThrowIfCancellationRequested();
        switch (result)
        {
            case ChildBirthProtectionOutcome.Protected:
                foreach (var children in prepared) children.BirthsProtected = true;
                return true;
            case ChildBirthProtectionOutcome.Stale:
                return false;
            case ChildBirthProtectionOutcome.Refused:
                throw new StoreException(Failure.Definite, "The host could not protect a child executable selection.");
            case ChildBirthProtectionOutcome.Retry:
                throw new StoreException(Failure.Transient, "The host will retry child executable protection.");
            default:
                throw new InvalidOperationException("The child birth protector returned an unknown outcome.");
        }
    }

    private static PreparedRoot WithRootChildEffects(PreparedRoot prepared, RootChildEffects? children, string? parent)
    {
        if (children is null) return prepared;
        return prepared.Checkpoint switch
        {
            FirstRootCheckpoint first => prepared with
            {
                JournalTurn = prepared.JournalTurn! with { Row = first.Row with { Parent = parent, Children = children.Children } },
                Checkpoint = first with { ChildEffects = children }
            },
            RootTurnCheckpoint subsequent => prepared with { Checkpoint = subsequent with { ChildEffects = children } },
            _ => throw RootShape()
        };
    }

    private static async Task<bool> CloseEndedChildRegistryAsync(KernelDb db, WorkerRoot root, IReadOnlyList<InboxKey> keys,
        CancellationToken token)
    {
        var ids = keys.Where(key => IsChildOccurrence(root, key)).Select(key => key.Id).ToArray();
        if (ids.Length == 0) return true;
        var actor = await db.Instances.FromSqlInterpolated($"""
            SELECT i.* FROM "Instances" i
            WHERE i."Id" = {root.Actor} AND i."Partition" = {root.Partition}
                AND i."FoundationTenant" = {root.Tenant} AND i."FoundationReference" = {root.Reference}
                AND NOT i."Retired" AND i."FoundationTerminalRevision" = i."Revision" FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(token);
        if (actor is null) return false;
        if (actor.Children is null) return true;
        var pending = await db.Messages.AsNoTracking().Where(row => row.Target == root.Actor && row.Partition == root.Partition &&
            row.Kind == Kinds.ChildFinished && row.InboxScope == InboxKey.FoundationOccurrenceScope &&
            !row.Done && row.EventMatch == null && ids.Contains(row.Id)).ToArrayAsync(token);
        var registry = ChildRegistry.Read(actor.Children);
        var prior = registry.Count;
        foreach (var row in pending)
        {
            if (!IsStoredRootChildNotice(row.Start)) return false;
            var envelope = DecodeRootChildNotice(row, root.Reference);
            if (envelope.Read<FoundationChildNotice>().Final)
                registry.Remove(new(envelope.Sender!, envelope.SenderCreation));
        }
        return registry.Count == prior || await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Children" = {ChildRegistry.Write(registry)}
            WHERE "Id" = {root.Actor} AND "Partition" = {root.Partition} AND "Revision" = {actor.Revision}
                AND "FoundationTenant" = {root.Tenant} AND "FoundationReference" = {root.Reference}
                AND "Children" IS NOT DISTINCT FROM {actor.Children} AND NOT "Retired" AND "FoundationTerminalRevision" = "Revision"
            """, token) == 1;
    }

    private static bool IsChildOccurrence(WorkerRoot root, InboxKey key) => key.Scope == InboxKey.FoundationOccurrenceScope &&
        key.Id.StartsWith(root.Reference + "/child/", StringComparison.Ordinal);

    private static string ChildOccurrence(string reference, string id) =>
        reference + "/child/" + FoundationRootCreator.OperationDigest(id);

    private static string ChildNoticeOccurrence(string reference, string id, bool sqlite) =>
        sqlite ? reference + SqliteChildNoticePath + id : ChildOccurrence(reference, id);

    private static string RootChildNoticePredicate(string alias) =>
        $"""(COALESCE({alias}."Start", '') LIKE '{RootChildNoticePrefix}%' OR COALESCE({alias}."Start", '') LIKE '{RootChildInputPrefix}%' OR COALESCE({alias}."Start", '') = '{RootChildCascadeWire}')""";

    private static string RootChildTerminatePredicate(string input, string target) =>
        FormattableString.Invariant($"""
            ({input}."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND {input}."Kind" = '{Kinds.Terminate}'
                AND {input}."Start" LIKE '{RootChildInputPrefix}%' AND {input}."Sender" = {target}."Parent")
            """);

    private static string RootChildParkInputs(string input, string target) => $"""
        ({target}."FoundationParkAt" IS NULL OR NOT {RootChildNoticePredicate(input)} OR
            {RootChildTerminatePredicate(input, target)} AND {target}."FoundationParkClearing" IS NULL
            AND ({target}."Jobs" IS NULL OR {target}."FoundationParkJobResult" IS NOT NULL OR {target}."FoundationParkLiveJob" IS NOT NULL)
            AND {target}."CustomHandles" IS NULL
            AND NOT EXISTS (SELECT 1 FROM "Subscriptions" s WHERE s."Instance" = {target}."Id"))
        """;

    private static bool IsRootChildTerminate(Message message) => message.FoundationRootChildInput && message.Kind == Kinds.Terminate;

    private static bool IsStoredRootChildInput(string? header) =>
        header?.StartsWith(RootChildInputPrefix, StringComparison.Ordinal) == true;

    private static string EncodeChildInput(Message input)
    {
        var storedInput = RootChildInputPrefix + DurableKeyJson.Write(new StoredChildInput(input.Id, input.Kind, input.Payload));
        if (!RootAdmissionWire.FitsEnvelope(storedInput, null))
            throw new StoreException(Failure.Definite, "The child input exceeds its storage budget.");
        return storedInput;
    }

    private static Message DecodeRootChildInput(MessageEntity row, string reference)
    {
        var stored = DurableKeyJson.Read<StoredChildInput>(row.Start![RootChildInputPrefix.Length..]);
        if (row.InboxScope != InboxKey.FoundationOccurrenceScope || row.Kind != stored.Kind ||
            !(FoundationMetadataLimits.IsOrdinaryKind(stored.Kind) || stored.Kind == Kinds.Terminate && stored.Payload is null) ||
            !FoundationSelfSend.IsHandle(stored.Id) ||
            row.Sender is null || row.JobGeneration != 0 || row.Due != 0 || row.KeepUntil is not null ||
            row.ReplyTo is not null || row.Payload is not null || row.Id != ChildOccurrence(reference, stored.Id))
            throw new StoreException(Failure.Definite, "Invalid PostgreSQL child input.");
        return (new Message(stored.Id, new(row.Target), stored.Kind, stored.Payload, row.Sender, TargetCreation: 1, SenderCreation: 1)
            { ActorAuthored = stored.Kind != Kinds.Terminate, FoundationRootChildInput = true, StorageId = row.Id })
            .WithInboxScope(InboxKey.FoundationOccurrenceScope);
    }

    private static bool IsStoredRootChildNotice(string? header) =>
        header?.StartsWith(RootChildNoticePrefix, StringComparison.Ordinal) == true;

    private static Message DecodeRootChildNotice(MessageEntity row, string reference, bool sqlite = false)
    {
        var stored = DurableKeyJson.Read<StoredChildNotice>(row.Start![RootChildNoticePrefix.Length..]);
        var notice = DurableKeyJson.Read<FoundationChildNotice>(stored.Payload);
        if (row.InboxScope != InboxKey.FoundationOccurrenceScope || row.Kind != Kinds.ChildFinished || row.Sender != notice.Child ||
            row.JobGeneration != 0 || row.Due != 0 || row.KeepUntil is not null || row.ReplyTo is not null || row.Payload is not null ||
            notice.Creation != 1 || notice.Status is not (InstanceStatus.Finished or InstanceStatus.Faulted or InstanceStatus.Cancelled) ||
            stored.Final != notice.Final || !notice.Final && notice.Status != InstanceStatus.Faulted ||
            !CreationProtocol.IsTerminalHandle(stored.Id, CreationProtocol.Origin(notice.Child, 1)) ||
            row.Id != ChildNoticeOccurrence(reference, stored.Id, sqlite))
            throw new StoreException(Failure.Definite, "Invalid PostgreSQL child notice.");
        return (new Message(stored.Id, new(row.Target), Kinds.ChildFinished, stored.Payload,
            row.Sender, TargetCreation: 1, SenderCreation: 1) { FoundationRootChildNotice = true, StorageId = row.Id })
            .WithInboxScope(InboxKey.FoundationOccurrenceScope);
    }

    private const string RemovedTerminalChildren = """
        SELECT 1 FROM jsonb_array_elements(OLD."Children"::jsonb) AS removed(child)
        WHERE NOT coalesce(NEW."Children"::jsonb, '[]'::jsonb) @> jsonb_build_array(removed.child)
        """;

    // The parent can close a registry entry only against the final notice of that exact committed child life.
    private static readonly string PendingFinalChildNotice = FormattableString.Invariant($"""
        SELECT 1 FROM "Messages" m JOIN "FoundationRootBirths" b
            ON b."Actor" = m."Sender" AND b."Partition" = m."Partition" AND b."Tenant" = OLD."FoundationTenant"
        JOIN "Instances" child ON child."Id" = b."Actor" AND child."Partition" = b."Partition"
            AND child."FoundationReference" = b."Reference" AND child."FoundationTenant" = b."Tenant"
        CROSS JOIN LATERAL (SELECT CASE WHEN left(m."Start", {RootChildNoticePrefix.Length}) = '{RootChildNoticePrefix}'
            THEN substring(m."Start" FROM {RootChildNoticePrefix.Length + 1})::jsonb END AS wire) notice
        CROSS JOIN LATERAL (SELECT CASE WHEN jsonb_typeof(notice.wire->'Payload') = 'string'
            THEN (notice.wire->>'Payload')::jsonb END AS body) payload
        CROSS JOIN LATERAL (SELECT 'c' || length(b."Actor") || '_' || b."Actor" || '_' || b."Creation"
            || '#' || child."FoundationTerminalRevision" || '.end' AS handle) canonical
        WHERE m."Target" = OLD."Id" AND m."Partition" = OLD."Partition" AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope}
            AND m."Kind" = '{Kinds.ChildFinished}' AND m."Due" = 0 AND m."EventMatch" IS NULL AND NOT m."Done"
            AND m."ReplyTo" IS NULL AND m."KeepUntil" IS NULL AND m."Payload" IS NULL AND m."JobGeneration" = 0
            AND m."Id" = OLD."FoundationReference" || '/child/' || {ChildNoticeDigest("canonical.handle")}
            AND b."Actor" = removed.child->>'Id' AND b."Creation" = 1 AND to_jsonb(b."Creation") = removed.child->'Creation'
            AND child."FoundationTerminalRevision" > 0 AND child."FoundationTerminalAt" IS NOT NULL
            AND (child."Parent" = OLD."Id" OR child."Retired")
            AND notice.wire->'Final' = 'true'::jsonb AND notice.wire->>'Id' = canonical.handle
            AND payload.body->>'Child' = b."Actor" AND payload.body->'Creation' = to_jsonb(b."Creation")
            AND payload.body->'Final' = 'true'::jsonb AND payload.body->'Status' = to_jsonb(child."Status")
            AND child."Status" IN ({(int)InstanceStatus.Finished}, {(int)InstanceStatus.Faulted}, {(int)InstanceStatus.Cancelled})
        """);

    // Generated child terminal handles are ASCII. Their operation digest hashes big-endian UTF-16 code units.
    private static string ChildNoticeDigest(string handle) => $"""
        upper(encode(sha256(decode(regexp_replace(encode(convert_to({handle}, 'UTF8'), 'hex'),
            '([0-9a-f][0-9a-f])', '00' || chr(92) || '1', 'g'), 'hex')), 'hex'))
        """;

    private static readonly string TerminalChildRegistryWrite = $"""
        IF TG_OP = 'UPDATE' AND COALESCE(OLD."FoundationTerminalRevision" = OLD."Revision", false)
            AND NOT OLD."Retired" AND OLD."Children" IS NOT NULL
            AND (to_jsonb(NEW) - 'Children') = (to_jsonb(OLD) - 'Children')
            AND coalesce(NEW."Children"::jsonb, '[]'::jsonb) <@ OLD."Children"::jsonb
            AND NOT EXISTS ({RemovedTerminalChildren} AND NOT EXISTS ({PendingFinalChildNotice}))
            AND jsonb_array_length(coalesce(NEW."Children"::jsonb, '[]'::jsonb)) < jsonb_array_length(OLD."Children"::jsonb)
        THEN RETURN NEW; END IF;
        """;

    // The owner must consume its selected notice in the same fenced commit as the registry change.
    private static readonly string RequireSettledTerminalChildNotice = $"""
        CREATE OR REPLACE FUNCTION kernel_require_terminal_child_notice_settlement() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF EXISTS ({RemovedTerminalChildren} AND EXISTS ({PendingFinalChildNotice}))
            THEN RAISE EXCEPTION 'A closed child registry entry must settle its final notice' USING ERRCODE = '23514'; END IF;
            RETURN NEW;
        END
        $fn$;
        DROP TRIGGER IF EXISTS kernel_require_terminal_child_notice_settlement ON "Instances";
        CREATE CONSTRAINT TRIGGER kernel_require_terminal_child_notice_settlement AFTER UPDATE ON "Instances"
        DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD."Revision" = NEW."Revision"
            AND OLD."Children" IS DISTINCT FROM NEW."Children" AND OLD."FoundationReference" IS NOT NULL)
        EXECUTE FUNCTION kernel_require_terminal_child_notice_settlement();
        """;
}
