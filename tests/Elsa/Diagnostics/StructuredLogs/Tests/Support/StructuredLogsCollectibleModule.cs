using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeEndpoints;

namespace Elsa.Diagnostics.StructuredLogs.Tests.Support;

/// <summary>Value-only record of what one live Structured Logs composition exercised before unload.</summary>
public sealed record StructuredLogsObservation(
    int RouteCount,
    bool QueryExercised,
    bool StreamStarted,
    bool StreamCancelled,
    bool SerializerExercised,
    bool AuthorizationExercised,
    bool DocumentationGenerated,
    bool DocumentationFirst,
    OpenApiDescriptionInspection OpenApiDescription);

/// <summary>
/// String/value-only API Explorer description evidence. It deliberately does not expose descriptions,
/// endpoint metadata, <see cref="Type"/>, <see cref="MethodInfo"/>, or delegates.
/// </summary>
public sealed record OpenApiDescriptionInspection(
    int DescriptionCount,
    int ModuleOwnedTypeCount,
    int ModuleOwnedMethodInfoCount,
    int ModuleOwnedDelegateCount,
    string ModuleOwnedMetadataKinds,
    bool DescriptionsInspected)
{
    public static readonly OpenApiDescriptionInspection Empty = new(0, 0, 0, 0, string.Empty, false);

    public bool HasModuleOwnedMetadata =>
        ModuleOwnedTypeCount != 0 || ModuleOwnedMethodInfoCount != 0 || ModuleOwnedDelegateCount != 0;
}

/// <summary>
/// The production Structured Logs feature as a collectible module: its host composition, and the query,
/// SSE stream, serializer, authorization, and OpenAPI description evidence it exercises while mapped.
/// </summary>
public static class StructuredLogsCollectibleModule
{
    private const string SerializerTypeName = "Elsa.Diagnostics.StructuredLogs.Endpoints.StructuredLogEntrySerializer";
    private const string SerializerContextTypeName = "Elsa.Diagnostics.StructuredLogs.Endpoints.StructuredLogsJsonContext";

    private static readonly CollectibleModule Module = new()
    {
        FeatureType = typeof(StructuredLogsFeature),
        AssemblyNamePrefix = "Elsa.Diagnostics.StructuredLogs.Collectible",
        ConfigureHost = ConfigureHost,
        ObservedTypeNames = [SerializerContextTypeName]
    };

    public static CollectibleModuleCycle<StructuredLogsObservation> Create(
        RetentionStage retentionStage = RetentionStage.Clean,
        bool generateDocumentation = true,
        bool documentationFirst = false) =>
        CollectibleModuleFixture.Create(Module, retentionStage, host => Observe(host, generateDocumentation, documentationFirst));

    private static void ConfigureHost(IServiceCollection services)
    {
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DiagnosticsStructuredLogs:ServiceName"] = "collectibility",
                ["DiagnosticsStructuredLogs:SourceDisplayName"] = "Collectibility"
            })
            .Build());
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddOpenApi();
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddFoundationIdentityAbstractions();
        services.AddNormalizedAuthenticationType("structured-logs-collectibility");
    }

    private static StructuredLogsObservation Observe(CollectibleModuleHost host, bool generateDocumentation, bool documentationFirst)
    {
        var routeCount = host.Endpoints.Count(endpoint => endpoint is RouteEndpoint);
        if (routeCount == 0)
            throw new InvalidOperationException("The production Structured Logs mapper published no endpoints.");

        var description = generateDocumentation && documentationFirst
            ? GenerateOpenApiDocument(host)
            : OpenApiDescriptionInspection.Empty;
        var queryExercised = ExerciseQuery(host);
        var (streamStarted, streamCancelled) = ExerciseStream(host);
        var serializerExercised = ExerciseSerializer(host);
        var authorizationExercised = ExerciseAuthorization(host);
        if (generateDocumentation && !documentationFirst)
            description = GenerateOpenApiDocument(host);

        return new(
            routeCount,
            queryExercised,
            streamStarted,
            streamCancelled,
            serializerExercised,
            authorizationExercised,
            generateDocumentation && description.DescriptionsInspected,
            documentationFirst,
            description);
    }

    private static bool ExerciseQuery(CollectibleModuleHost host)
    {
        var endpoint = host.FindEndpoint("/recent");
        if (endpoint?.RequestDelegate is null)
            return false;

        using var scope = host.Services.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(IStructuredLogStore)) is not IStructuredLogStore store)
            return false;

        store.AppendAsync(CreateEntry(1, "query")).GetAwaiter().GetResult();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = endpoint.RoutePattern.RawText ?? "/recent";
        context.Request.QueryString = new QueryString("?take=1");
        context.Response.Body = new MemoryStream();
        endpoint.RequestDelegate(context).GetAwaiter().GetResult();
        return context.Response.StatusCode is >= 200 and < 500;
    }

    private static bool ExerciseAuthorization(CollectibleModuleHost host)
    {
        var endpoint = host.FindEndpoint("/recent");
        var authorizeData = endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>();
        if (endpoint is null || authorizeData is null || authorizeData.Count == 0)
            return false;

        using var scope = host.Services.CreateScope();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = AuthorizationPolicy.CombineAsync(policyProvider, authorizeData).GetAwaiter().GetResult();
        if (policy is null)
            return false;

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(IdentityClaimTypes.Normalized, "v1"),
                new Claim(IdentityClaimTypes.Permission, "DIAGNOSTICS:STRUCTUREDLOGS")
            ],
            "structured-logs-collectibility"));
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return authorization.AuthorizeAsync(principal, endpoint, policy).GetAwaiter().GetResult().Succeeded;
    }

    private static (bool Started, bool Cancelled) ExerciseStream(CollectibleModuleHost host)
    {
        var endpoint = host.FindEndpoint("/stream");
        if (endpoint?.RequestDelegate is null)
            return (false, false);

        using var scope = host.Services.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(IStructuredLogStore)) is not IStructuredLogStore store)
            return (false, false);

        store.AppendAsync(CreateEntry(2, "stream")).GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = endpoint.RoutePattern.RawText ?? "/stream";
        context.RequestAborted = cancellation.Token;
        context.Response.Body = new MemoryStream();
        var task = endpoint.RequestDelegate(context);
        // DefaultHttpContext has no server response feature to flip HasStarted. The production handler
        // establishes the observable SSE boundary by setting the content type and preamble headers before
        // it enters its first asynchronous wait, which is the relevant signal in this direct invocation.
        var started = SpinUntil(
            () => string.Equals(context.Response.ContentType, "text/event-stream", StringComparison.Ordinal),
            TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        try
        {
            task.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Cancellation is an expected terminal path for a live stream.
        }

        return (started, cancellation.IsCancellationRequested);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ExerciseSerializer(CollectibleModuleHost host)
    {
        var serializerType = host.ModuleAssembly.GetType(SerializerTypeName);
        var serializer = serializerType is null ? null : host.Services.GetService(serializerType);
        var method = serializerType?.GetMethod(
            "Serialize",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(StructuredLogEntry)],
            modifiers: null);
        if (serializer is null || method is null)
            return false;

        _ = method.Invoke(serializer, [CreateEntry(3, "serializer")]);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OpenApiDescriptionInspection GenerateOpenApiDocument(CollectibleModuleHost host)
    {
        var provider = host.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = provider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        var descriptions = host.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .ToArray();
        var inspection = InspectDescriptions(descriptions, host.ModuleAssembly);
        var paths = document.Paths?.Count(path => path.Key.Contains("structured-logs", StringComparison.OrdinalIgnoreCase)) ?? 0;
        return inspection with { DescriptionsInspected = inspection.DescriptionsInspected && paths > 0 };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OpenApiDescriptionInspection InspectDescriptions(
        IReadOnlyCollection<ApiDescription> descriptions,
        Assembly moduleAssembly)
    {
        var moduleLoadContext = AssemblyLoadContext.GetLoadContext(moduleAssembly)
            ?? throw new InvalidOperationException("The collectible Structured Logs assembly has no load context.");
        var typeCount = 0;
        var methodCount = 0;
        var delegateCount = 0;
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in descriptions.SelectMany(description => description.ActionDescriptor.EndpointMetadata))
            InspectMetadata(item, moduleLoadContext, ref typeCount, ref methodCount, ref delegateCount, kinds);

        return new OpenApiDescriptionInspection(
            descriptions.Count,
            typeCount,
            methodCount,
            delegateCount,
            string.Join(",", kinds.Order(StringComparer.Ordinal)),
            DescriptionsInspected: true);
    }

    private static void InspectMetadata(
        object? item,
        AssemblyLoadContext moduleLoadContext,
        ref int typeCount,
        ref int methodCount,
        ref int delegateCount,
        ISet<string> kinds)
    {
        if (item is null)
            return;

        Classify(item, moduleLoadContext, ref typeCount, ref methodCount, ref delegateCount, kinds);
        foreach (var field in item.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(Type) ||
                typeof(MethodInfo).IsAssignableFrom(field.FieldType) ||
                typeof(Delegate).IsAssignableFrom(field.FieldType))
            {
                Classify(field.GetValue(item), moduleLoadContext, ref typeCount, ref methodCount, ref delegateCount, kinds);
            }
        }
    }

    private static void Classify(
        object? value,
        AssemblyLoadContext moduleLoadContext,
        ref int typeCount,
        ref int methodCount,
        ref int delegateCount,
        ISet<string> kinds)
    {
        switch (value)
        {
            case Type type when IsModuleOwned(type.Assembly, moduleLoadContext):
                typeCount++;
                kinds.Add("Type");
                break;
            case MethodInfo { DeclaringType: { } methodType } when IsModuleOwned(methodType.Assembly, moduleLoadContext):
                methodCount++;
                kinds.Add("MethodInfo");
                break;
            case Delegate { Method.DeclaringType: { } delegateType } when IsModuleOwned(delegateType.Assembly, moduleLoadContext):
                delegateCount++;
                kinds.Add("Delegate");
                break;
        }
    }

    private static bool IsModuleOwned(Assembly assembly, AssemblyLoadContext moduleLoadContext) =>
        ReferenceEquals(AssemblyLoadContext.GetLoadContext(assembly), moduleLoadContext);

    private static bool SpinUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
            Thread.Yield();
        return predicate();
    }

    private static StructuredLogEntry CreateEntry(long sequence, string message) => new()
    {
        Sequence = sequence,
        Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        Level = LogLevel.Information,
        Category = "Collectibility",
        Message = message,
        SourceId = "collectibility"
    };
}
