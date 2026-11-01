using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Studio.Hosting;

namespace Elsa.Studio.Tests;

public sealed class RuntimeConfigurationCollectibilityTests
{
    [Fact]
    public void Runtime_projection_releases_module_owned_configuration_metadata()
    {
        var (type, assembly, json) = SerializeModuleConfiguration();
        Assert.Equal("{\"extensions\":{\"acme\":{\"Label\":\"module-owned\"}}}", json);
        var timeout = Stopwatch.StartNew();
        while ((type.IsAlive || assembly.IsAlive) && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            // System.Text.Json also has a one-second reflection-accessor cache. Fresh stable
            // metadata activity expires those entries; GC alone cannot distinguish that cache
            // from permanent retention by default serializer options.
            JsonSerializer.Serialize(new { StableControl = 1 }, new JsonSerializerOptions
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            });
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            Thread.Sleep(100);
        }
        Assert.False(type.IsAlive, "Runtime serialization retained the module DTO type.");
        Assert.False(assembly.IsAlive, "Runtime serialization retained the module assembly.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Type, WeakReference Assembly, string Json) SerializeModuleConfiguration()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RuntimeSettings_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Settings").DefineType("ModuleSettings", TypeAttributes.Public | TypeAttributes.Class);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var property = builder.DefineProperty("Label", PropertyAttributes.None, typeof(string), null);
        var getter = builder.DefineMethod("get_Label", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, typeof(string), Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "module-owned");
        il.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);
        var type = builder.CreateType()!;
        var runtime = new Dictionary<string, object?>
        {
            ["extensions"] = new Dictionary<string, object?> { ["acme"] = Activator.CreateInstance(type) }
        };
        var endpoint = typeof(StudioHostingExtensions).Assembly.GetType("Elsa.Studio.Hosting.StudioRuntimeEndpoint", true)!;
        var serialize = endpoint.GetMethod("Serialize", BindingFlags.NonPublic | BindingFlags.Static)!;
        var json = (string)serialize.Invoke(null, [runtime])!;
        return (new WeakReference(type), new WeakReference(type.Assembly), json);
    }
}
