using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Pass the absolute path to the built OpenApiRetention.Contract.dll.");
    return 2;
}

var stable = Repro.Run(args[0], exposeCollectibleContract: false);
var unsafeCycle = Repro.Run(args[0], exposeCollectibleContract: true);
Console.WriteLine($"Stable metadata:      {stable}");
Console.WriteLine($"Collectible metadata: {unsafeCycle}");
return stable.Collected && !unsafeCycle.Collected ? 0 : 1;

internal sealed record CollectionResult(
    bool Collected,
    bool LoadContextAlive,
    bool AssemblyAlive,
    bool ContractTypeAlive,
    bool DelegateAlive,
    bool ProviderAlive);

internal static class Repro
{
    public static CollectionResult Run(string contractPath, bool exposeCollectibleContract)
    {
        var cycle = Create(contractPath, exposeCollectibleContract);
        for (var attempt = 0; attempt < 32 && cycle.AnyAlive; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        return cycle.Result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CycleReferences Create(string contractPath, bool exposeCollectibleContract)
    {
        var loadContext = new ContractLoadContext();
        var assembly = loadContext.LoadFromAssemblyPath(contractPath);
        var implementationType = assembly.GetType("OpenApiRetention.Contract.Implementation", throwOnError: true)!;
        var contractType = assembly.GetType("OpenApiRetention.Contract.Request", throwOnError: true)!;
        var requestType = exposeCollectibleContract ? contractType : typeof(StableRequest);
        var responseType = exposeCollectibleContract ? contractType : typeof(StableResponse);
        RequestDelegate handler = async context =>
        {
            GC.KeepAlive(implementationType);
            await context.Response.WriteAsJsonAsync(new StableResponse("ok"));
        };

        var endpointBuilder = new RouteEndpointBuilder(handler, RoutePatternFactory.Parse("/retention"), 0)
        {
            DisplayName = "POST /retention"
        };
        endpointBuilder.Metadata.Add(new HttpMethodMetadata([HttpMethods.Post]));
        endpointBuilder.Metadata.Add(typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))!);
        endpointBuilder.Metadata.Add(new Accepts(requestType));
        endpointBuilder.Metadata.Add(new Produces(responseType));
        var source = new MutableSource([endpointBuilder.Build()]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new ReproEnvironment());
        services.AddSingleton<EndpointDataSource>(source);
        services.AddOpenApi();
        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = provider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (document.Paths?.ContainsKey("/retention") != true)
            throw new InvalidOperationException("The built-in OpenAPI provider did not describe the endpoint.");

        source.Replace([]);
        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var contractTypeReference = new WeakReference(contractType);
        var handlerReference = new WeakReference(handler);
        var providerReference = new WeakReference(serviceProvider);
        document = null;
        provider = null!;
        serviceProvider.Dispose();
        loadContext.Unload();
        return new CycleReferences(
            loadContextReference,
            assemblyReference,
            contractTypeReference,
            handlerReference,
            providerReference);
    }

    private sealed record StableRequest(string Value);

    private sealed record StableResponse(string Value);

    private sealed class Accepts(Type requestType) : IAcceptsMetadata
    {
        public Type? RequestType { get; } = requestType;
        public bool IsOptional => false;
        public IReadOnlyList<string> ContentTypes { get; } = ["application/json"];
    }

    private sealed class Produces(Type responseType) : IProducesResponseTypeMetadata
    {
        public Type? Type { get; } = responseType;
        public int StatusCode => StatusCodes.Status200OK;
        public IEnumerable<string> ContentTypes { get; } = ["application/json"];
    }

    private sealed class MutableSource(IReadOnlyList<Endpoint> endpoints) : EndpointDataSource
    {
        private IReadOnlyList<Endpoint> _endpoints = endpoints;
        private CancellationTokenSource _change = new();

        public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref _endpoints);

        public override IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _change).Token);

        public void Replace(IReadOnlyList<Endpoint> endpoints)
        {
            Volatile.Write(ref _endpoints, endpoints);
            Interlocked.Exchange(ref _change, new CancellationTokenSource()).Cancel();
        }
    }

    private sealed class ContractLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName assemblyName) => null;
    }

    private sealed class ReproEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(Repro).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record CycleReferences(
        WeakReference LoadContext,
        WeakReference Assembly,
        WeakReference ContractType,
        WeakReference Handler,
        WeakReference Provider)
    {
        public bool AnyAlive =>
            LoadContext.IsAlive ||
            Assembly.IsAlive ||
            ContractType.IsAlive ||
            Handler.IsAlive ||
            Provider.IsAlive;

        public CollectionResult Result => new(
            !AnyAlive,
            LoadContext.IsAlive,
            Assembly.IsAlive,
            ContractType.IsAlive,
            Handler.IsAlive,
            Provider.IsAlive);
    }
}
