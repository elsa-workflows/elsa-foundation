using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

// ADR 0032 R1 / spec 107: the side-effect profile is part of the pinned, fingerprinted contract.
public sealed class SideEffectProfileContractTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");

    private static ActivityContract NewContract(SideEffectProfile? profile) => profile is { } value
        ? new ActivityContract(
            "test/pure",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, false, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/pure"),
            value)
        : new ActivityContract(
            "test/pure",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, false, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/pure"));

    [Fact]
    public void Unmarked_contract_defaults_to_External()
    {
        Assert.Equal(SideEffectProfile.External, NewContract(profile: null).SideEffectProfile);
    }

    [Fact]
    public void Changing_the_profile_changes_the_fingerprint()
    {
        var external = NewContract(SideEffectProfile.External);
        var replaySafe = NewContract(SideEffectProfile.ReplaySafe);

        Assert.NotEqual(external.SchemaFingerprint, replaySafe.SchemaFingerprint);
    }

    [Fact]
    public void Default_profile_does_not_churn_the_fingerprint()
    {
        // Explicit External and the profile-unaware constructor path must produce a byte-identical
        // fingerprint, so no existing default-External golden/fixture moves under this unit.
        var unaware = NewContract(profile: null);
        var explicitExternal = NewContract(SideEffectProfile.External);

        Assert.Equal(unaware.SchemaFingerprint, explicitExternal.SchemaFingerprint);
    }

    [Fact]
    public void ReplaySafe_contract_round_trips_through_json_and_validates_the_fingerprint()
    {
        var original = NewContract(SideEffectProfile.ReplaySafe);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ActivityContract>(json)!;

        Assert.Equal(SideEffectProfile.ReplaySafe, restored.SideEffectProfile);
        Assert.Equal(original.SchemaFingerprint, restored.SchemaFingerprint);
    }
}
