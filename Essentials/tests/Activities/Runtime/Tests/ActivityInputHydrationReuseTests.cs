using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Activities.Runtime.Tests;

[Collection(ActivityInputHydrationCollection.Name)]
public sealed class ActivityInputHydrationReuseTests(ITestOutputHelper output)
{
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public void Warm_hydration_avoids_repeated_property_discovery_allocations()
    {
        var hydrator = new ActivityInputHydrator();
        var contract = Contract(StringType);
        var snapshot = Snapshot(contract, StringType, "hello");
        for (var i = 0; i < 32; i++)
            hydrator.Hydrate(new ExampleActivity(), contract, snapshot);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 256; i++)
            hydrator.Hydrate(new ExampleActivity(), contract, snapshot);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Warm hydration allocation for 256 fresh activations: {allocated} bytes.");

        // Includes fresh activity instances, once-only hydration tracking and JSON values. This excludes
        // rebuilding reflection/attribute/nullability metadata on every activation; no timing is asserted.
        Assert.InRange(allocated, 0, 256 * 1024);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reused_type_metadata_keeps_current_values_and_contract_validation(bool absent)
    {
        var hydrator = new ActivityInputHydrator();
        var firstContract = Contract(StringType);
        var first = new ExampleActivity();
        var second = new ExampleActivity();
        hydrator.Hydrate(first, firstContract, Snapshot(firstContract, StringType, "first"));
        hydrator.Hydrate(second, firstContract, Snapshot(firstContract, StringType, "second"));
        Assert.Equal("first", first.Message);
        Assert.Equal("second", second.Message);

        var currentContract = Contract(StringType, required: absent, nullable: absent);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            hydrator.Hydrate(new ExampleActivity(), currentContract, Snapshot(currentContract, StringType, null, absent)));
        Assert.Contains(absent ? "does not accept absence" : "does not accept null", exception.Message);
    }

    [Fact]
    public async Task Concurrent_hydration_keeps_each_activations_values()
    {
        var hydrator = new ActivityInputHydrator();
        var contract = Contract(StringType);
        await Task.WhenAll(Enumerable.Range(0, 128).Select(i => Task.Run(() =>
        {
            var activity = new ExampleActivity();
            var value = i % 2 == 0 ? null : $"message-{i}";
            hydrator.Hydrate(activity, contract, Snapshot(contract, StringType, value));
            Assert.Equal(value, activity.Message);
        })));
    }

    [Fact]
    public void Equal_type_names_from_different_modules_use_their_own_properties()
    {
        var hydrator = new ActivityInputHydrator();
        var stringActivity = NewPluginActivity(typeof(string));
        var numberActivity = NewPluginActivity(typeof(int));
        Assert.Equal(stringActivity.GetType().FullName, numberActivity.GetType().FullName);
        Assert.NotSame(stringActivity.GetType(), numberActivity.GetType());
        var stringContract = Contract(StringType);
        var numberType = new ValueTypeDescriptor("Int32");
        var numberContract = Contract(numberType);

        hydrator.Hydrate(stringActivity, stringContract, Snapshot(stringContract, StringType, "text"));
        hydrator.Hydrate(numberActivity, numberContract, Snapshot(numberContract, numberType, 42));

        Assert.Equal("text", stringActivity.GetType().GetProperty("Message")!.GetValue(stringActivity));
        Assert.Equal(42, numberActivity.GetType().GetProperty("Message")!.GetValue(numberActivity));
    }

    [Fact]
    public void Hydration_metadata_does_not_keep_a_collectible_module_alive()
    {
        var hydrator = new ActivityInputHydrator();
        var assembly = HydrateCollectibleActivity(hydrator);
        for (var i = 0; i < 10 && assembly.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(assembly.IsAlive);
        GC.KeepAlive(hydrator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference HydrateCollectibleActivity(ActivityInputHydrator hydrator)
    {
        var activity = NewPluginActivity(typeof(string));
        var contract = Contract(StringType);
        hydrator.Hydrate(activity, contract, Snapshot(contract, StringType, "collectible"));
        return new WeakReference(activity.GetType().Assembly);
    }

    private static IActivity NewPluginActivity(Type propertyType)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"HydrationPlugin_{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("Plugin").DefineType(
            "Plugin.Activity", TypeAttributes.Public | TypeAttributes.Sealed, typeof(StructuralActivity));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        var field = type.DefineField("_message", propertyType, FieldAttributes.Private);
        var property = type.DefineProperty("Message", PropertyAttributes.None, propertyType, Type.EmptyTypes);
        var getter = type.DefineMethod("get_Message", MethodAttributes.Public | MethodAttributes.SpecialName,
            propertyType, Type.EmptyTypes);
        var getIl = getter.GetILGenerator();
        getIl.Emit(OpCodes.Ldarg_0);
        getIl.Emit(OpCodes.Ldfld, field);
        getIl.Emit(OpCodes.Ret);
        var setter = type.DefineMethod("set_Message", MethodAttributes.Public | MethodAttributes.SpecialName,
            typeof(void), [propertyType]);
        var setIl = setter.GetILGenerator();
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Stfld, field);
        setIl.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
        property.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(ActivityInputAttribute).GetConstructor(Type.EmptyTypes)!, [],
            [typeof(ActivityInputAttribute).GetProperty(nameof(ActivityInputAttribute.Key))!], ["message"]));
        return (IActivity)Activator.CreateInstance(type.CreateType()!)!;
    }

    private static ActivityContract Contract(ValueTypeDescriptor type, bool required = false, bool nullable = true) => new(
        "test/hydration-reuse", "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
        [new ActivityInputContract("message", "Message", type, required, nullable, false, null, ActivityValuePolicy.Default)],
        new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
        ["Done"], new ActivityActivationRequirement("test", "test/hydration-reuse"));

    private static ActivityInputSnapshot Snapshot(ActivityContract contract, ValueTypeDescriptor type, object? value, bool absent = false) => new(
        "invocation", contract.SchemaFingerprint, "bindings",
        new Dictionary<string, ValueEnvelope>
        {
            ["message"] = absent ? ValueEnvelope.Absent(type, ValueProtectionPolicy.InstanceInline)
                : value is null ? ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline)
                : ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline)
        }, DateTimeOffset.UnixEpoch);

    private sealed class ExampleActivity : StructuralActivity
    {
        [ActivityInput(Key = "message")]
        public string? Message { get; set; }

        public string? Unannotated1 { get; set; }
        public string? Unannotated2 { get; set; }
        public string? Unannotated3 { get; set; }
    }
}

// Other host-composition tests enumerate AppDomain assemblies. Collection must run without those
// temporary assembly owners; the concurrent-hydration case creates its own parallel callers.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ActivityInputHydrationCollection
{
    public const string Name = "Activity input hydration metadata lifetime";
}
