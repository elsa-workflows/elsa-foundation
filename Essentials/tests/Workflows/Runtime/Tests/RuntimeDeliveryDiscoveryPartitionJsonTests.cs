using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Request JSON shape/policy controls; the narrow collection resolver is test isolation, not a host policy.</summary>
public sealed class RuntimeDeliveryDiscoveryPartitionJsonTests
{
    private static readonly PersistenceScope Scope = new("store");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_original_writer_fields_and_order_remain_exact_without_a_null_partition_member()
    {
        var request = Legacy();
        var original = JsonSerializer.Serialize(new { request.Scope, request.Families, request.WorkKinds,
            request.OwnerId, request.Visibility, request.Limit });
        Assert.Equal(original, JsonSerializer.Serialize(request));
        Assert.False(JsonSerializer.SerializeToElement(request).TryGetProperty("Partition", out _));
        var cleared = new RuntimeDeliveryClaimRequest(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1), 100, Partition())
            { Partition = null };
        Assert.Equal(original, JsonSerializer.Serialize(cleared));
        Assert.Throws<NotSupportedException>(() => cleared.RequirePartition(PersistenceAccessContext.Scoped(Scope), Partition(), Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_unmodified_default_claim_reader_remains_explicitly_unsupported_for_existing_collection_types(bool strict)
    {
        // The existing constructor takes IReadOnlyCollection; existing properties expose IReadOnlyList.
        // Preserve those public signatures. JsonConstructor selection must not introduce a different refusal.
        var json = JsonSerializer.Serialize(Legacy());
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<RuntimeDeliveryClaimRequest>(json,
            new JsonSerializerOptions { RespectRequiredConstructorParameters = strict }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Narrow_collection_isolation_preserves_the_old_optional_constructor_and_unspecified_partition(bool strict)
    {
        var original = JsonSerializer.SerializeToNode(Legacy())!.AsObject();
        original.Remove("Limit"); // The preserved constructor's optional limit remains optional in strict mode.
        var restored = original.Deserialize<RuntimeDeliveryClaimRequest>(IsolateExistingCollections(strict))!;
        Assert.Equal(100, restored.Limit);
        Assert.Null(restored.Partition);
        Assert.Throws<NotSupportedException>(() => restored.RequirePartition(PersistenceAccessContext.Scoped(Scope), Partition(), Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_standalone_partition_round_trips_with_the_actual_default_reader_and_recomputes_its_query_key(bool strict)
    {
        var original = Partition();
        var json = JsonSerializer.SerializeToNode(original)!.AsObject();
        json["AuthorityPartitionKey"] = "caller-asserted-key";
        var restored = json.Deserialize<RuntimeDeliveryDiscoveryPartition>(
            new JsonSerializerOptions { RespectRequiredConstructorParameters = strict })!;
        Assert.True(original.Matches(restored));
        Assert.Equal(original.AuthorityPartitionKey, restored.AuthorityPartitionKey);
        Assert.Equal(original.TestScope!.ExpiresAt, restored.TestScope!.ExpiresAt);
        Assert.Equal(original.Authority.Metadata["key"], restored.Authority.Metadata["key"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Narrow_collection_isolation_round_trips_real_new_partition_instead_of_silently_dropping_it(bool strict)
    {
        var partition = Partition();
        var request = new RuntimeDeliveryClaimRequest(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1), 7, partition);
        var json = JsonSerializer.Serialize(request);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("Partition", out _));
        var restored = JsonSerializer.Deserialize<RuntimeDeliveryClaimRequest>(json, IsolateExistingCollections(strict))!;
        var current = restored.RequirePartition(PersistenceAccessContext.Scoped(Scope), partition, Now);
        Assert.True(partition.Matches(current));
        Assert.Equal(7, restored.Limit);
        Assert.Equal(request.Families, restored.Families);
        Assert.Equal(request.WorkKinds, restored.WorkKinds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void New_partition_JSON_cannot_bypass_the_shared_scope_init_guard(bool strict)
    {
        var request = new RuntimeDeliveryClaimRequest(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1), 7, Partition());
        var json = JsonSerializer.SerializeToNode(request)!.AsObject();
        json["Partition"]!["Scope"]!["Value"] = "other-store";
        Assert.Throws<ArgumentException>(() => json.Deserialize<RuntimeDeliveryClaimRequest>(IsolateExistingCollections(strict)));
    }

    [Fact]
    public void The_overload_and_public_init_use_the_same_exact_scope_guard()
    {
        var other = Partition(new("other-store"));
        Assert.Throws<ArgumentException>(() => new RuntimeDeliveryClaimRequest(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1), 1, other));
        Assert.Throws<ArgumentException>(() => new RuntimeDeliveryClaimRequest(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1)) { Partition = other });
        var correct = new RuntimeDeliveryClaimRequest(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1)) { Partition = Partition() };
        Assert.True(correct.Partition!.Matches(Partition()));
    }

    private static RuntimeDeliveryClaimRequest Legacy() => new(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1));
    private static RuntimeDeliveryDiscoveryPartition Partition(PersistenceScope? scope = null)
    {
        var execution = new WorkflowExecutionPartition("execution");
        return new(scope ?? Scope, "tenant", WorkflowRunKind.TestRun,
            new("system", "root", new Dictionary<string, string> { ["key"] = "value" }), execution,
            new("test", Now.AddHours(1), "tenant", execution));
    }

    private static JsonSerializerOptions IsolateExistingCollections(bool strict)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(RuntimeDeliveryClaimRequest)) return;
            foreach (var name in new[] { nameof(RuntimeDeliveryClaimRequest.Families), nameof(RuntimeDeliveryClaimRequest.WorkKinds) })
            {
                var original = info.Properties.Single(property => property.Name == name);
                var replacement = info.CreateJsonPropertyInfo(typeof(IReadOnlyCollection<string>), original.Name);
                replacement.AttributeProvider = original.AttributeProvider;
                replacement.Get = original.Get;
                info.Properties[info.Properties.IndexOf(original)] = replacement;
            }
        });
        return new() { RespectRequiredConstructorParameters = strict, TypeInfoResolver = resolver };
    }
}
