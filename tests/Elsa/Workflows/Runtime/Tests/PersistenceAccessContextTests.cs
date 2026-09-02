using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public class PersistenceAccessContextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" tenant-a")]
    [InlineData("tenant-a ")]
    public void Scope_identity_must_be_nonblank_and_canonical(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PersistenceScope(value!));
    }

    [Fact]
    public void Scope_identity_is_an_opaque_case_sensitive_value()
    {
        var first = new PersistenceScope("Tenant-Å");
        var same = new PersistenceScope("Tenant-Å");
        var differentCase = new PersistenceScope("tenant-Å");

        Assert.Equal("Tenant-Å", first.Value);
        Assert.Equal(first, same);
        Assert.NotEqual(first, differentCase);
    }

    [Fact]
    public void Ordinary_and_privileged_access_are_independent_from_storage_scope()
    {
        var scope = new PersistenceScope("tenant-a");
        var purpose = new PersistenceAccessPurpose("repair-search-index");

        var ordinary = PersistenceAccessContext.Scoped(scope);
        var privileged = PersistenceAccessContext.PrivilegedScoped(scope, purpose);

        Assert.Equal(scope, ordinary.Scope);
        Assert.Equal(scope, privileged.Scope);
        Assert.Equal(PersistenceAccessPolicy.Ordinary, ordinary.AccessPolicy);
        Assert.Equal(PersistenceAccessPolicy.Privileged, privileged.AccessPolicy);
        Assert.Null(ordinary.Purpose);
        Assert.Equal(purpose, privileged.Purpose);
        Assert.False(ordinary.AcrossScopes);
        Assert.False(privileged.AcrossScopes);
    }

    [Fact]
    public void Global_and_across_scope_access_remain_distinct()
    {
        var globalPurpose = new PersistenceAccessPurpose("rotate-host-credential");
        var acrossScopesPurpose = new PersistenceAccessPurpose("recover-stalled-executions");
        var ordinaryGlobal = PersistenceAccessContext.Global;
        var privilegedGlobal = PersistenceAccessContext.PrivilegedGlobal(globalPurpose);
        var privilegedAcrossScopes = PersistenceAccessContext.PrivilegedAcrossScopes(acrossScopesPurpose);

        Assert.Null(ordinaryGlobal.Scope);
        Assert.Equal(PersistenceAccessPolicy.Ordinary, ordinaryGlobal.AccessPolicy);
        Assert.Null(ordinaryGlobal.Purpose);
        Assert.False(ordinaryGlobal.AcrossScopes);

        Assert.Null(privilegedGlobal.Scope);
        Assert.Equal(PersistenceAccessPolicy.Privileged, privilegedGlobal.AccessPolicy);
        Assert.Equal(globalPurpose, privilegedGlobal.Purpose);
        Assert.False(privilegedGlobal.AcrossScopes);

        Assert.Null(privilegedAcrossScopes.Scope);
        Assert.Equal(PersistenceAccessPolicy.Privileged, privilegedAcrossScopes.AccessPolicy);
        Assert.Equal(acrossScopesPurpose, privilegedAcrossScopes.Purpose);
        Assert.True(privilegedAcrossScopes.AcrossScopes);
    }

    [Fact]
    public void Explicit_domain_scope_must_match_the_current_persistence_scope()
    {
        var context = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

        context.EnsureScope(new PersistenceScope("tenant-a"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.EnsureScope(new PersistenceScope("tenant-b")));
        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_domain_scope_cannot_use_global_or_across_scope_contexts()
    {
        var required = new PersistenceScope("tenant-a");

        Assert.Throws<InvalidOperationException>(() => PersistenceAccessContext.Global.EnsureScope(required));
        Assert.Throws<InvalidOperationException>(() =>
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("host-maintenance")).EnsureScope(required));
        Assert.Throws<InvalidOperationException>(() =>
            PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("tenant-recovery")).EnsureScope(required));
    }

    [Fact]
    public void Explicit_tenant_identity_must_match_without_disclosing_scope_values()
    {
        var context = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

        context.EnsureTenantScope("tenant-a");

        var exception = Assert.Throws<InvalidOperationException>(() => context.EnsureTenantScope("tenant-b"));
        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_tenant_identity_keeps_the_selected_scope_authoritative()
    {
        var context = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

        context.EnsureTenantScope(null);

        Assert.Equal("tenant-a", context.Scope!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Privileged_access_purpose_must_be_nonblank(string? purpose)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PersistenceAccessPurpose(purpose!));
    }

    [Fact]
    public void Privileged_access_requires_a_named_purpose()
    {
        var scope = new PersistenceScope("tenant-a");

        Assert.Throws<ArgumentNullException>(() => PersistenceAccessContext.PrivilegedScoped(scope, null!));
        Assert.Throws<ArgumentNullException>(() => PersistenceAccessContext.PrivilegedGlobal(null!));
        Assert.Throws<ArgumentNullException>(() => PersistenceAccessContext.PrivilegedAcrossScopes(null!));
    }

    [Fact]
    public void Default_registration_resolves_missing_tenant_context_to_ordinary_default_scope()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore();
        using var provider = services.BuildServiceProvider();
        using var requestScope = provider.CreateScope();

        var current = requestScope.ServiceProvider
            .GetRequiredService<IPersistenceAccessContextAccessor>()
            .Current;

        Assert.Equal("default", current.Scope?.Value);
        Assert.Equal(PersistenceAccessPolicy.Ordinary, current.AccessPolicy);
        Assert.Null(current.Purpose);
        Assert.False(current.AcrossScopes);
    }

    [Fact]
    public void Host_can_replace_the_single_tenant_default_scope()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore("tenant-blue");
        using var provider = services.BuildServiceProvider();
        using var requestScope = provider.CreateScope();

        var current = requestScope.ServiceProvider
            .GetRequiredService<IPersistenceAccessContextAccessor>()
            .Current;

        Assert.Equal("tenant-blue", current.Scope?.Value);
        Assert.Equal(PersistenceAccessPolicy.Ordinary, current.AccessPolicy);
        Assert.False(current.AcrossScopes);
    }

    [Fact]
    public void Scoped_binder_and_accessor_share_one_context_and_reject_rebinding()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore();
        using var provider = services.BuildServiceProvider();
        using var requestScope = provider.CreateScope();
        var binder = requestScope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>();
        var accessor = requestScope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>();
        var transported = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-blue"));

        binder.Bind(transported);

        Assert.Same(transported, accessor.Current);
        Assert.Throws<InvalidOperationException>(() => binder.Bind(PersistenceAccessContext.Global));
        Assert.Same(transported, accessor.Current);
    }

    [Fact]
    public async Task Operation_scope_factory_binds_an_explicit_ordinary_partition()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPersistenceOperationScopeFactory>();

        await using var operationScope = await factory.CreateAsync(new PersistenceScope("tenant-blue"));
        var current = operationScope.ServiceProvider
            .GetRequiredService<IPersistenceAccessContextAccessor>()
            .Current;

        Assert.Equal(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-blue")), current);
        Assert.Equal(PersistenceAccessPolicy.Ordinary, current.AccessPolicy);
    }

    [Fact]
    public async Task Persistence_scope_runner_defaults_to_one_ordinary_default_partition()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore();
        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IPersistenceScopeRunner>();
        var observed = new List<PersistenceAccessContext>();

        await runner.RunAsync((scope, operationScope, _) =>
        {
            observed.Add(operationScope.ServiceProvider
                .GetRequiredService<IPersistenceAccessContextAccessor>()
                .Current);
            Assert.Equal(PersistenceScope.DefaultValue, scope.Value);
            return ValueTask.CompletedTask;
        });

        var context = Assert.Single(observed);
        Assert.Equal(PersistenceAccessContext.Scoped(new PersistenceScope(PersistenceScope.DefaultValue)), context);
    }

    [Fact]
    public async Task Persistence_scope_runner_sweeps_every_host_supplied_partition_sequentially()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        services.AddPersistenceCore();
        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IPersistenceScopeRunner>();
        var observed = new List<string>();
        var active = 0;
        var maximumActive = 0;

        await runner.RunAsync(async (scope, operationScope, cancellationToken) =>
        {
            var currentActive = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, currentActive);
            var current = operationScope.ServiceProvider
                .GetRequiredService<IPersistenceAccessContextAccessor>()
                .Current;
            Assert.Equal(PersistenceAccessContext.Scoped(scope), current);
            observed.Add(scope.Value);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Decrement(ref active);
        });

        Assert.Equal(["tenant-a", "tenant-b"], observed);
        Assert.Equal(1, maximumActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Host_cannot_configure_a_blank_default_scope(string? defaultScope)
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<ArgumentException>(() => services.AddPersistenceCore(defaultScope!));
    }

    private sealed class TestPersistenceScopeSource(params string[] scopes) : IPersistenceScopeSource
    {
        private readonly IReadOnlyList<PersistenceScope> _scopes = scopes.Select(x => new PersistenceScope(x)).ToArray();

        public ValueTask<IReadOnlyList<PersistenceScope>> GetScopesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(_scopes);
        }
    }
}
