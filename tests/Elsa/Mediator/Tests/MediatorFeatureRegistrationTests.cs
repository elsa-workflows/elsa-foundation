using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Mediator.Tests;

public sealed class MediatorFeatureRegistrationTests
{
    [Fact]
    public void Mediator_feature_registers_request_and_command_senders()
    {
        var services = new ServiceCollection();
        var feature = new MediatorFeature();

        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        // MD-10 (§2.23.1): the feature makes the mediation senders resolvable; resolvability replaces the concrete-type pin.
        provider.GetRequiredService<IRequestSender>();
        provider.GetRequiredService<ICommandSender>();
    }
}
