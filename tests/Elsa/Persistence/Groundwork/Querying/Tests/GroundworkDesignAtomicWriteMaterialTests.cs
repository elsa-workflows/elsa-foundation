using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

public class GroundworkDesignAtomicWriteMaterialTests
{
    [Fact]
    public void Semantically_equal_object_and_dictionary_orders_share_one_fingerprint()
    {
        var first = new RequestMaterial(
            "definition-1",
            JsonSerializer.Deserialize<Dictionary<string, int>>("""{"beta":2,"alpha":1}""")!);
        var second = new RequestMaterial(
            "definition-1",
            JsonSerializer.Deserialize<Dictionary<string, int>>("""{"alpha":1,"beta":2}""")!);

        var firstMaterial = GroundworkDesignAtomicWriteMaterial.Create(
            "workflow.definition.save.v1",
            "1",
            first);
        var secondMaterial = GroundworkDesignAtomicWriteMaterial.Create(
            "workflow.definition.save.v1",
            "1",
            second);

        Assert.Equal(firstMaterial, secondMaterial);
        Assert.Equal("""{"definitionId":"definition-1","values":{"alpha":1,"beta":2}}""", firstMaterial.Json);
    }

    [Fact]
    public void Operation_kind_schema_and_array_order_are_fingerprint_material()
    {
        var one = GroundworkDesignAtomicWriteMaterial.Create("kind-a", "1", new[] { "a", "b" });
        var otherKind = GroundworkDesignAtomicWriteMaterial.Create("kind-b", "1", new[] { "a", "b" });
        var otherSchema = GroundworkDesignAtomicWriteMaterial.Create("kind-a", "2", new[] { "a", "b" });
        var otherOrder = GroundworkDesignAtomicWriteMaterial.Create("kind-a", "1", new[] { "b", "a" });

        Assert.NotEqual(one.Fingerprint, otherKind.Fingerprint);
        Assert.NotEqual(one.Fingerprint, otherSchema.Fingerprint);
        Assert.NotEqual(one.Fingerprint, otherOrder.Fingerprint);
    }

    [Fact]
    public void Null_and_empty_values_remain_distinct()
    {
        var absent = GroundworkDesignAtomicWriteMaterial.Create("kind", "1", new OptionalMaterial(null));
        var empty = GroundworkDesignAtomicWriteMaterial.Create("kind", "1", new OptionalMaterial(""));

        Assert.NotEqual(absent, empty);
    }

    [Fact]
    public void Authoritative_result_round_trips_only_when_its_fingerprint_matches()
    {
        var material = GroundworkDesignAtomicWriteMaterial.Create(
            "workflow.draft.create.v1.result",
            "1",
            new ResultMaterial("draft-1"));

        var roundTripped = GroundworkDesignAtomicWriteMaterial.Deserialize<ResultMaterial>(
            material.Fingerprint,
            material.Json,
            "workflow.draft.create.v1.result",
            "1");

        Assert.Equal(new ResultMaterial("draft-1"), roundTripped);
    }

    [Fact]
    public void Authoritative_result_fingerprint_mismatch_is_rejected()
    {
        var material = GroundworkDesignAtomicWriteMaterial.Create(
            "workflow.draft.create.v1.result",
            "1",
            new ResultMaterial("draft-1"));

        Assert.Throws<GroundworkDesignAtomicWriteCorruptResultException>(() =>
            GroundworkDesignAtomicWriteMaterial.Deserialize<ResultMaterial>(
                "sha256:tampered",
                material.Json,
                "workflow.draft.create.v1.result",
                "1"));
    }

    private sealed record RequestMaterial(string DefinitionId, IReadOnlyDictionary<string, int> Values);
    private sealed record OptionalMaterial(string? Value);
    private sealed record ResultMaterial(string DraftId);
}
