using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Elsa.Api.AspNetCore;

/// <summary>
/// A self-describing HTTP endpoint: one class carrying its route, verb, metadata, and handling.
/// </summary>
/// <remarks>
/// The class declares its route and verb with an attribute such as <c>[Post("definitions")]</c> and
/// refines anything dynamic — body mode, status codes, permissions — by overriding
/// <see cref="Configure"/>. Registration is explicit and module-local: the owning module maps its
/// endpoint classes from its own assembly at composition time. There is no process-global discovery
/// and no static registry, which is what made previous endpoint frameworks unloadable; scanning a
/// module's own assembly inside its own mapping call retains nothing beyond the endpoint generation.
/// <para>
/// <see cref="Configure"/> is invoked once at mapping time on an uninitialized instance, so it must
/// not touch constructor-injected state. Handling instances are created per request from the request
/// services, so constructor injection works as it does for any scoped service.
/// </para>
/// </remarks>
public abstract class ApiEndpointBase
{
    /// <summary>The current request. Set before the handler runs; not available in <see cref="Configure"/>.</summary>
    public HttpContext HttpContext { get; set; } = null!;

    /// <summary>Refines the endpoint definition beyond what its attributes declare.</summary>
    public virtual void Configure(ApiEndpointOptions options)
    {
    }
}

/// <summary>An endpoint that binds a request contract and returns a JSON response body.</summary>
public abstract class ApiEndpoint<TRequest, TResponse> : ApiEndpointBase
    where TResponse : notnull
{
    public abstract Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>An endpoint that binds a request contract and returns no content.</summary>
public abstract class ApiEndpoint<TRequest> : ApiEndpointBase
{
    public abstract Task HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>An endpoint that takes no request contract and returns a JSON response body.</summary>
public abstract class ApiEndpointWithoutRequest<TResponse> : ApiEndpointBase
    where TResponse : notnull
{
    public abstract Task<TResponse> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>An endpoint that binds a request contract and picks the response status per result.</summary>
/// <remarks>
/// For operations whose status code is decided by the outcome rather than fixed by the route. The
/// handler stays a pure function: it returns the response paired with its status, and the mapper
/// writes it with the owning module's source-generated serializer metadata.
/// </remarks>
public abstract class ApiEndpointWithResult<TRequest, TResponse> : ApiEndpointBase
    where TResponse : notnull
{
    public abstract Task<EndpointResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>The endpoint definition an <see cref="ApiEndpointBase.Configure"/> override refines.</summary>
public sealed class ApiEndpointOptions
{
    private readonly List<Action<IEndpointConventionBuilder>> _conventions = [];

    public string? Method { get; set; }
    public string? Route { get; set; }

    /// <summary>The stable operation identifier used in the endpoint name and inventory.</summary>
    public string? Operation { get; set; }

    /// <summary>Content types the request is accepted as. Also decides whether a request schema is documented.</summary>
    public string[]? Accepts { get; set; }

    public EndpointBodyMode? BodyMode { get; set; }
    public int SuccessStatus { get; set; } = StatusCodes.Status200OK;

    /// <summary>The status the OpenAPI document declares, when it deliberately differs from the runtime status.</summary>
    public int? DocumentedStatus { get; set; }

    /// <summary>Registers an ordinary ASP.NET Core convention to apply to the mapped endpoint.</summary>
    public ApiEndpointOptions Convention(Action<IEndpointConventionBuilder> convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        _conventions.Add(convention);
        return this;
    }

    /// <summary>The conventions registered by <see cref="Convention"/>, applied by the mapper after mapping.</summary>
    public IReadOnlyList<Action<IEndpointConventionBuilder>> Conventions => _conventions;
}

/// <summary>Declares the route and verb of an <see cref="ApiEndpointBase"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class RouteAttribute(string method, string route) : Attribute
{
    public string Method { get; } = method;
    public string Route { get; } = route;
}

public sealed class GetAttribute(string route) : RouteAttribute("GET", route);
public sealed class PostAttribute(string route) : RouteAttribute("POST", route);
public sealed class PutAttribute(string route) : RouteAttribute("PUT", route);
public sealed class PatchAttribute(string route) : RouteAttribute("PATCH", route);
public sealed class DeleteAttribute(string route) : RouteAttribute("DELETE", route);

/// <summary>
/// An attribute that applies endpoint metadata when its endpoint class is mapped. Lets other layers
/// contribute class-level attributes — such as a permission requirement — without this project
/// referencing them.
/// </summary>
public interface IEndpointConventionAttribute
{
    void Apply(IEndpointConventionBuilder builder);
}
