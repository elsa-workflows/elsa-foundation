using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using FastEndpoints;

namespace Elsa.Api.Compatibility.Testing.Endpoints;

// Test-only FastEndpoints bases.
//
// Elsa no longer ships first-party FastEndpoints endpoint bases; they were removed when the REST
// consolidation program retired FastEndpoints from the first-party path. What remains is the
// guarantee that a *third-party* endpoint, built on the FastEndpoints package's own bases, still
// receives Elsa's endpoint permission composition and standard metadata, and still coexists with
// first-party Minimal APIs in one host.
//
// These types are how the retained guards keep asserting that. They derive from the third-party
// bases directly and delegate to EndpointPermissionPolicy, which is the same production rule the
// removed first-party bases delegated to. That matters: reimplementing the composition here would
// leave those guards asserting a copy of the rule instead of the rule, which stays green while
// protecting nothing.
//
// They deliberately keep the names the removed bases had, so the guards that derive from them
// changed only their using directive. The namespace is what marks them test-local.

/// <summary>Test-only base for a permissioned FastEndpoints endpoint with no request.</summary>
public abstract class ElsaEndpointWithoutRequest : EndpointWithoutRequest
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(EndpointPermissionPolicy.ComposePolicy(permissions));
        Description(EndpointPermissionPolicy.StandardMetadata(
            GetType(), permissions, EndpointAuthoringModels.FastEndpoints));
    }
}

/// <summary>Test-only base for a permissioned FastEndpoints endpoint with no request and a response.</summary>
public abstract class ElsaEndpointWithoutRequest<TResponse> : EndpointWithoutRequest<TResponse>
    where TResponse : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(EndpointPermissionPolicy.ComposePolicy(permissions));
        Description(EndpointPermissionPolicy.StandardMetadata(
            GetType(), permissions, EndpointAuthoringModels.FastEndpoints));
    }
}

/// <summary>Test-only base for a permissioned FastEndpoints endpoint with a request.</summary>
public class ElsaEndpoint<TRequest> : Endpoint<TRequest> where TRequest : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(EndpointPermissionPolicy.ComposePolicy(permissions));
        Description(EndpointPermissionPolicy.StandardMetadata(
            GetType(), permissions, EndpointAuthoringModels.FastEndpoints));
    }

    protected void ThrowError(Exception exception, int statusCode = 500) => ThrowError(exception.Message, statusCode);
}

/// <summary>Test-only base for a permissioned FastEndpoints endpoint with a request and a response.</summary>
public class ElsaEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    protected void ConfigurePermissions(params string[] permissions)
    {
        Policies(EndpointPermissionPolicy.ComposePolicy(permissions));
        Description(EndpointPermissionPolicy.StandardMetadata(
            GetType(), permissions, EndpointAuthoringModels.FastEndpoints));
    }

    protected void ThrowError(Exception exception, int statusCode = 500) => ThrowError(exception.Message, statusCode);
}
