using System.Reflection;
using System.Reflection.Emit;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Guards the single <c>[EfModule]</c> declaration every module assembly now carries (ADR 0076 D2, spec
/// 171 slice 1). <see cref="EfModuleCatalog.Discover"/> must find all 13 first-party modules with the
/// names, base contexts and frozen history names the vocabulary table pins, and a descriptor's
/// <c>HistoryModule</c> must never drift from the module's own frozen
/// <c>&lt;Module&gt;EfModule.HistoryModuleName</c> constant — the exact mismatch already latent between
/// <c>RuntimeEfModule.HistoryModuleName</c> and <see cref="ModuleContextCatalog"/>'s independently
/// re-derived history table before this descriptor existed.
/// </summary>
public sealed class EfModuleDescriptorTests
{
    // Read directly from each module's own frozen constant and base context, not re-derived, so this
    // guard catches the exact drift the descriptor exists to prevent.
    private static readonly (string Name, Type Context, string HistoryModule)[] Expected =
    [
        ("Secrets", typeof(SecretsDbContext), SecretsEfModule.HistoryModuleName),
        ("Workflows.Runtime", typeof(RuntimeDbContext), RuntimeEfModule.HistoryModuleName),
        ("Workflows.Design", typeof(WorkflowsDesignDbContext), WorkflowsDesignEfModule.HistoryModuleName),
        ("Workflows.Publishing", typeof(PublishingSnapshotReviewDbContext), PublishingSnapshotReviewEfModule.HistoryModuleName),
        ("Workflows.Runtime.Distributed.Placement", typeof(ExecutionPlacementDbContext), ExecutionPlacementEfModule.HistoryModuleName),
        ("Workflows.Runtime.Distributed.CommandTransport", typeof(ExecutionCommandTransportDbContext), ExecutionCommandTransportEfModule.HistoryModuleName),
        ("Activities.Design", typeof(ActivitiesDesignDbContext), ActivitiesDesignEfModule.HistoryModuleName),
        ("Identity.Iam", typeof(IdentityIamDbContext), IdentityIamEfModule.HistoryModuleName),
        ("Identity.ProviderConfiguration", typeof(IdentityProviderConfigurationDbContext), IdentityProviderConfigurationEfModule.HistoryModuleName),
        ("Diagnostics.OpenTelemetry", typeof(EfOpenTelemetryDbContext), EfOpenTelemetryModule.HistoryModuleName),
        ("Diagnostics.StructuredLogs", typeof(StructuredLogsDbContext), StructuredLogsEfModule.HistoryModuleName),
        ("Studio.Preferences", typeof(StudioPreferencesDbContext), StudioPreferencesEfModule.HistoryModuleName),
        ("Elsa3.Activities.Design.Import", typeof(Elsa3ImportDbContext), Elsa3ImportEfModule.HistoryModuleName)
    ];

    private static IReadOnlyList<EfModuleDescriptor> Discover() => EfModuleCatalog.Discover(ModuleContextCatalog.Modules);

    [Fact]
    public void Discover_returns_all_13_modules_with_correct_names_contexts_and_frozen_history()
    {
        var descriptors = Discover();
        Assert.Equal(13, descriptors.Count);

        foreach (var (name, context, historyModule) in Expected)
        {
            var descriptor = Assert.Single(descriptors, d => d.Name == name);
            Assert.Equal(context, descriptor.ContextType);
            Assert.Equal(historyModule, descriptor.HistoryModule);
            Assert.Equal(EfMigrationsHistory.TableName(historyModule), descriptor.HistoryTableName);
        }
    }

    [Fact]
    public void Every_module_name_is_unique_case_insensitively()
    {
        var collisions = DescribeDuplicates(
            Discover(),
            descriptor => descriptor.Name,
            StringComparer.OrdinalIgnoreCase,
            descriptor => $"{descriptor.ContextType.Name} in {descriptor.Assembly.GetName().Name}");

        Assert.True(collisions.Length == 0, $"Module names declared more than once: {string.Join("; ", collisions)}.");
    }

    [Fact]
    public void Every_history_module_is_unique()
    {
        var collisions = DescribeDuplicates(
            Discover(),
            descriptor => descriptor.HistoryModule,
            StringComparer.Ordinal,
            descriptor => descriptor.Name);

        Assert.True(collisions.Length == 0, $"History modules shared by more than one module: {string.Join("; ", collisions)}.");
    }

    /// <summary>Groups <paramref name="descriptors"/> by <paramref name="key"/>, naming the colliding key and every module that declares it.</summary>
    private static string[] DescribeDuplicates<TKey>(
        IReadOnlyList<EfModuleDescriptor> descriptors,
        Func<EfModuleDescriptor, TKey> key,
        IEqualityComparer<TKey> comparer,
        Func<EfModuleDescriptor, string> describe) =>
        descriptors
            .GroupBy(key, comparer)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} <- {string.Join(", ", group.Select(describe))}")
            .ToArray();

    [Fact]
    public void Secrets_declares_all_four_providers()
    {
        var secrets = Discover().Single(descriptor => descriptor.Name == "Secrets");
        Assert.NotNull(secrets.Sqlite);
        Assert.NotNull(secrets.SqlServer);
        Assert.NotNull(secrets.PostgreSql);
        Assert.NotNull(secrets.MySql);
    }

    [Fact]
    public void Every_declared_provider_context_derives_from_the_base_context_and_follows_the_provider_suffix()
    {
        foreach (var descriptor in Discover())
        {
            foreach (var provider in ModuleContextCatalog.Providers)
            {
                var context = descriptor.RequireProviderContext(provider);
                Assert.True(
                    descriptor.ContextType.IsAssignableFrom(context),
                    $"{descriptor.Name}: {context.Name} does not derive from {descriptor.ContextType.Name}.");
                Assert.EndsWith(provider + "DbContext", context.Name, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Every_provider_derived_context_is_covered_by_exactly_one_module()
    {
        var declared = Discover()
            .SelectMany(descriptor => ModuleContextCatalog.Providers.Select(descriptor.RequireProviderContext))
            .ToArray();

        Assert.Equal(declared.Length, declared.Distinct().Count());
        Assert.Equal(
            ModuleContextCatalog.AllContexts().OrderBy(type => type.FullName, StringComparer.Ordinal),
            declared.OrderBy(type => type.FullName, StringComparer.Ordinal));
    }

    /// <summary>
    /// Neither project owns a context or migrations of its own — Dashboard reads Design and Runtime's
    /// contexts read-only, and AspNetCoreIdentity reads Identity.Iam's — so neither declares a module.
    /// </summary>
    [Fact]
    public void The_two_context_free_ef_projects_declare_no_module()
    {
        var dashboard = typeof(WorkflowsDashboardEntityFrameworkCoreFeature).Assembly;
        var aspNetCoreIdentity = typeof(AspNetCoreIdentityEntityFrameworkCoreFeature).Assembly;

        var descriptors = EfModuleCatalog.Discover(ModuleContextCatalog.Modules.Append(dashboard).Append(aspNetCoreIdentity).Distinct());

        Assert.Equal(13, descriptors.Count);
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Assembly == dashboard);
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Assembly == aspNetCoreIdentity);
    }

    [Fact]
    public void ProviderContext_returns_null_for_an_unsupported_provider_and_RequireProviderContext_throws_a_clear_error()
    {
        var descriptor = new EfModuleDescriptor(
            "Acme.Widgets",
            typeof(object),
            "ElsaAcmeWidgets",
            Sqlite: typeof(object),
            SqlServer: null,
            PostgreSql: typeof(object),
            MySql: typeof(object),
            DependsOn: [],
            PostMigration: [],
            Assembly: typeof(EfModuleDescriptorTests).Assembly);

        Assert.Null(descriptor.ProviderContext("SqlServer"));

        var failure = Assert.Throws<NotSupportedException>(() => descriptor.RequireProviderContext("SqlServer"));
        Assert.Contains("Acme.Widgets", failure.Message, StringComparison.Ordinal);
        Assert.Contains("sqlserver", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discover_refuses_a_case_insensitive_name_collision_naming_both_sources()
    {
        var first = BuildModuleAssembly("EfModuleDescriptorTests.Collision.First", "Acme.Widgets");
        var second = BuildModuleAssembly("EfModuleDescriptorTests.Collision.Second", "acme.widgets");

        var failure = Assert.Throws<InvalidOperationException>(() => EfModuleCatalog.Discover([first, second]));
        Assert.Contains(first.GetName().Name!, failure.Message, StringComparison.Ordinal);
        Assert.Contains(second.GetName().Name!, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_refuses_a_history_module_TableName_rejects_naming_the_assembly_and_module()
    {
        var assembly = BuildModuleAssembly("EfModuleDescriptorTests.BadHistoryModule", "Acme.Widgets", historyModule: "Bad.Name");

        var failure = Assert.Throws<InvalidOperationException>(() => EfModuleCatalog.Discover([assembly]));
        Assert.Contains(assembly.GetName().Name!, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Widgets", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_matches_a_first_party_module_name_case_insensitively_and_returns_null_for_an_unknown_name()
    {
        var descriptors = Discover();

        var lower = EfModuleCatalog.Find(descriptors, "workflows.runtime");
        var upper = EfModuleCatalog.Find(descriptors, "WORKFLOWS.RUNTIME");

        Assert.NotNull(lower);
        Assert.Same(lower, upper);
        Assert.Equal(typeof(RuntimeDbContext), lower!.ContextType);

        Assert.Null(EfModuleCatalog.Find(descriptors, "No.Such.Module"));
    }

    /// <summary>Builds a minimal in-memory assembly carrying one <see cref="EfModuleAttribute"/> declaration.</summary>
    private static Assembly BuildModuleAssembly(string assemblyName, string moduleName, string? historyModule = null)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var constructor = typeof(EfModuleAttribute).GetConstructor([typeof(string), typeof(Type)])!;
        var historyModuleProperty = typeof(EfModuleAttribute).GetProperty(nameof(EfModuleAttribute.HistoryModule))!;
        builder.SetCustomAttribute(new CustomAttributeBuilder(
            constructor,
            [moduleName, typeof(object)],
            [historyModuleProperty],
            [historyModule ?? $"Elsa{moduleName.Replace(".", "", StringComparison.Ordinal)}"]));
        return builder;
    }
}
