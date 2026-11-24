using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class DurableValuePoliciesTests
{
    [Theory]
    [InlineData(DurableValueStorage.External)]
    [InlineData(DurableValueStorage.Custom)]
    public void A_null_preserves_its_minimum_and_profile_independently_of_the_physical_row(DurableValueStorage minimum)
    {
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Audit, minimum, isSensitive: true,
            metadata: new Dictionary<string, string> { [ValuePolicyCombiner.StorageProfileMetadataKey] = "private", ["acme.policy"] = "strict" });
        var stored = DurableValuePolicies.ApplyMinimum(Row(null), policy, "target");
        var reloaded = JsonSerializer.Deserialize<DurableValueState>(JsonSerializer.Serialize(stored))!;
        var actual = DurableValuePolicies.Read(reloaded);
        Assert.Equal(DurableValueLifecycle.Audit, actual.Lifecycle);
        Assert.Equal(minimum, actual.Storage);
        Assert.True(actual.IsSensitive);
        Assert.Equal("private", actual.Metadata[ValuePolicyCombiner.StorageProfileMetadataKey]);
        Assert.Equal("strict", actual.Metadata["acme.policy"]);
        Assert.DoesNotContain(RuntimeMetadataKeys.BoundaryReferenceKey, actual.Metadata.Keys);
        Assert.Equal(DurableValueStorage.Inline, stored.Storage);
        Assert.Equal(JsonValueKind.Null, stored.InlineValue!.Value.ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Substituted_payloads_cannot_bypass_encryption_or_external_storage(bool encryption)
    {
        var minimum = new ValueProtectionPolicy(DurableValueLifecycle.Instance,
            encryption ? DurableValueStorage.Inline : DurableValueStorage.External, requiresEncryption: encryption);
        Assert.Contains("VF-ACT-005", Assert.Throws<InvalidOperationException>(() => DurableValuePolicies.ApplyMinimum(Row(0), minimum, "target")).Message);
    }

    private static DurableValueState Row(object? value) => new("id", "workflow", "output:value", new("Any", null, null),
        DurableValueLifecycle.Instance, DurableValueStorage.Inline, JsonSerializer.SerializeToElement(value), null, null,
        DateTimeOffset.UnixEpoch, new Dictionary<string, string> { [RuntimeMetadataKeys.BoundaryReferenceKey] = "value" });
}
