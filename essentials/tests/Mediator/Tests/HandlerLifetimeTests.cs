using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Mediator.Tests;

/// <summary>
/// Pins handler lifetime: handlers stay scoped and are resolved from the sender's scoped provider on
/// every dispatch. The compiled-invoker cache holds only types and stateless delegates, so it must
/// never pin a handler instance or a scope.
/// </summary>
public class HandlerLifetimeTests
{
    [Fact]
    public async Task Handler_IsResolvedFromSendersScope_PerDispatch()
    {
        await using var provider = MediatorTestHost.Build(services =>
        {
            services.AddScoped<ScopeMarker>();
            services.AddScoped<IRequestHandler<ScopeRequest, Guid>, ScopeRequestHandler>();
        });

        Guid firstInScope1;
        Guid secondInScope1;
        Guid inScope2;

        using (var scope1 = provider.CreateScope())
        {
            var sender = scope1.ServiceProvider.GetRequiredService<IRequestSender>();
            firstInScope1 = await sender.Send(new ScopeRequest());
            secondInScope1 = await sender.Send(new ScopeRequest());
        }

        using (var scope2 = provider.CreateScope())
        {
            var sender = scope2.ServiceProvider.GetRequiredService<IRequestSender>();
            inScope2 = await sender.Send(new ScopeRequest());
        }

        // Same scope resolves the same scoped marker across dispatches...
        Assert.Equal(firstInScope1, secondInScope1);
        // ...but a different scope resolves a different marker, proving the handler is resolved fresh
        // from the caller's scope rather than captured by the singleton pipeline or the invoker cache.
        Assert.NotEqual(firstInScope1, inScope2);
    }
}
