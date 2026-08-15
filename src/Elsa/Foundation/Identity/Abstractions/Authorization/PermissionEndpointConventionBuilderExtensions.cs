using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Elsa.Foundation.Identity.Abstractions.Authorization;

public static class PermissionEndpointConventionBuilderExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        AddPolicy(builder, PermissionPolicyDescriptor.Single(permission));

    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder =>
        AddPolicy(builder, PermissionPolicyDescriptor.Any(permissions));

    public static TBuilder RequireAllPermissions<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder =>
        AddPolicy(builder, PermissionPolicyDescriptor.All(permissions));

    private static TBuilder AddPolicy<TBuilder>(TBuilder builder, PermissionPolicyDescriptor descriptor)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var policy = new PermissionPolicyCodec().Format(descriptor);
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(new AuthorizeAttribute(policy)));
        return builder;
    }
}
