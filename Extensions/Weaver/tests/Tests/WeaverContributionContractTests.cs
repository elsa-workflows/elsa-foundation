using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Tests;

public sealed class WeaverContributionContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_privileges_and_undeclared_codes_preserve_legacy_wire_defaults(bool web)
    {
        var options = Options(web);
        var preparation = Preparation();
        var prepared = Prepared();
        Assert.Equal(JsonSerializer.Serialize(preparation, options),
            JsonSerializer.Serialize(preparation with { RequiredPrivilegeClasses = [] }, options));
        Assert.Equal(JsonSerializer.Serialize(prepared, options),
            JsonSerializer.Serialize(prepared with { RequiredPrivilegeClasses = [] }, options));

        Assert.False(JsonSerializer.SerializeToElement(preparation, options).TryGetProperty(Name("RequiredPrivilegeClasses", web), out _));
        Assert.False(JsonSerializer.SerializeToElement(prepared, options).TryGetProperty(Name("RequiredPrivilegeClasses", web), out _));
        var tool = JsonSerializer.SerializeToElement(Tool(), options);
        Assert.False(tool.TryGetProperty(Name("CanPerformWithoutPersistence", web), out _));
        Assert.False(tool.TryGetProperty(Name("ModuleCodes", web), out _));
        Assert.False(JsonSerializer.SerializeToElement(Source(), options).TryGetProperty(Name("ModuleCodes", web), out _));
        Assert.False(JsonSerializer.SerializeToElement(Receipt(), options).TryGetProperty(Name("ModuleCode", web), out _));
        Assert.False(JsonSerializer.SerializeToElement(new WeaverError("owned.denied", "Denied."), options)
            .TryGetProperty(Name("OwnerUnavailable", web), out _));

        Assert.Null(RoundTrip(preparation, options).RequiredPrivilegeClasses);
        Assert.Null(RoundTrip(prepared, options).RequiredPrivilegeClasses);
        Assert.Null(RoundTrip(Tool(), options).ModuleCodes);
        Assert.Null(RoundTrip(Source(), options).ModuleCodes);
    }

    [Fact]
    public void Preparation_privileges_cannot_change_through_owner_input_or_a_deserialized_list()
    {
        List<WeaverPrivilegeClass> ownerClasses = [WeaverPrivilegeClass.Activation, WeaverPrivilegeClass.Administrative];
        var preparation = Preparation() with { RequiredPrivilegeClasses = ownerClasses };
        var prepared = Prepared() with { RequiredPrivilegeClasses = preparation.RequiredPrivilegeClasses };
        ownerClasses[0] = WeaverPrivilegeClass.Irrecoverable;
        ownerClasses.Clear();

        // Copying freezes the owner's observation without prematurely canonicalizing retained legacy data.
        WeaverPrivilegeClass[] expected = [WeaverPrivilegeClass.Activation, WeaverPrivilegeClass.Administrative];
        Assert.Equal(expected, preparation.RequiredPrivilegeClasses);
        Assert.Equal(expected, prepared.RequiredPrivilegeClasses);
        Assert.Throws<NotSupportedException>(() => ((IList<WeaverPrivilegeClass>)preparation.RequiredPrivilegeClasses!)[0] = WeaverPrivilegeClass.Irrecoverable);
        var restored = RoundTrip(prepared, Options(true));
        Assert.Equal(expected, restored.RequiredPrivilegeClasses);
        Assert.Throws<NotSupportedException>(() => ((IList<WeaverPrivilegeClass>)restored.RequiredPrivilegeClasses!).Clear());

        var replacement = prepared with { RequiredPrivilegeClasses = [WeaverPrivilegeClass.Irrecoverable] };
        Assert.Equal(expected, prepared.RequiredPrivilegeClasses);
        Assert.Equal([WeaverPrivilegeClass.Irrecoverable], replacement.RequiredPrivilegeClasses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Declared_codes_remain_closed_immutable_and_distinct_from_undeclared(bool web)
    {
        var options = Options(web);
        List<string> ownerCodes = ["owned.completed"];
        var tool = Tool() with { ModuleCodes = ownerCodes };
        var source = Source() with { ModuleCodes = ownerCodes };
        ownerCodes.Add("owned.unreviewed");
        Assert.Equal(["owned.completed"], tool.ModuleCodes);
        Assert.Equal(["owned.completed"], source.ModuleCodes);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)tool.ModuleCodes!).Add("owned.unreviewed"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)source.ModuleCodes!).Clear());
        Assert.Equal(["owned.completed"], RoundTrip(tool, options).ModuleCodes);
        Assert.Equal(["owned.completed"], RoundTrip(source, options).ModuleCodes);

        var noToolCodes = Tool() with { ModuleCodes = [] };
        var noSourceCodes = Source() with { ModuleCodes = [] };
        Assert.Empty(RoundTrip(noToolCodes, options).ModuleCodes!);
        Assert.Empty(RoundTrip(noSourceCodes, options).ModuleCodes!);
        Assert.Equal(JsonValueKind.Array, JsonSerializer.SerializeToElement(noToolCodes, options).GetProperty(Name("ModuleCodes", web)).ValueKind);
        Assert.Equal(JsonValueKind.Array, JsonSerializer.SerializeToElement(noSourceCodes, options).GetProperty(Name("ModuleCodes", web)).ValueKind);
        Assert.Null((tool with { ModuleCodes = null }).ModuleCodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void New_outcome_fields_survive_transport_without_changing_the_resource_observation(bool web)
    {
        var options = Options(web);
        var receipt = Receipt() with { Scope = WeaverEffectScope.NoPersistedEffect, ModuleCode = "owned.tested" };
        var restored = RoundTrip(receipt, options);
        Assert.Equal(receipt.Before, restored.Before);
        Assert.Equal(receipt.Before, restored.After);
        Assert.Equal(WeaverEffectScope.NoPersistedEffect, restored.Scope);
        Assert.Equal("owned.tested", restored.ModuleCode);
        Assert.Equal(2, JsonSerializer.SerializeToElement(receipt, options).GetProperty(Name("Scope", web)).GetInt32());

        var error = new WeaverError("owned.unavailable", "The owner could not decide.", 503) { OwnerUnavailable = true, Limit = "example-limit" };
        Assert.Equal(error, RoundTrip(error, options));
        Assert.True(RoundTrip(Tool() with { CanPerformWithoutPersistence = true }, options).CanPerformWithoutPersistence);
    }

    private static JsonSerializerOptions Options(bool web) => new(web ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General);
    private static string Name(string name, bool web) => web ? JsonNamingPolicy.CamelCase.ConvertName(name) : name;
    private static T RoundTrip<T>(T value, JsonSerializerOptions options) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!;
    private static WeaverPayload Payload() => new("owned.preview", "1", JsonSerializer.SerializeToElement(new { value = "safe" }));
    private static WeaverResourceTarget Target() => new("owned", "resource", "resource-1", "revision-1");
    private static WeaverToolDescriptor Tool() => new("owned.test", "Test", "Test the resource.", WeaverToolMutability.Mutating,
        WeaverRisk.ReviewRequired, "{}", [], []) { ModuleId = "owned" };
    private static WeaverContextProviderDescriptor Source() => new("owned.context", "owned", "resource", [], [], []);
    private static WeaverToolPreparation Preparation() => new(Target(), JsonSerializer.SerializeToElement(new { }), WeaverRisk.ReviewRequired, Payload(), true);
    private static WeaverPreparedToolInvocation Prepared() => new("invocation-1", "session-1", "turn-1", "call-1", "owned.test", "1", "owned",
        "actor-1", "tenant-1", Target(), JsonSerializer.SerializeToElement(new { }), "original-digest", WeaverRisk.ReviewRequired, Payload(),
        WeaverExecutionLocation.Server, true, null, DateTimeOffset.UnixEpoch.AddHours(1));
    private static WeaverEffectReceipt Receipt() => new("invocation-1", Target(), Target(), WeaverEffectScope.ServerPersisted, Payload());
}
