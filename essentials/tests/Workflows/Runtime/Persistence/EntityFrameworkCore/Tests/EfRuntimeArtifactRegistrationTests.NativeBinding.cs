using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactRegistrationTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void EF_registration_replaces_or_excludes_the_whole_native_group_and_keeps_exact_selected_ownership(bool runtimeFirst, bool repeatRuntime)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new NativeBindingAccess());
        if (runtimeFirst) services.AddWorkflowRuntime();
        var nativeBackend = RuntimeArtifactStoreBackend.Find(services);
        var nativeOwned = nativeBackend is null ? [] : services.Where(nativeBackend.Owns).ToArray();
        services.AddRuntimeArtifactsEntityFrameworkCore(new()
        {
            Provider = "Sqlite", ConnectionString = "Data Source=:memory:"
        });
        services.AddOptions<RuntimeRecoveryContinuationOptions>().Configure(options => { options.SigningKey = SigningKey; options.AllowEphemeralDevelopmentKey = false; });
        if (!runtimeFirst || repeatRuntime) services.AddWorkflowRuntime();
        foreach (var previous in nativeOwned) Assert.DoesNotContain(previous, services);
        var groupType = typeof(InMemoryExecutableActivityTemplateStore).Assembly.GetType("Elsa.Workflows.Runtime.Services.Executables.InMemoryRuntimeArtifactStores", throwOnError: true)!;
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == groupType);
        var backend = RuntimeArtifactStoreBackend.Find(services)!;
        Assert.Equal(RuntimeArtifactStoreBackend.EntityFramework, backend.Name); backend.EnsureOwnsRegisteredContracts(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var templates = Assert.IsType<EfExecutableActivityTemplateStore>(scope.ServiceProvider.GetRequiredService<IExecutableActivityTemplateStore>());
        var artifacts = Assert.IsType<EfWorkflowExecutableStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>());
        var sources = Assert.IsType<EfWorkflowExecutableSourceReferenceStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());
        Assert.True(templates.CanMaintainReferences(artifacts, sources));
        Assert.Same(templates, scope.ServiceProvider.GetRequiredService<IExecutableActivityTemplateReader>());
        Assert.Same(sources, scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceReader>());
    }
    private sealed class NativeBindingAccess : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope("default"));
    }

}
