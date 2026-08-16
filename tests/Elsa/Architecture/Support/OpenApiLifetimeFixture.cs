using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Architecture.Tests.Support;

internal enum OpenApiContractLifetime
{
    Stable,
    Collectible
}

internal enum StableOpenApiKind
{
    Primary,
    Secondary
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StableOpenApiTextNode), "text")]
internal abstract record StableOpenApiNode;

internal sealed record StableOpenApiTextNode(string Text) : StableOpenApiNode;

internal sealed record StableOpenApiRequest(
    string Value,
    StableOpenApiKind Kind,
    IReadOnlyDictionary<string, StableOpenApiNode?> Attributes);

internal sealed record StableOpenApiResponse(IReadOnlyList<StableOpenApiNode> Nodes, string? ContinuationToken);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(StableOpenApiRequest))]
[JsonSerializable(typeof(StableOpenApiResponse))]
internal sealed partial class StableOpenApiJsonContext : JsonSerializerContext;

internal sealed record OpenApiLifetimeCollectionEvidence(
    bool Collected,
    bool LoadContextAlive,
    bool AssemblyAlive,
    bool ImplementationTypeAlive,
    bool ContractTypeAlive,
    bool DelegateAlive,
    bool ProviderAlive,
    bool SpecificSchemas)
{
    public string Diagnostic =>
        $"Collected={Collected}; LoadContextAlive={LoadContextAlive}; AssemblyAlive={AssemblyAlive}; " +
        $"ImplementationTypeAlive={ImplementationTypeAlive}; ContractTypeAlive={ContractTypeAlive}; " +
        $"DelegateAlive={DelegateAlive}; ProviderAlive={ProviderAlive}.";
}

internal sealed record OpenApiCandidateRejectionEvidence(
    bool PreviousDocumentedBefore,
    bool PreviousDocumentedAfter,
    bool CandidateNeverDocumented,
    bool PreviousCallableAfter,
    UnsafeOpenApiMetadataViolation Violation);

internal sealed record OpenApiAcceptedReplacementEvidence(
    bool PreviousCompleteBefore,
    bool CandidateAbsentBefore,
    bool PreviousAbsentAfter,
    bool CandidateCompleteAfter,
    bool CandidateCallableAfter,
    bool ConcurrentDocumentsComplete);

internal sealed class OpenApiLifetimeCycle(
    OpenApiContractLifetime contractLifetime,
    string operationId,
    WeakReference loadContext,
    WeakReference assembly,
    WeakReference implementationType,
    WeakReference contractType,
    WeakReference requestDelegate,
    WeakReference provider,
    bool specificSchemas)
{
    public OpenApiContractLifetime ContractLifetime { get; } = contractLifetime;
    public string OperationId { get; } = operationId;

    public OpenApiLifetimeCollectionEvidence VerifyCollection(int attempts = 32)
    {
        for (var attempt = 0; attempt < attempts && AnyAlive(); attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        var evidence = new OpenApiLifetimeCollectionEvidence(
            !AnyAlive(),
            loadContext.IsAlive,
            assembly.IsAlive,
            implementationType.IsAlive,
            contractType.IsAlive,
            requestDelegate.IsAlive,
            provider.IsAlive,
            specificSchemas);
        return evidence;
    }

    private bool AnyAlive() =>
        loadContext.IsAlive ||
        assembly.IsAlive ||
        implementationType.IsAlive ||
        contractType.IsAlive ||
        requestDelegate.IsAlive ||
        provider.IsAlive;
}

internal static class OpenApiLifetimeFixture
{
    private const string ImplementationTypeName = "CollectibleOpenApiFixture.EndpointImplementation";
    private const string ContractTypeName = "CollectibleOpenApiFixture.Contract";
    private static readonly byte[] FixtureAssembly = CompileFixture();

    public static OpenApiLifetimeCycle Create(OpenApiContractLifetime contractLifetime)
    {
        var assemblyName = $"Elsa.OpenApi.Lifetime.{contractLifetime}.{Guid.NewGuid():N}";
        return CreateAndUnload(contractLifetime, assemblyName);
    }

    public static OpenApiCandidateRejectionEvidence RejectUnsafeCandidate() => RejectUnsafeCandidateCore();

    public static OpenApiAcceptedReplacementEvidence ReplaceAcceptedGeneration() => ReplaceAcceptedGenerationCore();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OpenApiLifetimeCycle CreateAndUnload(OpenApiContractLifetime contractLifetime, string assemblyName)
    {
        var loadContext = new AssemblyLoadContext(assemblyName, isCollectible: true);
        using var assemblyStream = new MemoryStream(FixtureAssembly, writable: false);
        var assembly = loadContext.LoadFromStream(assemblyStream);
        var implementationType = assembly.GetType(ImplementationTypeName, throwOnError: true)!;
        var collectibleContractType = assembly.GetType(ContractTypeName, throwOnError: true)!;
        var requestType = contractLifetime == OpenApiContractLifetime.Stable
            ? typeof(StableOpenApiRequest)
            : collectibleContractType;
        var responseType = contractLifetime == OpenApiContractLifetime.Stable
            ? typeof(StableOpenApiResponse)
            : collectibleContractType;
        var operationId = $"lifetime-{contractLifetime.ToString().ToLowerInvariant()}";

        RequestDelegate requestDelegate = async context =>
        {
            GC.KeepAlive(implementationType);
            var request = await JsonSerializer.DeserializeAsync(
                    context.Request.Body,
                    StableOpenApiJsonContext.Default.StableOpenApiRequest,
                    context.RequestAborted)
                ?? throw new InvalidOperationException("The source-generated request binder returned null.");
            var response = new StableOpenApiResponse(
                [new StableOpenApiTextNode(request.Value)],
                request.Attributes.Count == 0 ? null : "next");
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                response,
                StableOpenApiJsonContext.Default.StableOpenApiResponse,
                context.RequestAborted);
        };

        var endpoint = BuildEndpoint(
            requestDelegate,
            requestType,
            responseType,
            operationId,
            "/lifetime",
            validateLifetime: contractLifetime == OpenApiContractLifetime.Stable);
        var source = new MutableEndpointDataSource([endpoint]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new FixtureHostEnvironment());
        services.AddSingleton<EndpointDataSource>(source);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddOpenApi();

        var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Invoke(requestDelegate, serviceProvider);
        var documentProvider = serviceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = documentProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (document.Paths is null || !document.Paths.ContainsKey("/lifetime"))
            throw new InvalidOperationException("The real OpenAPI provider did not describe the lifetime fixture endpoint.");
        var documentJson = document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var specificSchemas = documentJson.Contains("\"attributes\"", StringComparison.Ordinal) &&
                              documentJson.Contains("\"nodes\"", StringComparison.Ordinal) &&
                              documentJson.Contains("\"continuationToken\"", StringComparison.Ordinal);

        source.Replace([]);
        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var implementationTypeReference = new WeakReference(implementationType);
        var contractTypeReference = new WeakReference(collectibleContractType);
        var requestDelegateReference = new WeakReference(requestDelegate);
        var providerReference = new WeakReference(serviceProvider);

        document = null;
        documentProvider = null!;
        serviceProvider.Dispose();
        loadContext.Unload();

        return new OpenApiLifetimeCycle(
            contractLifetime,
            operationId,
            loadContextReference,
            assemblyReference,
            implementationTypeReference,
            contractTypeReference,
            requestDelegateReference,
            providerReference,
            specificSchemas);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OpenApiCandidateRejectionEvidence RejectUnsafeCandidateCore()
    {
        var loadContext = new AssemblyLoadContext($"Elsa.OpenApi.Candidate.{Guid.NewGuid():N}", isCollectible: true);
        using var assemblyStream = new MemoryStream(FixtureAssembly, writable: false);
        var assembly = loadContext.LoadFromStream(assemblyStream);
        var collectibleContractType = assembly.GetType(ContractTypeName, throwOnError: true)!;
        RequestDelegate previous = context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        };
        var previousEndpoint = BuildEndpoint(
            previous,
            typeof(StableOpenApiRequest),
            typeof(StableOpenApiResponse),
            "generation-one",
            "/generation-one",
            validateLifetime: true);
        var source = new MutableEndpointDataSource([previousEndpoint]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new FixtureHostEnvironment());
        services.AddSingleton<EndpointDataSource>(source);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddOpenApi();
        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var documentProvider = serviceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var before = documentProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();

        RequestDelegate candidate = _ => Task.CompletedTask;
        var candidateBuilder = BuildEndpointBuilder(
            candidate,
            collectibleContractType,
            collectibleContractType,
            "generation-two",
            "/generation-two");
        var exception = AssertRejected(candidateBuilder);
        var after = documentProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        Invoke(previous, serviceProvider, StatusCodes.Status204NoContent);
        var evidence = new OpenApiCandidateRejectionEvidence(
            before.Paths?.ContainsKey("/generation-one") == true,
            after.Paths?.ContainsKey("/generation-one") == true,
            after.Paths?.ContainsKey("/generation-two") != true,
            true,
            exception.Violations.First(violation => violation.Category == OpenApiLifetimeViolationCategory.RequestType));

        source.Replace([]);
        loadContext.Unload();
        return evidence;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OpenApiAcceptedReplacementEvidence ReplaceAcceptedGenerationCore()
    {
        RequestDelegate previous = context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        };
        RequestDelegate candidate = context =>
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        };
        var previousEndpoint = BuildEndpoint(
            previous,
            typeof(StableOpenApiRequest),
            typeof(StableOpenApiResponse),
            "generation-one",
            "/generation-one",
            validateLifetime: true);
        var candidateEndpoint = BuildEndpoint(
            candidate,
            typeof(StableOpenApiRequest),
            typeof(StableOpenApiResponse),
            "generation-two",
            "/generation-two",
            validateLifetime: true);
        var source = new MutableEndpointDataSource([previousEndpoint]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new FixtureHostEnvironment());
        services.AddSingleton<EndpointDataSource>(source);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddOpenApi();
        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var documentProvider = serviceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var before = documentProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        var beforeJson = SerializeDocument(before);

        var observations = new ConcurrentBag<string>();
        using var ready = new CountdownEvent(8);
        using var start = new ManualResetEventSlim();
        var readers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                ready.Signal();
                start.Wait();
                for (var request = 0; request < 8; request++)
                {
                    var current = documentProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
                    observations.Add(SerializeDocument(current));
                }
            }))
            .ToArray();
        ready.Wait();
        start.Set();
        source.Replace([candidateEndpoint]);
        Task.WaitAll(readers);

        var after = documentProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        var afterJson = SerializeDocument(after);
        Invoke(candidate, serviceProvider, StatusCodes.Status202Accepted);
        return new OpenApiAcceptedReplacementEvidence(
            before.Paths?.ContainsKey("/generation-one") == true,
            before.Paths?.ContainsKey("/generation-two") != true,
            after.Paths?.ContainsKey("/generation-one") != true,
            after.Paths?.ContainsKey("/generation-two") == true,
            true,
            observations.Count == 64 &&
            !string.Equals(beforeJson, afterJson, StringComparison.Ordinal) &&
            observations.All(item =>
                string.Equals(item, beforeJson, StringComparison.Ordinal) ||
                string.Equals(item, afterJson, StringComparison.Ordinal)));
    }

    private static RouteEndpoint BuildEndpoint(
        RequestDelegate requestDelegate,
        Type requestType,
        Type responseType,
        string operationId,
        string route,
        bool validateLifetime)
    {
        var builder = BuildEndpointBuilder(requestDelegate, requestType, responseType, operationId, route);
        if (validateLifetime)
            OpenApiLifetimeValidator.ValidateAndMark(builder);
        return (RouteEndpoint)builder.Build();
    }

    private static RouteEndpointBuilder BuildEndpointBuilder(
        RequestDelegate requestDelegate,
        Type requestType,
        Type responseType,
        string operationId,
        string route)
    {
        var builder = new RouteEndpointBuilder(requestDelegate, RoutePatternFactory.Parse(route), 0)
        {
            DisplayName = operationId
        };
        builder.Metadata.Add(EndpointOwnershipMetadata.Module("Elsa.OpenApi.LifetimeFixture"));
        builder.Metadata.Add(new HttpMethodMetadata([HttpMethods.Post]));
        builder.Metadata.Add(typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))!);
        builder.Metadata.Add(new EndpointNameMetadata(operationId));
        builder.Metadata.Add(new AcceptsMetadata(requestType));
        builder.Metadata.Add(new ProducesMetadata(responseType));
        return builder;
    }

    private static UnsafeOpenApiMetadataException AssertRejected(EndpointBuilder builder)
    {
        try
        {
            OpenApiLifetimeValidator.ValidateAndMark(builder);
        }
        catch (UnsafeOpenApiMetadataException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The unsafe candidate unexpectedly crossed the OpenAPI lifetime boundary.");
    }

    private static void Invoke(
        RequestDelegate requestDelegate,
        IServiceProvider serviceProvider,
        int expectedStatus = StatusCodes.Status200OK)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            Response = { Body = new MemoryStream() }
        };
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"value\":\"ok\",\"kind\":\"primary\",\"attributes\":{\"sample\":{\"kind\":\"text\",\"text\":\"value\"}}}"));
        requestDelegate(context).GetAwaiter().GetResult();
        if (context.Response.StatusCode != expectedStatus)
            throw new InvalidOperationException($"The lifetime fixture returned HTTP {context.Response.StatusCode}.");
    }

    private static string SerializeDocument(OpenApiDocument document) =>
        document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static byte[] CompileFixture()
    {
        const string source = """
            namespace CollectibleOpenApiFixture;

            public sealed class EndpointImplementation;

            public sealed record Contract(string Value);
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "Elsa.OpenApi.Lifetime.Template",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success)
        {
            var diagnostics = string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"The OpenAPI lifetime fixture failed to compile:{Environment.NewLine}{diagnostics}");
        }

        return output.ToArray();
    }

    private sealed class AcceptsMetadata(Type requestType) : IAcceptsMetadata
    {
        public Type? RequestType { get; } = requestType;
        public bool IsOptional => false;
        public IReadOnlyList<string> ContentTypes { get; } = ["application/json"];
    }

    private sealed class ProducesMetadata(Type responseType) : IProducesResponseTypeMetadata
    {
        public Type? Type { get; } = responseType;
        public int StatusCode => StatusCodes.Status200OK;
        public IEnumerable<string> ContentTypes { get; } = ["application/json"];
    }

    private sealed class MutableEndpointDataSource(IReadOnlyList<Endpoint> endpoints) : EndpointDataSource
    {
        private IReadOnlyList<Endpoint> _endpoints = endpoints;
        private CancellationTokenSource _changeTokenSource = new();

        public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref _endpoints);

        public override IChangeToken GetChangeToken() =>
            new CancellationChangeToken(Volatile.Read(ref _changeTokenSource).Token);

        public void Replace(IReadOnlyList<Endpoint> endpoints)
        {
            Volatile.Write(ref _endpoints, endpoints);
            var previous = Interlocked.Exchange(ref _changeTokenSource, new CancellationTokenSource());
            previous.Cancel();
        }
    }

    private sealed class FixtureHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(OpenApiLifetimeFixture).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
