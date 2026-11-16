using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Tests;

public sealed class RuleRuntimeRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Default_reader_follows_the_replaced_combined_store_in_both_registration_orders(bool beforeFeature)
    {
        await using var fixture = new RuleStoreTestFixture();
        await fixture.InitializeAsync();
        var replacement = fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready));
        var services = new ServiceCollection();
        if (beforeFeature) services.AddSingleton<IRuleRunStore>(replacement);
        new RulesRuntimeFeature().ConfigureServices(services);
        new RulesRuntimeEntityFrameworkCoreFeature().ConfigureServices(services);
        if (!beforeFeature) services.AddSingleton<IRuleRunStore>(replacement);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRuleRunStore>();
        var reader = scope.ServiceProvider.GetRequiredService<IRuleRunReader>();
        Assert.Same(replacement, store);
        Assert.Same(replacement, reader);

        var invocation = RuleStoreTestFixture.NewInvocation();
        Assert.Equal(RuleAdmissionDisposition.Admitted, (await store.AdmitAsync(new(invocation))).Disposition);
        Assert.Equal(invocation.Identity, (await reader.FindAsync(invocation.Identity)).View!.Identity);
    }
}
