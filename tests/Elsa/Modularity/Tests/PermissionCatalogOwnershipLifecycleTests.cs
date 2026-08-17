using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Studio.Preferences.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class PermissionCatalogOwnershipLifecycleTests
{
    [Fact]
    public void BridgeCanariesExposeStableOwnerAndContributorProvenance()
    {
        using var provider = BuildProvider(includeModuleManagement: true, includeStudioPreferences: true);
        var catalog = provider.GetRequiredService<IPermissionCatalog>();

        var modulePermissions = catalog.List().Where(x => x.Key.StartsWith("module-management.", StringComparison.Ordinal)).ToArray();
        var studioPermissions = catalog.List().Where(x => x.Key.StartsWith("studio.preferences.", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, modulePermissions.Length);
        Assert.Equal(2, studioPermissions.Length);
        Assert.All(modulePermissions, permission =>
        {
            Assert.Equal(ModuleManagementPermissionKeys.OwnerId, permission.OwnerId);
            Assert.Equal(typeof(ModuleManagementPermissionContributor).FullName, permission.ContributorType);
        });
        Assert.All(studioPermissions, permission =>
        {
            Assert.Equal(StudioPreferencesPermissions.OwnerId, permission.OwnerId);
            Assert.Equal(typeof(StudioPreferencesPermissionContributor).FullName, permission.ContributorType);
        });
    }

    [Fact]
    public void LegacyContributorsReceiveStableTypeBasedOwnerDefaults()
    {
        var contributor = new LegacyContributor();
        var catalog = new CompositePermissionCatalog([contributor]);

        var permission = Assert.Single(catalog.List());

        Assert.Equal(typeof(LegacyContributor).FullName, permission.OwnerId);
        Assert.Equal(typeof(LegacyContributor).FullName, permission.ContributorType);
    }

    [Fact]
    public void DuplicateCanonicalKeysReportBothOwnersAndContributorTypes()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new CompositePermissionCatalog(
        [
            new FirstDuplicateContributor(),
            new SecondDuplicateContributor()
        ]));

        Assert.Contains("READ", exception.Message);
        Assert.Contains(typeof(FirstDuplicateContributor).FullName!, exception.Message);
        Assert.Contains(typeof(SecondDuplicateContributor).FullName!, exception.Message);
        Assert.Contains(FirstDuplicateContributor.Owner, exception.Message);
        Assert.Contains(SecondDuplicateContributor.Owner, exception.Message);
    }

    [Theory]
    [InlineData("*")]
    [InlineData(" read ")]
    [InlineData("")]
    public void InvalidCatalogKeysFailActivation(string key)
    {
        Assert.Throws<InvalidOperationException>(() => new CompositePermissionCatalog(
            [new InvalidContributor(key, null)]));
    }

    [Fact]
    public void WildcardImplicationTargetFailsActivation()
    {
        Assert.Throws<InvalidOperationException>(() => new CompositePermissionCatalog(
            [new InvalidContributor("read", new HashSet<string> { "*" })]));
    }

    [Fact]
    public void CanonicalIndexRetainsTheContributorPresentationSpelling()
    {
        const string presentation = "réad";
        var catalog = new CompositePermissionCatalog([new InvalidContributor(presentation, null)]);

        var permission = catalog.Find("RE\u0301AD");

        Assert.NotNull(permission);
        Assert.Equal(presentation, permission!.Key);
        Assert.Equal(presentation, Assert.Single(catalog.List()).Key);
    }

    [Fact]
    public void SuccessiveProvidersReflectOnlyTheirActiveContributors()
    {
        using var enabled = BuildProvider(includeModuleManagement: true, includeStudioPreferences: true);
        using var disabled = BuildProvider(includeModuleManagement: false, includeStudioPreferences: true);
        using var reenabled = BuildProvider(includeModuleManagement: true, includeStudioPreferences: false);

        var enabledCatalog = enabled.GetRequiredService<IPermissionCatalog>();
        var disabledCatalog = disabled.GetRequiredService<IPermissionCatalog>();
        var reenabledCatalog = reenabled.GetRequiredService<IPermissionCatalog>();

        Assert.NotNull(enabledCatalog.Find(ModuleManagementPermissionKeys.Read));
        Assert.NotNull(enabledCatalog.Find(StudioPreferencesPermissions.Read));
        Assert.Null(disabledCatalog.Find(ModuleManagementPermissionKeys.Read));
        Assert.NotNull(disabledCatalog.Find(StudioPreferencesPermissions.Read));
        Assert.NotNull(reenabledCatalog.Find(ModuleManagementPermissionKeys.Read));
        Assert.Null(reenabledCatalog.Find(StudioPreferencesPermissions.Read));

        // A previously built provider remains an immutable snapshot after another provider is built.
        Assert.NotNull(enabledCatalog.Find(StudioPreferencesPermissions.Read));
    }

    [Fact]
    public void ReplacementProviderDoesNotRetainTheReplacedContributor()
    {
        using var original = BuildProvider(includeModuleManagement: true, includeStudioPreferences: false);
        using var replacement = BuildProvider(includeModuleManagement: false, includeStudioPreferences: false, replacement: true);

        var originalCatalog = original.GetRequiredService<IPermissionCatalog>();
        var replacementCatalog = replacement.GetRequiredService<IPermissionCatalog>();

        Assert.Equal(ModuleManagementPermissionKeys.OwnerId, originalCatalog.Find(ModuleManagementPermissionKeys.Read)!.OwnerId);
        Assert.Null(replacementCatalog.Find(ModuleManagementPermissionKeys.Read));
        Assert.Equal(ReplacementContributor.Owner, replacementCatalog.Find(ReplacementContributor.Key)!.OwnerId);
        Assert.Null(replacementCatalog.Find(ModuleManagementPermissionKeys.Read));
    }

    private static ServiceProvider BuildProvider(bool includeModuleManagement, bool includeStudioPreferences, bool replacement = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionContributor, DefaultIdentityPermissionCatalog>();

        if (includeModuleManagement)
            services.AddSingleton<IPermissionContributor, ModuleManagementPermissionContributor>();
        if (includeStudioPreferences)
            services.AddSingleton<IPermissionContributor, StudioPreferencesPermissionContributor>();
        if (replacement)
            services.AddSingleton<IPermissionContributor, ReplacementContributor>();

        services.AddSingleton<IPermissionCatalog, CompositePermissionCatalog>();
        return services.BuildServiceProvider();
    }

    private sealed class LegacyContributor : IPermissionContributor
    {
        public IEnumerable<Permission> Contribute() =>
            [new("legacy.read", "Legacy", "Tests", "Legacy contributor.")];
    }

    private sealed class FirstDuplicateContributor : IPermissionContributor
    {
        public const string Owner = "tests.first-owner";

        public string OwnerId => Owner;

        public IEnumerable<Permission> Contribute() =>
            [new("read", "First", "Tests", "First duplicate.")];
    }

    private sealed class SecondDuplicateContributor : IPermissionContributor
    {
        public const string Owner = "tests.second-owner";

        public string OwnerId => Owner;

        public IEnumerable<Permission> Contribute() =>
            [new("READ", "Second", "Tests", "Second duplicate.")];
    }

    private sealed class InvalidContributor(string key, IReadOnlySet<string>? implies) : IPermissionContributor
    {
        public IEnumerable<Permission> Contribute() =>
            [new(key, "Invalid", "Tests", "Invalid catalog entry.", implies)];
    }

    private sealed class ReplacementContributor : IPermissionContributor
    {
        public const string Key = "replacement.read";
        public const string Owner = "tests.replacement-owner";

        public string OwnerId => Owner;

        public IEnumerable<Permission> Contribute() =>
            [new(Key, "Replacement", "Tests", "Replacement contributor.")];
    }
}
