using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.Compatibility.Testing.Collectibility;

/// <summary>
/// A collectible endpoint lifecycle and its weak-only observation handles.
/// </summary>
public sealed class CollectibleEndpointCycle : IDisposable
{
    private bool _disposed;

    internal CollectibleEndpointCycle(
        Guid cycleId,
        RetentionStage requestedStage,
        string assemblyName,
        string endpointTypeName,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference endpointType)
    {
        CycleId = cycleId;
        RequestedStage = requestedStage;
        AssemblyName = assemblyName;
        EndpointTypeName = endpointTypeName;
        LoadContext = loadContext;
        Assembly = assembly;
        EndpointType = endpointType;
    }

    public Guid CycleId { get; }

    public RetentionStage RequestedStage { get; }

    public string AssemblyName { get; }

    public string EndpointTypeName { get; }

    /// <summary>Weak reference to the collectible load context; never a strong context reference.</summary>
    public WeakReference LoadContext { get; }

    /// <summary>Weak reference to the fixture assembly; never a strong assembly reference.</summary>
    public WeakReference Assembly { get; }

    /// <summary>Weak reference to the generated endpoint type; never a strong type reference.</summary>
    public WeakReference EndpointType { get; }

    public void ReleaseRetention() => RetentionStageProbe.Release(CycleId);

    public UnloadEvidence VerifyCollection(int maxAttempts = UnloadEvidence.DefaultMaxCollectionAttempts) =>
        UnloadEvidence.Verify(this, maxAttempts);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseRetention();
    }
}

/// <summary>
/// Compiles and loads a tiny endpoint assembly in an isolated collectible context.
/// </summary>
public static class CollectibleEndpointFixture
{
    private const string EndpointTypeName = "CollectibleFixture.Endpoint";
    private static readonly byte[] FixtureAssembly = CompileFixture();
    private static int _assemblyNumber;

    /// <summary>
    /// Creates one isolated lifecycle. The returned value contains only weak references to
    /// collectible objects, even when a deliberate retention stage is requested.
    /// </summary>
    public static CollectibleEndpointCycle Create(RetentionStage retentionStage = RetentionStage.Clean)
    {
        var cycleId = Guid.NewGuid();
        var assemblyName = $"Elsa.Collectible.Endpoint.{Interlocked.Increment(ref _assemblyNumber)}";
        return CreateAndUnload(cycleId, assemblyName, retentionStage);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CollectibleEndpointCycle CreateAndUnload(Guid cycleId, string assemblyName, RetentionStage retentionStage)
    {
        var loadContext = new AssemblyLoadContext(assemblyName, isCollectible: true);
        using var stream = new MemoryStream(FixtureAssembly, writable: false);
        var assembly = loadContext.LoadFromStream(stream);
        var endpointType = assembly.GetType(EndpointTypeName, throwOnError: true)!;

        switch (retentionStage)
        {
            case RetentionStage.Route:
                RequestDelegate requestDelegate = _ =>
                {
                    GC.KeepAlive(endpointType);
                    return Task.CompletedTask;
                };
                var route = new RouteEndpointBuilder(requestDelegate, RoutePatternFactory.Parse("/collectible"), 0).Build();
                RetentionStageProbe.PublishRoute(cycleId, route);
                break;
            case RetentionStage.Services:
                var services = new ServiceCollection();
                services.AddSingleton(endpointType, Activator.CreateInstance(endpointType)!);
                var serviceProvider = services.BuildServiceProvider();
                _ = serviceProvider.GetRequiredService(endpointType);
                RetentionStageProbe.PublishServices(cycleId, serviceProvider);
                break;
            case RetentionStage.Serializer:
                var serializerOptions = new JsonSerializerOptions
                {
                    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
                };
                _ = serializerOptions.GetTypeInfo(endpointType);
                RetentionStageProbe.PublishSerializer(cycleId, serializerOptions);
                break;
            case RetentionStage.Harness:
                RetentionStageProbe.PublishHarness(cycleId, endpointType);
                break;
            case RetentionStage.Clean:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage, null);
        }

        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var endpointTypeReference = new WeakReference(endpointType);
        loadContext.Unload();

        // Do not return any collectible object, Type, Assembly, or context. Only the three
        // weak references above cross the NoInlining boundary.
        return new CollectibleEndpointCycle(
            cycleId,
            retentionStage,
            assemblyName,
            EndpointTypeName,
            loadContextReference,
            assemblyReference,
            endpointTypeReference);
    }

    private static byte[] CompileFixture()
    {
        const string source = """
            namespace CollectibleFixture;

            public sealed class Endpoint
            {
                public static string Route => "/collectible";
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = GetPlatformAssemblyPaths()
        .Where(File.Exists)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "Elsa.Collectible.Endpoint.Template",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success)
        {
            var diagnostics = string.Join(Environment.NewLine,
                result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"The collectible endpoint fixture failed to compile:{Environment.NewLine}{diagnostics}");
        }

        return output.ToArray();
    }

    private static IEnumerable<string> GetPlatformAssemblyPaths()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
            return trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal);

        return [typeof(object).Assembly.Location, typeof(Enumerable).Assembly.Location, typeof(Console).Assembly.Location];
    }
}
