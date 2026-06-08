using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US4 — IActivityDefinitionHasher behaviour. Determinism, property-order independence,
/// and exclusion of fields that would defeat the hash's purpose.
/// </summary>
public sealed class HasherTests
{
    [Fact]
    public void Hash_IsDeterministic_AcrossInvocations()
    {
        var hasher = new DefaultActivityDefinitionHasher();
        var (def, ver) = MakeFixture();

        var h1 = hasher.Hash(def, ver);
        var h2 = hasher.Hash(def, ver);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Hash_IsStable_AcrossReconciledAtDifferences()
    {
        // The hasher excludes ReconciledAt/ReconciledBy — two definitions that differ
        // ONLY in those fields must hash to the same value, otherwise idempotency breaks
        // (every reconciliation pass would generate a fresh hash and trigger a rewrite).
        var hasher = new DefaultActivityDefinitionHasher();
        var (def1, ver) = MakeFixture(provisionedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (def2, _) = MakeFixture(provisionedAt: new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(hasher.Hash(def1, ver), hasher.Hash(def2, ver));
    }

    [Fact]
    public void Hash_Changes_WhenContentChanges()
    {
        var hasher = new DefaultActivityDefinitionHasher();
        var (def1, ver) = MakeFixture(displayName: "Original");
        var (def2, _) = MakeFixture(displayName: "Changed");

        Assert.NotEqual(hasher.Hash(def1, ver), hasher.Hash(def2, ver));
    }

    [Fact]
    public void Hash_IsStable_AcrossVersionId()
    {
        // The version Id is generated random per pass; including it in the hash would
        // defeat idempotency. This test pins that exclusion.
        var hasher = new DefaultActivityDefinitionHasher();
        var (def, _) = MakeFixture();

        var verA = NewVersion(def, id: "id-A");
        var verB = NewVersion(def, id: "id-B");

        Assert.Equal(hasher.Hash(def, verA), hasher.Hash(def, verB));
    }

    private static (IActivityDefinition def, IActivityDefinitionVersion ver) MakeFixture(
        string displayName = "Display",
        DateTimeOffset? provisionedAt = null)
    {
        var def = new FakeDefinition(
            Id: "def-id",
            ActivityTypeKey: "Acme.Foo",
            SourceKind: "Json",
            SourceId: "Acme.Pkg",
            ReconciledAt: provisionedAt ?? new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            ReconciledBy: "test",
            Category: "Test",
            DisplayName: displayName,
            Description: null);

        return (def, NewVersion(def));
    }

    private static IActivityDefinitionVersion NewVersion(IActivityDefinition def, string id = "ver-id") =>
        new FakeVersion(
            Id: id,
            Version: "1.0.0",
            DefinitionId: def.Id,
            ActivityTypeKey: def.ActivityTypeKey,
            DescriptorType: typeof(TypeInformation).FullName!,
            DescriptorPayload: JsonSerializer.SerializeToElement(new TypeInformation("Foo", "Acme", "Acme.Pkg", "1.0.0.0")),
            ExecutionType: ActivityExecutionType.Action,
            Inputs: [],
            Outputs: [],
            Ports: [],
            Definition: def);

    private sealed record FakeDefinition(
        string Id, string ActivityTypeKey, string SourceKind, string SourceId,
        DateTimeOffset ReconciledAt, string? ReconciledBy,
        string Category, string? DisplayName, string? Description
    ) : IActivityDefinition;

    private sealed record FakeVersion(
        string Id, string Version, string DefinitionId, string ActivityTypeKey,
        string DescriptorType, JsonElement DescriptorPayload,
        ActivityExecutionType ExecutionType, IEnumerable<InputDefinition> Inputs, IEnumerable<OutputDefinition> Outputs,
        IEnumerable<ActivityPortDefinition> Ports, IActivityDefinition Definition,
        string? ReconcilliationHash = null
    ) : IActivityDefinitionVersion;
}
