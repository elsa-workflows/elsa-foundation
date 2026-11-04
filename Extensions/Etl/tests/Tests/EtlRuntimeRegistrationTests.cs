using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlRuntimeRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_runtime_registration_keeps_sqlite_and_scoped_optional_capabilities(bool pooling)
    {
        await using var database = await EtlRuntimeTestDatabase.CreateAsync();
        await using var expected = database.CreateContext();
        var services = new ServiceCollection();
        services.AddLogging();
        new EtlRuntimeEntityFrameworkCoreFeature
        {
            ConnectionString = expected.Database.GetConnectionString(), Pooling = pooling
        }.ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var admission = EtlRuntimeFixtures.Admission("registered-default");
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            Assert.IsType<EtlRuntimeSqliteDbContext>(context);
            var store = scope.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            Assert.Same(store, scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>());
            Assert.Same(store, scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeInspector>());
            Assert.Null(scope.ServiceProvider.GetService<IEtlDefinitionStore>());
            Assert.Null(scope.ServiceProvider.GetService<IEtlDefinitionStoreV2>());
            Assert.Null(scope.ServiceProvider.GetService<IEtlArtifactCompiler>());
            Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            Assert.Equal(EtlStoreStatus.Replay, (await store.AdmitAsync(admission)).Status);
            Assert.Equal(admission.Invocation.RunId, (await store.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId))!.Invocation.RunId);
        }
    }
}
