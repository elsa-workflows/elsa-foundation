using CShells;
using CShells.AspNetCore.Routing;
using CShells.Resolution;

namespace Elsa.Studio.Hosting;

/// <summary>Keeps matched host endpoints in the root scope, while CShells owns actual shell endpoints.</summary>
internal sealed class StudioShellResolver(DefaultShellResolver inner) : IShellResolver
{
    public Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = context.Get<HttpContext>(ShellResolutionContextKeys.ProtocolContext)?.GetEndpoint();
        return endpoint is not null && endpoint.Metadata.GetMetadata<ShellEndpointMetadata>() is null
            ? Task.FromResult<ShellId?>(null)
            : inner.ResolveAsync(context, cancellationToken);
    }
}
