using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class JsonConversionLifetimeTests
{
    [Fact]
    public async Task Scalar_optimization_does_not_cache_collectible_enum_contracts_in_the_baseline()
    {
        var type = ExerciseEnum();
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        _ = JsonSerializer.SerializeToElement(new Canary(), new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() });
        for (var round = 0; round < 12; round++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(type.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExerciseEnum()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("S9CollectibleEnum"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("contract").DefineEnum("PluginState", TypeAttributes.Public, typeof(int));
        builder.DefineLiteral("Ready", 1);
        var type = builder.CreateTypeInfo()!.AsType();
        using var document = JsonDocument.Parse("1");
        var contract = JsonConversionContract.For(type);
        var value = contract.Read(document.RootElement, type);
        Assert.Equal(1, Convert.ToInt32(value));
        return new WeakReference(type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stable_roots_do_not_retain_nested_collectible_conversion_metadata(bool annotated)
    {
        var type = Exercise(annotated);
        // STJ's transient reflection accessor cache expires after a second and evicts on its next access.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        _ = JsonSerializer.SerializeToElement(new Canary(), new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() });
        for (var round = 0; round < 12; round++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(type.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Exercise(bool annotated)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("S9CollectibleConversion"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("contract").DefineType("PluginOrder", TypeAttributes.Public | TypeAttributes.Class);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        if (annotated)
            builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(JsonConversionAttribute).GetConstructor(Type.EmptyTypes)!, [],
                [typeof(JsonConversionAttribute).GetProperty(nameof(JsonConversionAttribute.NamingPolicy))!], [JsonKnownNamingPolicy.SnakeCaseLower]));
        var property = builder.DefineProperty("CustomerName", PropertyAttributes.None, typeof(string), null);
        var getter = builder.DefineMethod("get_CustomerName", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, typeof(string), Type.EmptyTypes);
        var il = getter.GetILGenerator(); il.Emit(OpCodes.Ldstr, "Ada"); il.Emit(OpCodes.Ret); property.SetGetMethod(getter);
        // A member override of an annotated recursive root also creates collection-local options.
        // Those options must be released with the operation, including their collectible element metadata.
        var children = builder.DefineProperty("Children", PropertyAttributes.None, builder.MakeArrayType(), null);
        var childrenGetter = builder.DefineMethod("get_Children", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, builder.MakeArrayType(), Type.EmptyTypes);
        var childrenIl = childrenGetter.GetILGenerator();
        childrenIl.Emit(OpCodes.Ldc_I4_0); childrenIl.Emit(OpCodes.Newarr, builder); childrenIl.Emit(OpCodes.Ret); children.SetGetMethod(childrenGetter);
        var type = builder.CreateType()!;
        var instance = Activator.CreateInstance(type)!;
        var key = annotated ? "customer_name" : "customerName";
        Assert.Equal("Ada", JsonConversionContract.SerializeToElement<object>(instance).GetProperty(key).GetString());
        Assert.Equal("Ada", JsonConversionContract.SerializeToElement(new Holder { Value = instance }).GetProperty("value").GetProperty(key).GetString());
        Assert.Equal("Ada", JsonConversionContract.SerializeToElement(new List<object> { instance })[0].GetProperty(key).GetString());
        Assert.Equal("Ada", JsonConversionContract.SerializeToElement(new Dictionary<string, object> { ["KeepKey"] = instance }).GetProperty("KeepKey").GetProperty(key).GetString());
        // Include a collectible root read and an explicit stable-root options consumer.
        _ = JsonConversionContract.Deserialize("{}", type);
        var member = typeof(MemberHolder).GetProperty(nameof(MemberHolder.Value))!;
        var memberContract = JsonConversionContract.For(type, member);
        Assert.Same(memberContract, JsonConversionContract.For(type, member));
        Assert.NotSame(memberContract, JsonConversionContract.For(typeof(object), member));
        var memberWritten = memberContract.Write(instance, type);
        Assert.Equal("Ada", memberWritten.GetProperty("customerName").GetString());
        Assert.Empty(memberWritten.GetProperty("children").EnumerateArray());
        Assert.True(JsonConversionContract.For(typeof(object)).Options.IsReadOnly);
        _ = JsonSerializer.SerializeToElement<object>(instance, JsonConversionContract.For(typeof(object)).Options);
        // A stable scalar contract is also an options provider and accepts an explicit requested type.
        // Neither route may turn the scalar read optimization into a global plugin-metadata cache.
        _ = JsonSerializer.SerializeToElement<object>(instance, JsonConversionContract.For(typeof(string)).Options);
        using var empty = JsonDocument.Parse("{}");
        _ = JsonConversionContract.For(typeof(string)).Read(empty.RootElement, type);
        return new WeakReference(type);
    }

    private sealed class Holder { public object? Value { get; init; } }
    private sealed class MemberHolder { [JsonConversion] public object? Value { get; init; } }
    private sealed class Canary { public string Name => "canary"; }
}
