using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Elsa.Workflows.Design.Api.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Projects an assembly's HTTP endpoints into contract data: route, verb, permissions, and the schemas of
/// the request and response bodies (spec 150 FR-C14).
/// </summary>
/// <remarks>
/// The benchmark named this the single largest gap: <c>/swagger</c> and <c>/openapi</c> both 404, so every
/// session obtained the publish/execute/instances/activity-executions/value-evidence endpoints by blind
/// probing or by reading an existing consumer suite. A consumer asked to *prove* a workflow works needs
/// all of it, and none of it was published.
/// <para>
/// The endpoint is instantiated exactly the way the API contract tests already do it — FastEndpoints'
/// <c>Factory.Create</c> with stubbed constructor dependencies, then <c>Configure()</c> — so what is
/// published is what the endpoint actually registers, not a hand-maintained description that can rot.
/// Request and response schemas come from <see cref="AuthoringSchemaExporter"/>, the same wire-coupled
/// exporter the submit schema uses.
/// </para>
/// </remarks>
public sealed class EndpointProjector(Diagnostics diagnostics)
{
    private const string SuccessStatusAttributeName = "SuccessStatusAttribute";

    public IReadOnlyList<EndpointContract> Project(Assembly assembly, string assemblyPath, string? owningFeatureId)
    {
        var endpoints = new List<EndpointContract>();

        // ContainsGenericParameters excludes the open-generic endpoint base classes (ElsaEndpoint<,> and
        // friends): concrete types, but not instantiable and not routes in their own right.
        var endpointTypes = TargetAssembly.GetLoadableTypes(assembly)
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } &&
                           typeof(BaseEndpoint).IsAssignableFrom(type));

        foreach (var endpointType in endpointTypes)
        {
            BaseEndpoint endpoint;
            try
            {
                endpoint = CreateEndpoint(endpointType);
                // Factory.Create already runs Configure(); calling it again duplicates whatever the
                // endpoint appends (permissions were emitted twice before this check).
                if (endpoint.Definition.Routes is null or { Length: 0 })
                    endpoint.Configure();
            }
            catch (Exception exception)
            {
                // An endpoint that cannot be configured for projection is a visible omission, never a
                // silent one: a consumer must not read a partial endpoint list as complete.
                diagnostics.Warning(assemblyPath, "ELSACT012",
                    $"Endpoint '{endpointType.FullName}' could not be configured for projection and is absent from the published API surface: {exception.GetBaseException().Message}");
                continue;
            }

            var definition = endpoint.Definition;
            var routes = definition.Routes ?? [];
            if (routes.Length == 0)
                continue;

            var (requestType, responseType) = ContractTypes(endpointType);
            var (successStatuses, successCondition) = ReadSuccessStatus(endpointType);

            foreach (var route in routes.Order(StringComparer.Ordinal))
            {
                foreach (var verb in (definition.Verbs ?? []).Order(StringComparer.Ordinal))
                {
                    endpoints.Add(new EndpointContract(
                        owningFeatureId,
                        verb.ToUpperInvariant(),
                        NormalizeRoute(route),
                        successStatuses,
                        successCondition,
                        definition.AnonymousVerbs is not null,
                        (definition.AllowedPermissions ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                        requestType is null ? null : SafeSchema(requestType, endpointType, assemblyPath),
                        responseType is null ? null : SafeSchema(responseType, endpointType, assemblyPath)));
                }
            }
        }

        return endpoints
            .OrderBy(endpoint => endpoint.Route, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.Verb, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Routes are published with a single leading slash so a consumer can concatenate them onto a base address.</summary>
    private static string NormalizeRoute(string route) => "/" + route.TrimStart('/');

    private static (IReadOnlyList<int>? Statuses, string? Condition) ReadSuccessStatus(Type endpointType)
    {
        var attribute = endpointType.GetCustomAttributesData()
            .FirstOrDefault(candidate => candidate.AttributeType.Name == SuccessStatusAttributeName);
        if (attribute is null)
            return (null, null);

        // params int[] arrives as a single array-typed constructor argument.
        var statuses = attribute.ConstructorArguments.Count > 0 &&
                       attribute.ConstructorArguments[0].Value is IReadOnlyCollection<CustomAttributeTypedArgument> values
            ? values.Select(value => value.Value).OfType<int>().ToArray()
            : [];

        var condition = attribute.NamedArguments
            .FirstOrDefault(argument => argument.MemberName == "Condition").TypedValue.Value as string;

        return statuses.Length == 0 ? (null, condition) : (statuses, condition);
    }

    /// <summary>Request/response types from the FastEndpoints generic base, skipping its sentinel types.</summary>
    private static (Type? Request, Type? Response) ContractTypes(Type endpointType)
    {
        for (var current = endpointType.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType)
                continue;

            var arguments = current.GetGenericArguments().Where(IsContractType).ToArray();
            switch (arguments.Length)
            {
                case >= 2:
                    return (arguments[0], arguments[1]);
                case 1:
                    // A single contract argument is the response on the "without request" bases and the
                    // request on the request-only bases; the base name disambiguates.
                    return current.Name.Contains("WithoutRequest", StringComparison.Ordinal)
                        ? (null, arguments[0])
                        : (arguments[0], null);
            }
        }

        return (null, null);
    }

    private static bool IsContractType(Type type) =>
        type is { IsGenericParameter: false } &&
        type != typeof(object) &&
        type.Name != "EmptyRequest" &&
        type.Name != "EmptyResponse" &&
        !typeof(IMapper).IsAssignableFrom(type);

    private System.Text.Json.JsonElement? SafeSchema(Type type, Type endpointType, string assemblyPath)
    {
        try
        {
            return AuthoringSchemaExporter.ExportSchema(type);
        }
        catch (Exception exception)
        {
            diagnostics.Warning(assemblyPath, "ELSACT013",
                $"Endpoint '{endpointType.FullName}' body type '{type.FullName}' could not be exported as a schema: {exception.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>
    /// Mirrors the endpoint-contract test helper: FastEndpoints' factory with stubbed dependencies, so
    /// <c>Configure()</c> runs without a host.
    /// </summary>
    private static BaseEndpoint CreateEndpoint(Type endpointType)
    {
        var constructor = endpointType
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .First();
        var dependencies = constructor.GetParameters().Select(parameter => ResolveDependency(parameter.ParameterType)).ToArray();

        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) &&
                              method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var rest] &&
                              first.ParameterType == typeof(Action<Microsoft.AspNetCore.Http.DefaultHttpContext>) &&
                              rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        return (BaseEndpoint)create.Invoke(null, [(Action<Microsoft.AspNetCore.Http.DefaultHttpContext>)(_ => { }), dependencies])!;
    }

    private static object ResolveDependency(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var nullLogger = typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments[0]);
            return (nullLogger.GetProperty("Instance")?.GetValue(null) ?? nullLogger.GetField("Instance")?.GetValue(null))!;
        }

        if (type.IsInterface)
            return DispatchProxy.Create(type, typeof(UnusedDependencyProxy));

        return RuntimeHelpers.GetUninitializedObject(type);
    }

    /// <summary>Configure() must not call its dependencies; if one does, the failure is loud and warned.</summary>
    public class UnusedDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(
                $"Endpoint configuration invoked dependency member '{targetMethod?.Name}'; contract projection configures endpoints without a host.");
    }
}
