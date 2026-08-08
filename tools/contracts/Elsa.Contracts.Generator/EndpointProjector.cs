using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Elsa.Workflows.Design.Api.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    // Set while the endpoint under projection runs Configure(), if it read a host options object.
    private bool _readConfiguration;

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
            _readConfiguration = false;
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
                        responseType is null ? null : SafeSchema(responseType, endpointType, assemblyPath),
                        requestType?.FullName,
                        responseType?.FullName,
                        _readConfiguration));
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
    private BaseEndpoint CreateEndpoint(Type endpointType)
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

    private object ResolveDependency(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var nullLogger = typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments[0]);
            return (nullLogger.GetProperty("Instance")?.GetValue(null) ?? nullLogger.GetField("Instance")?.GetValue(null))!;
        }

        if (ResolveOptions(type) is { } options)
            return options;

        if (type.IsInterface)
        {
            var proxy = DispatchProxy.Create(type, typeof(HostAbsentProxy));
            ((HostAbsentProxy)proxy).Bind(() => _readConfiguration = true);
            return proxy;
        }

        // GetUninitializedObject cannot make an abstract class (TimeProvider is the one in practice).
        // Framework abstractions of that shape expose a static singleton — prefer it, and fall back to a
        // null argument, which binds fine because Configure() never touches the dependency.
        if (type.IsAbstract)
            return StaticSingleton(type)!;

        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static object? StaticSingleton(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => type.IsAssignableFrom(property.PropertyType))
            .Select(property => property.GetValue(null))
            .FirstOrDefault(value => value is not null);

    /// <summary>
    /// <c>IOptions&lt;T&gt;</c> and friends resolve to the options type's own compiled defaults, so endpoints
    /// whose <c>Configure()</c> reads configuration still project — the identity token endpoint is the one
    /// that matters most, since a consumer that cannot find it cannot authenticate at all. What such an
    /// endpoint publishes is the *default* configuration, so the fact is flagged
    /// (<c>configurationDependent</c>) rather than passed off as fixed.
    /// </summary>
    private object? ResolveOptions(Type type)
    {
        if (!type.IsGenericType)
            return null;

        var definition = type.GetGenericTypeDefinition();
        if (definition != typeof(IOptions<>) && definition != typeof(IOptionsMonitor<>) && definition != typeof(IOptionsSnapshot<>))
            return null;

        var optionsType = type.GenericTypeArguments[0];
        if (optionsType.GetConstructor(Type.EmptyTypes) is null)
            return null;

        var proxy = DispatchProxy.Create(type, typeof(DefaultOptionsProxy));
        ((DefaultOptionsProxy)proxy).Bind(Activator.CreateInstance(optionsType)!, () => _readConfiguration = true);
        return proxy;
    }

    /// <summary>
    /// Stands in for a service when there is no host: every query answers "absent". The identity token
    /// endpoint is the case that matters — it filters its auth schemes by what the host registered, and
    /// throwing there dropped the one endpoint a consumer needs before it can call anything else. Answering
    /// "absent" configures the endpoint against an empty host, which is exactly what a build-time
    /// projection knows; the endpoint is flagged <c>configurationDependent</c> so the reader knows the auth
    /// (and any host-derived route) is this build's default rather than a fixed fact.
    /// </summary>
    public class HostAbsentProxy : DispatchProxy
    {
        private Action? _onRead;

        internal void Bind(Action onRead) => _onRead = onRead;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _onRead?.Invoke();
            return AbsentResult(targetMethod?.ReturnType);
        }

        private static object? AbsentResult(Type? returnType)
        {
            if (returnType is null || returnType == typeof(void))
                return null;

            // Async members must still hand back an awaitable, or Configure() faults instead of seeing "absent".
            if (returnType.IsGenericType)
            {
                var definition = returnType.GetGenericTypeDefinition();
                var argument = returnType.GenericTypeArguments[0];
                if (definition == typeof(Task<>))
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(argument)
                        .Invoke(null, [Default(argument)]);
                if (definition == typeof(ValueTask<>))
                    return Activator.CreateInstance(returnType, Default(argument));
            }

            return returnType == typeof(Task) ? Task.CompletedTask : Default(returnType);
        }

        private static object? Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    /// <summary>Serves the options type's compiled defaults and records that configuration was consulted.</summary>
    public class DefaultOptionsProxy : DispatchProxy
    {
        private object? _value;
        private Action? _onRead;

        internal void Bind(object value, Action onRead)
        {
            _value = value;
            _onRead = onRead;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            // Value / CurrentValue / Get(name) — every accessor these interfaces expose.
            _onRead?.Invoke();
            return _value;
        }
    }
}
