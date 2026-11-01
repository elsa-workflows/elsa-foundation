using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Tests;

[Collection("Conversion preparation lifetime")]
public sealed class ValueConversionPreparationTests(ITestOutputHelper output)
{
    [Fact]
    public void Warm_collection_conversion_avoids_per_element_member_discovery()
    {
        var executor = new RuntimeValueConversionExecutor(new MutableRegistry { Target = typeof(Contract) });
        var plan = Plan(collection: CollectionKind.List);
        var source = Source(JsonSerializer.Serialize(Enumerable.Range(0, 128).Select(i => new
        {
            wire_name = $"name-{i}", quantity = i, enabled = true, label = "current"
        })));
        executor.Convert(source, plan);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = executor.Convert(source, plan);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"128 typed elements: {allocated} bytes");
        Assert.Equal(127, result.InlineValue!.Value[127].GetProperty("quantity").GetInt32());
        Assert.True(allocated < 512 * 1024, $"Repeated member discovery allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Warm_conversion_rechecks_values_members_and_required_fields(bool xml)
    {
        var executor = new RuntimeValueConversionExecutor(new MutableRegistry { Target = typeof(Contract) });
        var plan = Plan(xml: xml);
        ValueEnvelope Input(string json, string document) => Source(xml ? document : json);
        foreach (var name in new[] { "first", "second" })
        {
            var result = executor.Convert(Input($$"""{"wire_name":"{{name}}","quantity":1}""", $"<root><wire_name>{name}</wire_name><quantity>1</quantity></root>"), plan);
            Assert.Equal(name, result.InlineValue!.Value.GetProperty("wire_name").GetString());
        }
        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(Input("{}", "<root/>"), plan));
        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(Input("""{"wire_name":"ok","unknown":1}""", "<root><wire_name>ok</wire_name><unknown>1</unknown></root>"), plan));
        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(Input("""{"wire_name":"ok","quantity":"wrong"}""", "<root><wire_name>ok</wire_name><quantity>wrong</quantity></root>"), plan));
    }

    [Fact]
    public void Alias_resolution_is_current_and_same_named_module_types_stay_distinct()
    {
        var registry = new MutableRegistry { Target = DynamicContract(typeof(string)) };
        var executor = new RuntimeValueConversionExecutor(registry);
        var plan = Plan();
        executor.Convert(Source("""{"value":"first"}"""), plan);
        var first = registry.Target;
        registry.Target = DynamicContract(typeof(int));
        Assert.Equal(first.FullName, registry.Target.FullName);
        Assert.NotSame(first, registry.Target);
        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(Source("""{"value":"second"}"""), plan));
        Assert.Equal(42, executor.Convert(Source("""{"value":42}"""), plan).InlineValue!.Value.GetProperty("value").GetInt32());
        registry.Target = null;
        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(Source("""{"value":42}"""), plan));
    }

    [Fact]
    public async Task Concurrent_preparation_keeps_module_shapes_and_values_separate()
    {
        var textType = DynamicContract(typeof(string));
        var numberType = DynamicContract(typeof(int));
        await Task.WhenAll(Enumerable.Range(0, 128).Select(i => Task.Run(() =>
        {
            var text = i % 2 == 0;
            var executor = new RuntimeValueConversionExecutor(new MutableRegistry { Target = text ? textType : numberType });
            var json = text ? $$"""{"value":"text-{{i}}"}""" : $$"""{"value":{{i}}}""";
            var value = executor.Convert(Source(json), Plan()).InlineValue!.Value.GetProperty("value");
            if (text) Assert.Equal($"text-{i}", value.GetString());
            else Assert.Equal(i, value.GetInt32());
        })));
    }

    [Fact]
    public void Member_preparation_does_not_root_collectible_contracts()
    {
        var reference = ConvertCollectible();
        for (var i = 0; i < 10 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ConvertCollectible()
    {
        var type = DynamicContract(typeof(string));
        new RuntimeValueConversionExecutor(new MutableRegistry { Target = type }).Convert(Source("""{"value":"value"}"""), Plan());
        return new WeakReference(type.Assembly);
    }

    private static Type DynamicContract(Type memberType)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"conversion-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("main").DefineType("Plugin.Contract", TypeAttributes.Public);
        var property = builder.DefineProperty("Value", PropertyAttributes.None, memberType, null);
        var getter = builder.DefineMethod("get_Value", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, memberType, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        if (memberType.IsValueType) il.Emit(OpCodes.Ldc_I4_0); else il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);
        return builder.CreateType()!;
    }

    private static ValueEnvelope Source(string text) => ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement(text), ValueProtectionPolicy.InstanceInline);

    private static ValueConversionPlan Plan(bool xml = false, CollectionKind collection = CollectionKind.Single) => new(
        ValueConversionPlan.CurrentSchemaVersion, ValueRepresentation.FormattedContent,
        new ValueTypeDescriptor("String"), new ValueTypeDescriptor("test-contract", collection),
        xml ? ValueConversionMode.Xml : ValueConversionMode.Json, ValueConversionOperation.Profile,
        new ValueConversionProfileReference(xml ? "elsa.xml" : "elsa.json", "1"), ValueConversionLimits.Default, null);

    private sealed class MutableRegistry : IWellKnownTypeRegistry
    {
        public Type? Target { get; set; }
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = "test-contract"; return Target == type; }
        public bool TryGetType(string alias, out Type type) { type = Target!; return Target is not null; }
        public IEnumerable<Type> ListTypes() => Target is null ? [] : [Target];
        public string GetAliasOrDefault(Type type) => TryGetAlias(type, out var alias) ? alias : type.FullName!;
        public Type GetTypeOrDefault(string alias) => Target ?? typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }

    private sealed class Contract
    {
        [JsonPropertyName("wire_name")]
        public required string Name { get; init; }
        public int Quantity { get; init; }
        public bool Enabled { get; init; }
        public string? Label { get; init; }
    }
}

// Other host-composition tests temporarily root loaded assemblies during activity discovery.
[CollectionDefinition("Conversion preparation lifetime", DisableParallelization = true)]
public sealed class ConversionPreparationLifetimeCollection;
