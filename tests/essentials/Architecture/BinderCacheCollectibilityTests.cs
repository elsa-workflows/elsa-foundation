using Elsa.Api.AspNetCore;
using NativeEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Proves the framework's own internals retain nothing from a collectible module after a request
/// has actually been bound.
/// </summary>
/// <remarks>
/// The shared collectibility fixtures deliberately route AROUND the framework (a hand-rolled
/// <see cref="RequestDelegate"/> over a bare route builder), so a static cache inside
/// <see cref="EndpointRequestBinder"/> was invisible to every existing suite: collectible tests
/// never bind, and binding tests are never collectible. This fixture compiles a contract record and
/// an <c>ApiEndpoint&lt;T&gt;</c> subclass into a collectible context, maps them through
/// <c>MapEndpointGroup(...).MapEndpointsFrom(...)</c>, serves one request so
/// <c>BindAsync&lt;T&gt;</c> executes and populates the binder cache, and then requires the context
/// to collect. The endpoint shape is deliberately a bodyless GET with no Accepts: it passes the
/// OpenAPI lifetime validator cleanly, so a failure here points squarely at framework internals.
/// </remarks>
public sealed class BinderCacheCollectibilityTests
{
    [Fact]
    public void Bound_contract_types_do_not_root_the_collectible_module()
    {
        var (loadContext, assembly, contractType) = CreateBindAndUnload();

        for (var attempt = 0; attempt < 32 && (loadContext.IsAlive || assembly.IsAlive || contractType.IsAlive); attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(contractType.IsAlive, "The bound contract type is still reachable; the binder cache is rooting it.");
        Assert.False(assembly.IsAlive, "The collectible module assembly is still reachable after unload.");
        Assert.False(loadContext.IsAlive, "The collectible load context is still reachable after unload.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference LoadContext, WeakReference Assembly, WeakReference ContractType) CreateBindAndUnload()
    {
        var loadContext = new AssemblyLoadContext("Elsa.BinderProbe", isCollectible: true);
        using var stream = new MemoryStream(FixtureAssembly, writable: false);
        var assembly = loadContext.LoadFromStream(stream);
        var contractType = assembly.GetType("CollectibleFixture.ProbeRequest", throwOnError: true)!;

        using var provider = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var routes = new ProbeEndpointRouteBuilder(provider);
        routes.MapEndpointGroup("Elsa.BinderProbe", BinderProbeJsonContext.Default)
            .MapEndpointsFrom(assembly);
        var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = HttpMethods.Get;
        context.Request.RouteValues["id"] = "probe-1";
        endpoint.RequestDelegate!(context).GetAwaiter().GetResult();
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);

        var references = (new WeakReference(loadContext), new WeakReference(assembly), new WeakReference(contractType));
        loadContext.Unload();
        return references;
    }

    private static readonly byte[] FixtureAssembly = CompileFixture();

    private static byte[] CompileFixture()
    {
        const string source = """
            using NativeEndpoints;
            using System.Threading;
            using System.Threading.Tasks;

            namespace CollectibleFixture;

            public sealed record ProbeRequest(string Id);

            [Get("probe/{id}")]
            public sealed class Endpoint : ApiEndpoint<ProbeRequest>
            {
                public override void Configure(ApiEndpointOptions options) => options.Operation = "Probe";

                public override Task HandleAsync(ProbeRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = GetPlatformAssemblyPaths()
            .Append(typeof(ApiEndpointBase).Assembly.Location)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "Elsa.BinderProbe.Template",
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
            throw new InvalidOperationException($"The binder probe fixture failed to compile:{Environment.NewLine}{diagnostics}");
        }

        return output.ToArray();
    }

    private static IEnumerable<string> GetPlatformAssemblyPaths()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
            return trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal);

        return [typeof(object).Assembly.Location, typeof(Enumerable).Assembly.Location];
    }

    private sealed class ProbeEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

[JsonSerializable(typeof(int))]
internal sealed partial class BinderProbeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
