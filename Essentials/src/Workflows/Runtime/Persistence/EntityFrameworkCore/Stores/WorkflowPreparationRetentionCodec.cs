using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Checked metadata only; no material, CLR type names or issuing authority are persisted.</summary>
internal static class WorkflowPreparationRetentionCodec
{
    private const int FormatVersion = 1;

    public static WorkflowPreparationRetentionEntity Create(WorkflowPreparationSnapshot snapshot, string schemaVersion)
    {
        Validate(snapshot);
        RequirePreparationFormat(schemaVersion);
        var id = snapshot.Id;
        var scope = ScopeIdentity(id.Scope);
        var key = PhysicalId(id);
        return new WorkflowPreparationRetentionEntity
        {
            Id = key,
            ScopeKey = EfRelationalIdentity.Encode(scope),
            ScopeKeyHash = EfRelationalIdentity.Hash(scope),
            OwnerAddress = EfRelationalIdentity.Encode(id.OwnerAddress),
            OperationId = EfRelationalIdentity.Encode(id.OperationId),
            PreparationOrderKey = key,
            IncarnationId = snapshot.Token.IncarnationId,
            Revision = snapshot.Token.Revision,
            State = snapshot.State.ToString(),
            SchemaVersion = schemaVersion,
            ContentJson = RuntimeArtifactJson.Serialize(ToDocument(snapshot))
        };
    }

    public static WorkflowPreparationSnapshot Read(WorkflowPreparationRetentionEntity row, WorkflowPreparationId? expectedId = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        // Skew has priority over interpreting projections from an unknown format.
        RequirePreparationFormat(row.SchemaVersion);
        try
        {
            var content = RuntimeArtifactEfModule.Chain.Upcast<WorkflowPreparationRetentionEntity>(row.SchemaVersion,
                (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]
                ?? throw Corrupt();
            using var json = JsonDocument.Parse(content);
            ValidateWireShape(json.RootElement);
            var document = RuntimeArtifactJson.Deserialize<Document>(content);
            if (document.FormatVersion != FormatVersion) throw Corrupt();
            var snapshot = FromDocument(document);
            Validate(snapshot);
            var id = snapshot.Id;
            var scope = ScopeIdentity(id.Scope);
            var key = PhysicalId(id);
            if (expectedId is not null && expectedId != id ||
                row.Id != key || row.PreparationOrderKey != key ||
                row.ScopeKey != EfRelationalIdentity.Encode(scope) || row.ScopeKeyHash != EfRelationalIdentity.Hash(scope) ||
                row.OwnerAddress != EfRelationalIdentity.Encode(id.OwnerAddress) ||
                row.OperationId != EfRelationalIdentity.Encode(id.OperationId) ||
                row.Revision != snapshot.Token.Revision || row.IncarnationId != snapshot.Token.IncarnationId ||
                row.State != snapshot.State.ToString())
                throw Corrupt();
            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or
            NotSupportedException or FormatException or OverflowException or NullReferenceException)
        {
            throw new InvalidDataException("The persisted preparation retention document is corrupt.", exception);
        }
    }

    internal static string PhysicalId(WorkflowPreparationId id) =>
        EfRelationalIdentity.HashLengthFramed(ScopeIdentity(id.Scope), id.OwnerAddress, id.OperationId);

    // Null is explicit global scope, distinct even from a tenant whose opaque name starts with a marker.
    private static string ScopeIdentity(PersistenceScope? scope) => scope is null ? "0" : "1" + scope.Value;

    private static void RequirePreparationFormat(string stamp)
    {
        _ = EfSchemaVersion.NotReadable(RuntimeArtifactEfModule.Chain, stamp);
        if (!RuntimeArtifactEfModule.Chain.IsAtOrAfter(stamp, RuntimeArtifactEfModule.PreparationSchemaVersion))
            throw new InvalidDataException("The legacy RuntimeArtifact format cannot carry preparation metadata.");
    }

    private static Document ToDocument(WorkflowPreparationSnapshot value) => new(FormatVersion, value.Id,
        new(value.Selection.SnapshotId, value.Selection.SourceFingerprint, value.Selection.SourceProfile,
            value.Selection.CompilerProfile, value.Selection.PreparedProfile, value.Selection.PreparedContentFingerprint,
            value.Selection.Roots.Select(ToRoot).ToArray(), value.Selection.Closure.Select(ToPin).ToArray(),
            value.Selection.Fingerprint), value.AcceptedOwner, value.State, value.Token, value.ClosedFrom, value.TerminalDecision);

    private static WorkflowPreparationSnapshot FromDocument(Document value)
    {
        var selection = value.Selection ?? throw Corrupt();
        var roots = selection.Roots ?? throw Corrupt();
        var closure = selection.Closure ?? throw Corrupt();
        return new(value.Id, new(selection.SnapshotId, selection.SourceFingerprint, selection.SourceProfile,
            selection.CompilerProfile, selection.PreparedProfile, selection.PreparedContentFingerprint,
            Array.AsReadOnly(roots.Select(FromRoot).ToArray()), Array.AsReadOnly(closure.Select(FromPin).ToArray()),
            selection.Fingerprint), value.AcceptedOwner, value.State, value.Token, value.ClosedFrom,
            value.TerminalDecision is { } decision
                ? decision with { PublishedReferenceIds = Array.AsReadOnly((decision.PublishedReferenceIds ?? throw Corrupt()).ToArray()) }
                : null);
    }

    private static Member ToRoot(WorkflowPreparationRoot value) => value switch
    {
        WorkflowPreparationExecutableRoot root => new(Kind(root.Kind), Element(root)),
        WorkflowPreparationTemplateRoot root => new("template", Element(root)),
        _ => throw Corrupt()
    };

    private static WorkflowPreparationRoot FromRoot(Member value) => value.Kind switch
    {
        "native" or "model" => CheckedRoot(value),
        "template" => RuntimeArtifactJson.Deserialize<WorkflowPreparationTemplateRoot>(value.Content.GetRawText()),
        _ => throw Corrupt()
    };

    private static WorkflowPreparationExecutableRoot CheckedRoot(Member value)
    {
        var root = RuntimeArtifactJson.Deserialize<WorkflowPreparationExecutableRoot>(value.Content.GetRawText());
        if (Kind(root.Kind) != value.Kind) throw Corrupt();
        return root;
    }

    private static Member ToPin(WorkflowPreparationMaterialPin value) => value switch
    {
        WorkflowPreparationExecutablePin pin => new(Kind(pin.Kind), Element(pin)),
        WorkflowPreparationTemplatePin pin => new("template", Element(pin)),
        _ => throw Corrupt()
    };

    private static WorkflowPreparationMaterialPin FromPin(Member value) => value.Kind switch
    {
        "native" or "model" => CheckedPin(value),
        "template" => RuntimeArtifactJson.Deserialize<WorkflowPreparationTemplatePin>(value.Content.GetRawText()),
        _ => throw Corrupt()
    };

    private static WorkflowPreparationExecutablePin CheckedPin(Member value)
    {
        var pin = RuntimeArtifactJson.Deserialize<WorkflowPreparationExecutablePin>(value.Content.GetRawText());
        if (Kind(pin.Kind) != value.Kind) throw Corrupt();
        return pin;
    }

    private static string Kind(WorkflowPreparationExecutableKind kind) => kind switch
    {
        WorkflowPreparationExecutableKind.Native => "native",
        WorkflowPreparationExecutableKind.Model => "model",
        _ => throw Corrupt()
    };

    private static JsonElement Element<T>(T value)
    {
        using var document = JsonDocument.Parse(RuntimeArtifactJson.Serialize(value));
        return document.RootElement.Clone();
    }

    private static void Validate(WorkflowPreparationSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateId(value.Id);
        var selection = value.Selection ?? throw Corrupt();
        Required(selection.SnapshotId, selection.SourceFingerprint, selection.SourceProfile, selection.CompilerProfile,
            selection.PreparedProfile, selection.PreparedContentFingerprint, selection.Fingerprint);
        if (selection.Roots is not { Count: > 0 } || selection.Closure is not { Count: > 0 }) throw Corrupt();
        foreach (var root in selection.Roots)
            switch (root)
            {
                case WorkflowPreparationExecutableRoot executable:
                    _ = Kind(executable.Kind); ValidateExecutable(executable.Identity); break;
                case WorkflowPreparationTemplateRoot template:
                    ValidateTemplate(template.Identity); break;
                default: throw Corrupt();
            }
        foreach (var pin in selection.Closure)
            switch (pin)
            {
                case WorkflowPreparationExecutablePin executable:
                    _ = Kind(executable.Kind); ValidateExecutable(executable.Identity); Required(executable.IncarnationId); break;
                case WorkflowPreparationTemplatePin template:
                    ValidateTemplate(template.Identity); Required(template.IncarnationId, template.HashClaimIncarnationId);
                    if (template.Revision <= 0) throw Corrupt();
                    break;
                default: throw Corrupt();
            }
        ValidateOwner(value.AcceptedOwner, value.Id, selection);
        ValidateToken(value.Token, value.Id, selection.Fingerprint);
        if (value.State == WorkflowPreparationState.Retained)
        {
            if (value.ClosedFrom is not null || value.TerminalDecision is not null) throw Corrupt();
            return;
        }
        if (value.State is not (WorkflowPreparationState.Transferred or WorkflowPreparationState.RejectedReleased) ||
            value.ClosedFrom is not { } closed || value.TerminalDecision is not { } decision) throw Corrupt();
        ValidateToken(closed, value.Id, selection.Fingerprint);
        if (closed.IncarnationId != value.Token.IncarnationId || closed.Revision == long.MaxValue ||
            value.Token.Revision != closed.Revision + 1) throw Corrupt();
        ValidateOwner(decision.VerifiedAt, value.Id, selection);
        Required(decision.CanonicalDecisionId, decision.DecisionFingerprint, decision.SelectionFingerprint);
        if (decision.VerifiedAt.LedgerIncarnation != value.AcceptedOwner.LedgerIncarnation ||
            decision.VerifiedAt.RequestFingerprint != value.AcceptedOwner.RequestFingerprint) throw Corrupt();
        if (decision.SelectionFingerprint != selection.Fingerprint || decision.PublishedReferenceIds is null ||
            value.State == WorkflowPreparationState.Transferred && decision.Kind != WorkflowPreparationDecisionKind.Published ||
            value.State == WorkflowPreparationState.RejectedReleased && decision.Kind != WorkflowPreparationDecisionKind.Rejected)
            throw Corrupt();
        foreach (var reference in decision.PublishedReferenceIds) Required(reference);
        if (decision.Kind == WorkflowPreparationDecisionKind.Rejected && decision.PublishedReferenceIds.Count != 0) throw Corrupt();
    }

    private static void ValidateId(WorkflowPreparationId id)
    {
        if (id is null) throw Corrupt();
        Required(id.OwnerAddress, id.OperationId);
    }

    private static void ValidateOwner(WorkflowPreparationOwnerStamp owner, WorkflowPreparationId id, WorkflowPreparationSelection selection)
    {
        if (owner is null || owner.Id != id || owner.OperationRevision < 0) throw Corrupt();
        Required(owner.LedgerIncarnation, owner.OperationState, owner.RequestFingerprint);
        if (owner.SnapshotId != selection.SnapshotId || owner.SourceFingerprint != selection.SourceFingerprint ||
            owner.SourceProfile != selection.SourceProfile || owner.CompilerProfile != selection.CompilerProfile ||
            owner.PreparedProfile != selection.PreparedProfile || owner.PreparedContentFingerprint != selection.PreparedContentFingerprint)
            throw Corrupt();
    }

    private static void ValidateToken(WorkflowPreparationToken token, WorkflowPreparationId id, string fingerprint)
    {
        if (token is null || token.Id != id || token.Revision <= 0 || token.SelectionFingerprint != fingerprint) throw Corrupt();
        Required(token.IncarnationId);
    }

    private static void ValidateExecutable(WorkflowExecutableIdentity identity)
    {
        if (identity is null) throw Corrupt();
        Required(identity.ArtifactId, identity.ArtifactHash, identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion);
    }

    private static void ValidateTemplate(ExecutableActivityTemplateIdentity identity)
    {
        if (identity is null) throw Corrupt();
        Required(identity.TemplateId, identity.TemplateHash);
    }

    private static void Required(params string[] values)
    {
        if (values.Any(string.IsNullOrWhiteSpace)) throw Corrupt();
    }

    private static InvalidDataException Corrupt() => new("The preparation retention document is corrupt.");

    // Every field, including nullable history/global scope, must be present. Missing numeric/enum
    // fields must not silently deserialize as a valid zero or Retained state.
    private static void ValidateWireShape(JsonElement value)
    {
        Object(value, "formatVersion", "id", "selection", "acceptedOwner", "state", "token", "closedFrom", "terminalDecision");
        IdShape(value.GetProperty("id"));
        OwnerShape(value.GetProperty("acceptedOwner"));
        TokenShape(value.GetProperty("token"));
        var selection = value.GetProperty("selection");
        Object(selection, "snapshotId", "sourceFingerprint", "sourceProfile", "compilerProfile", "preparedProfile",
            "preparedContentFingerprint", "roots", "closure", "fingerprint");
        foreach (var root in selection.GetProperty("roots").EnumerateArray()) MemberShape(root, pin: false);
        foreach (var pin in selection.GetProperty("closure").EnumerateArray()) MemberShape(pin, pin: true);
        if (value.GetProperty("closedFrom") is { ValueKind: not JsonValueKind.Null } closed) TokenShape(closed);
        if (value.GetProperty("terminalDecision") is { ValueKind: not JsonValueKind.Null } decision)
        {
            Object(decision, "kind", "verifiedAt", "canonicalDecisionId", "decisionFingerprint", "selectionFingerprint", "publishedReferenceIds");
            OwnerShape(decision.GetProperty("verifiedAt"));
        }
    }

    private static void IdShape(JsonElement value)
    {
        Object(value, "scope", "ownerAddress", "operationId");
        if (value.GetProperty("scope") is { ValueKind: not JsonValueKind.Null } scope) Object(scope, "value");
    }

    private static void OwnerShape(JsonElement value)
    {
        Object(value, "id", "ledgerIncarnation", "operationRevision", "operationState", "requestFingerprint", "snapshotId",
            "sourceFingerprint", "sourceProfile", "compilerProfile", "preparedProfile", "preparedContentFingerprint");
        IdShape(value.GetProperty("id"));
    }

    private static void TokenShape(JsonElement value)
    {
        Object(value, "id", "incarnationId", "revision", "selectionFingerprint");
        IdShape(value.GetProperty("id"));
    }

    private static void MemberShape(JsonElement value, bool pin)
    {
        Object(value, "kind", "content");
        var kind = RuntimeArtifactJson.Deserialize<string>(value.GetProperty("kind").GetRawText());
        var content = value.GetProperty("content");
        switch (kind, pin)
        {
            case ("native" or "model", false): Object(content, "kind", "identity"); break;
            case ("template", false): Object(content, "identity"); break;
            case ("native" or "model", true): Object(content, "kind", "identity", "incarnationId"); break;
            case ("template", true): Object(content, "identity", "incarnationId", "hashClaimIncarnationId", "revision"); break;
            default: throw Corrupt();
        }
        if (kind == "template") Object(content.GetProperty("identity"), "templateId", "templateHash");
        else Object(content.GetProperty("identity"), "artifactId", "definitionId", "definitionVersionId", "artifactVersion", "artifactHash");
    }

    private static void Object(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Corrupt();
        var properties = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Length != names.Length || properties.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            !names.All(name => properties.Contains(name, StringComparer.Ordinal))) throw Corrupt();
    }

    private sealed record Document(int FormatVersion, WorkflowPreparationId Id, SelectionDocument Selection,
        WorkflowPreparationOwnerStamp AcceptedOwner, WorkflowPreparationState State, WorkflowPreparationToken Token,
        WorkflowPreparationToken? ClosedFrom, WorkflowPreparationDecision? TerminalDecision);
    private sealed record SelectionDocument(string SnapshotId, string SourceFingerprint, string SourceProfile,
        string CompilerProfile, string PreparedProfile, string PreparedContentFingerprint, Member[] Roots, Member[] Closure,
        string Fingerprint);
    private sealed record Member(string Kind, JsonElement Content);
}
