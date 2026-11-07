using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// Checked immutable barrier data. Its presence alone is not a commit/target/old-writer certificate.
internal static class WorkflowPreparationEnrollmentCodec
{
    internal enum Outcome { Accepted, Aborted }
    internal sealed record Barrier(WorkflowPreparationEfEnrollmentBinding Binding, Outcome Outcome,
        string Incarnation, string? HeaderIncarnation, string? SelectionFingerprint);
    private sealed record Document(int FormatVersion, Barrier Barrier);

    public static WorkflowPreparationEnrollmentEntity Create(Barrier barrier)
    {
        Validate(barrier);
        return new()
        {
            Id = WorkflowPreparationRetentionCodec.PhysicalId(barrier.Binding.Owner.Id),
            ScopeKeyHash = ScopeHash(barrier.Binding.Owner.Id.Scope),
            IncarnationId = barrier.Incarnation,
            Revision = 1,
            ContentJson = RuntimeArtifactJson.Serialize(new Document(1, barrier))
        };
    }

    public static Barrier Read(WorkflowPreparationEnrollmentEntity row, WorkflowPreparationId expected)
    {
        try
        {
            using var json = JsonDocument.Parse(row.ContentJson);
            Shape(json.RootElement);
            var document = RuntimeArtifactJson.Deserialize<Document>(row.ContentJson);
            if (document.FormatVersion != 1) throw Corrupt();
            var barrier = document.Barrier;
            Validate(barrier);
            if (barrier.Binding.Owner.Id != expected || row.Id != WorkflowPreparationRetentionCodec.PhysicalId(expected) ||
                row.ScopeKeyHash != ScopeHash(expected.Scope) || row.IncarnationId != barrier.Incarnation || row.Revision != 1)
                throw Corrupt();
            return barrier;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            NotSupportedException or FormatException or OverflowException or NullReferenceException)
        {
            throw new InvalidDataException("The preparation enrollment barrier is corrupt.", error);
        }
    }

    // Logical revision/state are current CAS expectations, never new acceptance namespaces.
    internal static bool SameMaterial(WorkflowPreparationOwnerStamp left, WorkflowPreparationOwnerStamp right) =>
        (left with { OperationRevision = 0, OperationState = "" }) ==
        (right with { OperationRevision = 0, OperationState = "" });

    internal static bool SameBinding(WorkflowPreparationEfEnrollmentBinding left, WorkflowPreparationEfEnrollmentBinding right) =>
        SameMaterial(left.Owner, right.Owner) && left.IntentId == right.IntentId &&
        left.TargetAssociation == right.TargetAssociation;

    internal static void RequireMatchingHeader(WorkflowPreparationOwnerStamp owner, WorkflowPreparationSnapshot header)
    {
        if (!SameMaterial(owner, header.AcceptedOwner) || owner.Id != header.Id)
            throw new WorkflowPreparationException("enrollment-header-binding", owner.Id);
    }

    internal static void RequireAcceptedHeader(Barrier barrier, WorkflowPreparationSnapshot header)
    {
        RequireMatchingHeader(barrier.Binding.Owner, header);
        if (barrier.Outcome != Outcome.Accepted || barrier.HeaderIncarnation != header.Token.IncarnationId ||
            barrier.SelectionFingerprint != header.Selection.Fingerprint)
            throw new WorkflowPreparationException("enrollment-header-identity", header.Id);
    }

    internal static void ValidateBinding(WorkflowPreparationEfEnrollmentBinding value)
    {
        if (value is null || value.Owner is not { } owner || owner.Id is not { } id ||
            owner.OperationRevision < 0) throw Corrupt();
        Required(id.OwnerAddress, id.OperationId, owner.LedgerIncarnation, owner.OperationState,
            owner.RequestFingerprint, owner.SnapshotId, owner.SourceFingerprint, owner.SourceProfile,
            owner.CompilerProfile, owner.PreparedProfile, owner.PreparedContentFingerprint,
            value.IntentId, value.TargetAssociation);
    }

    private static void Validate(Barrier value)
    {
        if (value is null) throw Corrupt();
        ValidateBinding(value.Binding);
        Required(value.Incarnation);
        if (value.Outcome == Outcome.Accepted) Required(value.HeaderIncarnation, value.SelectionFingerprint);
        else if (value.Outcome != Outcome.Aborted || value.HeaderIncarnation is not null || value.SelectionFingerprint is not null)
            throw Corrupt();
    }

    private static string ScopeHash(PersistenceScope? scope) => EfRelationalIdentity.Hash(scope is null ? "0" : "1" + scope.Value);
    private static void Required(params string?[] values)
    {
        if (values.Any(string.IsNullOrWhiteSpace)) throw Corrupt();
    }
    private static InvalidDataException Corrupt() => new("The preparation enrollment barrier is corrupt.");

    private static void Shape(JsonElement root)
    {
        Object(root, "formatVersion", "barrier");
        var barrier = root.GetProperty("barrier");
        Object(barrier, "binding", "outcome", "incarnation", "headerIncarnation", "selectionFingerprint");
        var binding = barrier.GetProperty("binding");
        Object(binding, "owner", "intentId", "targetAssociation");
        var owner = binding.GetProperty("owner");
        Object(owner, "id", "ledgerIncarnation", "operationRevision", "operationState", "requestFingerprint",
            "snapshotId", "sourceFingerprint", "sourceProfile", "compilerProfile", "preparedProfile", "preparedContentFingerprint");
        var id = owner.GetProperty("id");
        Object(id, "scope", "ownerAddress", "operationId");
        if (id.GetProperty("scope") is { ValueKind: not JsonValueKind.Null } scope) Object(scope, "value");
    }

    private static void Object(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Corrupt();
        var properties = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Length != names.Length || properties.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            !names.All(name => properties.Contains(name, StringComparer.Ordinal))) throw Corrupt();
    }
}
