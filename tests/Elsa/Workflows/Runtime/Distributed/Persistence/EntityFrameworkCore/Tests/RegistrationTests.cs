using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Groundwork.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests;

public sealed class RegistrationTests
{
    private static readonly DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions Options = new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=registration-test.db"
    };

    private static readonly DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions CommandOptions = new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=command-registration-test.db"
    };

    [Fact]
    public void Placement_store_declares_single_implementation_replacement_semantics()
    {
        Assert.True(typeof(IExecutionPlacementStore).IsDefined(
            typeof(ExecutionPlacementStoreReplacementContractAttribute),
            inherit: false));
    }

    [Fact]
    public void Command_transport_declares_single_implementation_replacement_semantics()
    {
        Assert.True(typeof(IExecutionCommandTransport).IsDefined(
            typeof(ExecutionCommandTransportReplacementContractAttribute),
            inherit: false));
    }

    [Fact]
    public void EF_command_transport_replaces_only_transport_after_the_distributed_default_is_composed()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
        var originalPlacement = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));

        services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(CommandOptions);

        Assert.Same(originalPlacement, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore)));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport) && descriptor.ImplementationFactory is not null);
        Assert.Equal(ExecutionCommandTransportBackend.EntityFramework, CommandBackend(services));
        Assert.Equal(ExecutionPlacementStoreBackend.InMemory, Backend(services));
    }

    [Fact]
    public void Repeating_the_same_command_transport_registration_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(CommandOptions);
        var before = services.ToArray();

        services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(new()
        {
            Provider = CommandOptions.Provider,
            ConnectionString = CommandOptions.ConnectionString
        });

        Assert.Equal(before, services);
    }

    [Fact]
    public void Conflicting_command_transport_reregistration_fails_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(CommandOptions);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=different-command.db"
        }));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Explicit_unmarked_command_transport_is_rejected_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IExecutionCommandTransport, ExplicitTransport>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(CommandOptions));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Groundwork_rejects_an_explicit_unmarked_command_transport_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IExecutionCommandTransport, ExplicitTransport>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkDistributedRuntimeStores());

        Assert.Equal(before, services);
    }

    [Fact]
    public void EF_command_transport_and_EF_placement_have_independent_ownership()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);
        services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(CommandOptions);

        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, Backend(services));
        Assert.Equal(ExecutionCommandTransportBackend.EntityFramework, CommandBackend(services));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ExecutionPlacementDbContext));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ExecutionCommandTransportDbContext));
    }

    [Fact]
    public void Groundwork_then_EF_command_transport_withdraws_only_command_units_and_preserves_placement()
    {
        var services = new ServiceCollection();
        services.AddGroundworkDistributedRuntimeStores();
        services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(CommandOptions);

        Assert.Equal(ExecutionPlacementStoreBackend.Groundwork, Backend(services));
        Assert.Equal(ExecutionCommandTransportBackend.EntityFramework, CommandBackend(services));
        Assert.Contains(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.PlacementUnitId);
        Assert.DoesNotContain(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
        Assert.DoesNotContain(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.CommandTransportUnitId);
    }

    [Fact]
    public void EF_command_transport_then_Groundwork_preserves_EF_transport_and_adds_only_placement_units()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(CommandOptions);
        services.AddGroundworkDistributedRuntimeStores();

        Assert.Equal(ExecutionPlacementStoreBackend.Groundwork, Backend(services));
        Assert.Equal(ExecutionCommandTransportBackend.EntityFramework, CommandBackend(services));
        Assert.Contains(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.PlacementUnitId);
        Assert.DoesNotContain(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
        Assert.DoesNotContain(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.CommandTransportUnitId);
    }

    [Fact]
    public void EF_command_transport_does_not_advertise_checkpoint_lease_fencing()
    {
        var services = new ServiceCollection();
        services.AddGroundworkDistributedRuntimeStores();
        services.AddDistributedRuntimeExecutionCommandTransportEntityFrameworkCore(CommandOptions);

        using var provider = services.BuildServiceProvider();
        var capability = provider.GetService<IWorkflowExecutionLeaseFencingCapability>();
        Assert.True(capability is null || !capability.IsAvailable);
        var evidence = provider.GetServices<IWorkflowDispatchDurabilityEvidence>()
            .Where(item => item.Component == WorkflowDispatchDurabilityComponents.DistributionPersistence)
            .ToArray();
        Assert.Single(evidence);
        Assert.Contains("EntityFramework", evidence[0].GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_transport_feature_maps_settings_and_resolves_a_working_scoped_store()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-command-transport-feature-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddPersistenceCore("command-feature-scope");
            var feature = new DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreFeature
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath}",
                ConnectionName = "IgnoredBecauseExplicitConnectionWins"
            };

            feature.ConfigureServices(services);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var configured = provider.GetRequiredService<DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreOptions>();
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ExecutionCommandTransportDbContext>();
            var transport = scope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>();

            Assert.Equal(feature.Provider, configured.Provider);
            Assert.Equal(feature.ConnectionString, configured.ConnectionString);
            Assert.Equal(feature.ConnectionName, configured.ConnectionName);
            Assert.IsType<ExecutionCommandTransportSqliteDbContext>(context);
            Assert.IsType<EfExecutionCommandTransport>(transport);
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);

            await context.Database.EnsureCreatedAsync();
            var sent = await transport.SendAsync(
                "wf-command-feature",
                new WorkflowExecutionCommandEnvelope(
                    "envelope-command-feature",
                    "wf-command-feature",
                    new WorkflowExecutionCommand(
                        "command-feature",
                        "wf-command-feature",
                        WorkflowExecutionCommandKind.RunSchedulerWork,
                        DateTimeOffset.UtcNow,
                        null,
                        new Dictionary<string, string>()),
                    "idempotency-command-feature",
                    WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
                    DateTimeOffset.UtcNow,
                    partition: new WorkflowExecutionPartition("command-feature-scope")),
                DateTimeOffset.UtcNow);
            Assert.Equal(1, sent.Sequence);
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public void Backend_rejects_a_null_name()
    {
        var descriptor = ServiceDescriptor.Scoped<IExecutionPlacementStore, ExplicitStore>();

        Assert.Throws<ArgumentNullException>(() => new ExecutionPlacementStoreBackend(null!, descriptor));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    public void Backend_rejects_an_unknown_or_blank_name(string name)
    {
        var descriptor = ServiceDescriptor.Scoped<IExecutionPlacementStore, ExplicitStore>();

        Assert.Throws<ArgumentException>(() => new ExecutionPlacementStoreBackend(name, descriptor));
    }

    [Fact]
    public void Backend_rejects_a_descriptor_for_another_contract()
    {
        var descriptor = ServiceDescriptor.Scoped<ExplicitStore, ExplicitStore>();

        var exception = Assert.Throws<ArgumentException>(() =>
            new ExecutionPlacementStoreBackend(ExecutionPlacementStoreBackend.EntityFramework, descriptor));
        Assert.Contains(nameof(IExecutionPlacementStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EF_replaces_only_placement_after_the_distributed_default_is_composed()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
        var originalTransport = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore) && descriptor.ImplementationFactory is not null);
        Assert.Same(originalTransport, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport)));
        Assert.Equal(
            ExecutionPlacementStoreBackend.EntityFramework,
            services.Single(descriptor => descriptor.ImplementationInstance is ExecutionPlacementStoreBackend).ImplementationInstance is ExecutionPlacementStoreBackend backend ? backend.Name : null);
    }

    [Fact]
    public void Repeating_the_same_registration_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);
        var before = services.Count;
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
        {
            Provider = Options.Provider,
            ConnectionString = Options.ConnectionString
        });
        Assert.Equal(before, services.Count);
    }

    [Fact]
    public void Conflicting_reregistration_fails_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=different.db"
        }));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Explicit_unmarked_store_is_rejected_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IExecutionPlacementStore, ExplicitStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Distributed_default_preserves_an_explicit_unmarked_store_and_EF_rejects_it()
    {
        var services = new ServiceCollection();
        services.AddScoped<IExecutionPlacementStore, ExplicitStore>();
        var explicitDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);

        Assert.Same(explicitDescriptor, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore)));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationInstance is ExecutionPlacementStoreBackend);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options));
        Assert.Equal(before, services);
    }

    [Fact]
    public void EF_rejects_a_store_replaced_after_the_backend_claimed_ownership()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Scoped<IExecutionPlacementStore, ExplicitStore>());
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options));
        Assert.Equal(before, services);
    }

    [Theory]
    [InlineData(ExecutionPlacementStoreBackend.InMemory)]
    [InlineData(ExecutionPlacementStoreBackend.Groundwork)]
    [InlineData(ExecutionPlacementStoreBackend.EntityFramework)]
    public void Startup_validation_accepts_each_exclusive_backend(string backend)
    {
        var services = new ServiceCollection();
        RegisterBackend(services, backend);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData(ExecutionPlacementStoreBackend.InMemory)]
    [InlineData(ExecutionPlacementStoreBackend.Groundwork)]
    [InlineData(ExecutionPlacementStoreBackend.EntityFramework)]
    public void Startup_validation_rejects_a_store_added_after_backend_registration(string backend)
    {
        var services = new ServiceCollection();
        RegisterBackend(services, backend);
        services.AddScoped<IExecutionPlacementStore, ExplicitStore>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("no longer exclusively owns", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExecutionPlacementStoreBackend.InMemory)]
    [InlineData(ExecutionPlacementStoreBackend.Groundwork)]
    [InlineData(ExecutionPlacementStoreBackend.EntityFramework)]
    public void Startup_validation_rejects_a_backend_marker_removed_after_registration(string backend)
    {
        var services = new ServiceCollection();
        RegisterBackend(services, backend);
        services.RemoveAll<ExecutionPlacementStoreBackend>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("ownership marker was removed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Groundwork_rejects_an_unmarked_store_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IExecutionPlacementStore, ExplicitStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkDistributedRuntimeStores());
        Assert.Equal(before, services);
    }

    [Fact]
    public void Groundwork_unit_withdrawal_is_idempotent_target_wide_and_preserves_other_units()
    {
        var services = new ServiceCollection();
        Assert.Same(services, services.RemoveGroundworkStorageUnit(DistributedGroundworkStorageManifest.PlacementUnitId));
        services.AddGroundworkStorageUnit(DistributedGroundworkStorageManifest.CreatePlacementUnit());
        services.AddGroundworkStorageUnit(DistributedGroundworkStorageManifest.CreatePlacementUnit(), "secondary");
        services.AddGroundworkStorageUnit(DistributedGroundworkStorageManifest.CreateCommandStreamHeadUnit(), "secondary");

        services.RemoveGroundworkStorageUnit(DistributedGroundworkStorageManifest.PlacementUnitId);
        services.RemoveGroundworkStorageUnit(DistributedGroundworkStorageManifest.PlacementUnitId);

        Assert.Equal([DistributedGroundworkStorageManifest.CommandStreamHeadUnitId], GroundworkUnitIds(services));
        Assert.Throws<ArgumentException>(() => services.RemoveGroundworkStorageUnit(" "));
    }

    [Fact]
    public void EF_snapshots_mutable_registration_options()
    {
        var services = new ServiceCollection();
        var supplied = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=original.db",
            ConnectionName = "Original"
        };

        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(supplied);
        supplied.Provider = "SqlServer";
        supplied.ConnectionString = "Server=changed";
        supplied.ConnectionName = "Changed";

        var configured = Assert.IsType<DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions>(
            services.Single(descriptor => descriptor.ServiceType == typeof(DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions)).ImplementationInstance);
        Assert.NotSame(supplied, configured);
        Assert.Equal("Sqlite", configured.Provider);
        Assert.Equal("Data Source=original.db", configured.ConnectionString);
        Assert.Equal("Original", configured.ConnectionName);
    }

    [Theory]
    [InlineData("Sqlite", typeof(ExecutionPlacementSqliteDbContext))]
    [InlineData("SqlServer", typeof(ExecutionPlacementSqlServerDbContext))]
    [InlineData("PostgreSql", typeof(ExecutionPlacementPostgreSqlDbContext))]
    [InlineData("MySql", typeof(ExecutionPlacementMySqlDbContext))]
    public void Provider_selection_registers_only_the_selected_context(string provider, Type expectedContext)
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
        {
            Provider = provider,
            ConnectionString = "unused"
        });

        var selectedContexts = services
            .Where(descriptor =>
                typeof(ExecutionPlacementDbContext).IsAssignableFrom(descriptor.ServiceType) &&
                descriptor.ServiceType != typeof(ExecutionPlacementDbContext))
            .Select(descriptor => descriptor.ServiceType)
            .ToArray();

        Assert.Equal([expectedContext], selectedContexts);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ExecutionPlacementDbContext));
    }

    [Fact]
    public void Connection_resolution_prefers_an_explicit_connection_string()
    {
        var options = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=explicit.db",
            ConnectionName = "Named"
        };

        Assert.Equal(
            "Data Source=explicit.db",
            ResolveConnection(options, new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ExecutionPlacementEfModule.DefaultConnectionName}"] = "Data Source=default.db",
                ["ConnectionStrings:Named"] = "Data Source=named.db"
            }));
    }

    [Fact]
    public void Connection_resolution_uses_the_named_connection()
    {
        var options = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionName = "Named"
        };

        Assert.Equal(
            "Data Source=named.db",
            ResolveConnection(options, new Dictionary<string, string?>
            {
                ["ConnectionStrings:Named"] = "Data Source=named.db"
            }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Missing_or_blank_named_connection_is_rejected(string? configuredValue)
    {
        var options = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionName = "Missing"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ResolveConnection(options, new Dictionary<string, string?>
            {
                ["ConnectionStrings:Missing"] = configuredValue
            }));
        Assert.Contains("connection 'Missing' was not found", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Configured_default_is_used_when_connection_name_is_missing_or_blank(string? connectionName)
    {
        var options = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionName = connectionName
        };

        Assert.Equal(
            "Data Source=configured-default.db",
            ResolveConnection(options, new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ExecutionPlacementEfModule.DefaultConnectionName}"] = "Data Source=configured-default.db"
            }));
    }

    [Fact]
    public void Sqlite_uses_the_module_fallback_without_configuration()
    {
        var options = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions { Provider = "Sqlite" };

        Assert.Equal(
            ExecutionPlacementEfModule.DefaultSqliteConnectionString,
            ResolveConnection(options));
    }

    [Fact]
    public void Non_Sqlite_provider_requires_an_explicit_or_configured_connection()
    {
        var options = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions { Provider = "SqlServer" };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ResolveConnection(options));
        Assert.Contains("requires ConnectionString or ConnectionName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_provider_is_rejected_before_service_collection_mutation()
    {
        var services = new ServiceCollection();
        var before = services.ToArray();

        Assert.Throws<ArgumentException>(() => services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
        {
            Provider = "Unknown",
            ConnectionString = "unused"
        }));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Groundwork_then_EF_replaces_only_the_placement_store()
    {
        var services = new ServiceCollection();
        services.AddGroundworkDistributedRuntimeStores();
        var originalTransport = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        Assert.Contains(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.PlacementUnitId);

        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, Backend(services));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Same(originalTransport, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport)));
        Assert.DoesNotContain(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.PlacementUnitId);
        Assert.Contains(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
        Assert.Contains(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.CommandTransportUnitId);
    }

    [Fact]
    public void EF_then_Groundwork_preserves_EF_placement_and_replaces_transport()
    {
        var services = new ServiceCollection();
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        services.AddGroundworkDistributedRuntimeStores();
        var groundworkTransport = services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport));
        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);

        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, Backend(services));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore));
        Assert.Same(groundworkTransport, services.Single(descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport)));
        Assert.DoesNotContain(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.PlacementUnitId);
        Assert.Contains(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
        Assert.Contains(GroundworkUnitIds(services), unitId => unitId == DistributedGroundworkStorageManifest.CommandTransportUnitId);
    }

    [Fact]
    public async Task Registration_builds_and_resolves_the_provider_context_accessor_and_scoped_store()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-placement-registration-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddPersistenceCore("registration-scope");
            services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath}"
            });

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ExecutionPlacementDbContext>();
            var accessor = scope.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextAccessor>();
            var store = scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>();

            Assert.IsType<ExecutionPlacementSqliteDbContext>(context);
            Assert.NotNull(accessor.Current.Scope);
            Assert.IsType<EfExecutionPlacementStore>(store);
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var claimed = await store.TryClaimAsync(new("wf-registration", "node-registration", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)), DateTimeOffset.UtcNow);
            Assert.Equal(ExecutionPlacementClaimOutcome.Granted, claimed.Outcome);
            Assert.NotNull(await store.FindAsync("wf-registration"));
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public async Task Feature_maps_public_settings_and_resolves_every_service_it_registers()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-placement-feature-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddPersistenceCore("feature-scope");
            var feature = new DistributedRuntimeExecutionPlacementEntityFrameworkCoreFeature
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath}",
                ConnectionName = "IgnoredBecauseExplicitConnectionWins"
            };

            feature.ConfigureServices(services);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var configured = provider.GetRequiredService<DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions>();
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ExecutionPlacementDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>();

            Assert.Equal(feature.Provider, configured.Provider);
            Assert.Equal(feature.ConnectionString, configured.ConnectionString);
            Assert.Equal(feature.ConnectionName, configured.ConnectionName);
            Assert.IsType<ExecutionPlacementSqliteDbContext>(context);
            Assert.IsType<EfExecutionPlacementStore>(store);
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public void Feature_registration_entry_point_is_overridable()
    {
        var services = new ServiceCollection();
        var feature = new DerivedExecutionPlacementFeature
        {
            Provider = Options.Provider,
            ConnectionString = Options.ConnectionString
        };

        feature.ConfigureServices(services);

        Assert.True(feature.WasInvoked);
        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, Backend(services));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mixed_composition_admits_only_Groundwork_transport_schema(bool groundworkFirst)
    {
        var groundworkPath = Path.Join(Path.GetTempPath(), $"elsa-placement-groundwork-{Guid.NewGuid():N}.db");
        var efPath = Path.Join(Path.GetTempPath(), $"elsa-placement-ef-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={groundworkPath}");
            var services = new ServiceCollection();
            services.AddPersistenceCore("mixed-composition-scope");
            services.AddGroundworkStorageProviderConnection(connection);
            if (groundworkFirst)
            {
                services.AddGroundworkDistributedRuntimeStores();
                services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={efPath}"
                });
            }
            else
            {
                services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(new()
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={efPath}"
                });
                services.AddGroundworkDistributedRuntimeStores();
            }

            await using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IStartupValidator>().Validate();
            await provider.GetRequiredService<GroundworkStorageSessionSource>().StartAsync(CancellationToken.None);
            var tables = await ReadSqliteTablesAsync(groundworkPath);

            Assert.DoesNotContain(DistributedGroundworkStorageManifest.PlacementUnitName, tables);
            Assert.Contains(DistributedGroundworkStorageManifest.CommandStreamHeadUnitName, tables);
            Assert.Contains(DistributedGroundworkStorageManifest.CommandTransportUnitName, tables);
        }
        finally
        {
            DeleteSqliteFiles(groundworkPath);
            DeleteSqliteFiles(efPath);
        }
    }

    private static string Backend(IServiceCollection services) => services
        .Single(descriptor => descriptor.ImplementationInstance is ExecutionPlacementStoreBackend)
        .ImplementationInstance is ExecutionPlacementStoreBackend backend
        ? backend.Name
        : throw new InvalidOperationException("The placement backend marker was not registered.");

    private static string CommandBackend(IServiceCollection services) => services
        .Single(descriptor => descriptor.ImplementationInstance is ExecutionCommandTransportBackend)
        .ImplementationInstance is ExecutionCommandTransportBackend backend
        ? backend.Name
        : throw new InvalidOperationException("The command transport backend marker was not registered.");

    private static void RegisterBackend(IServiceCollection services, string backend)
    {
        switch (backend)
        {
            case ExecutionPlacementStoreBackend.InMemory:
                new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);
                break;
            case ExecutionPlacementStoreBackend.Groundwork:
                services.AddGroundworkDistributedRuntimeStores();
                break;
            case ExecutionPlacementStoreBackend.EntityFramework:
                services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(Options);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, null);
        }
    }

    private static string ResolveConnection(
        DistributedRuntimeExecutionPlacementEntityFrameworkCoreOptions options,
        IReadOnlyDictionary<string, string?>? values = null)
    {
        var services = new ServiceCollection();
        if (values is not null)
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build());
        }

        services.AddDistributedRuntimeExecutionPlacementEntityFrameworkCore(options);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ExecutionPlacementDbContext>().Database.GetConnectionString()!;
    }

    private static string[] GroundworkUnitIds(IServiceCollection services) => services
        .Single(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
        .ImplementationInstance is GroundworkStorageUnitRegistry registry
        ? registry.Registrations.Select(candidate => candidate.Unit.Id.Value).ToArray()
        : throw new InvalidOperationException("The Groundwork storage registry was not registered as an instance.");

    private static async Task<string[]> ReadSqliteTablesAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables.ToArray();
    }

    private static void DeleteSqliteFiles(string path)
    {
        foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal" })
            File.Delete(file);
    }

    private sealed class DerivedExecutionPlacementFeature : DistributedRuntimeExecutionPlacementEntityFrameworkCoreFeature
    {
        public bool WasInvoked { get; private set; }

        public override void ConfigureServices(IServiceCollection services)
        {
            WasInvoked = true;
            base.ConfigureServices(services);
        }
    }

    private sealed class ExplicitStore : IExecutionPlacementStore
    {
        public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ExplicitTransport : IExecutionCommandTransport
    {
        public ValueTask<ExecutionCommandTransportItem> SendAsync(string workflowExecutionId, WorkflowExecutionCommandEnvelope envelope, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(string workflowExecutionId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, int maxItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> AckAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, int maxItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
