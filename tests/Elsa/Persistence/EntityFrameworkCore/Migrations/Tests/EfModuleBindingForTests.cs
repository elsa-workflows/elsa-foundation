using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// #1872 replaces each module's hand-written <see cref="EfModuleBinding"/> construction with <see
/// cref="EfModuleBinding.For"/>. Behaviour must not change: for every one of the 13 modules, this proves
/// the descriptor-derived binding equals the values the registration class used to pass by hand — same
/// owner, history table, migrations assembly and connection defaults.
/// </summary>
public sealed class EfModuleBindingForTests
{
    private static readonly (Type ContextType, EfModuleBinding Expected)[] Expected =
    [
        (typeof(SecretsDbContext), new EfModuleBinding(
            "Secrets",
            SecretsEfModule.HistoryTableName,
            typeof(SecretsDbContext).Assembly.GetName().Name,
            SecretsEfModule.DefaultConnectionName,
            SecretsEfModule.DefaultSqliteConnectionString)),
        (typeof(StudioPreferencesDbContext), new EfModuleBinding(
            "Studio Preferences",
            StudioPreferencesEfModule.HistoryTableName,
            typeof(StudioPreferencesDbContext).Assembly.GetName().Name,
            StudioPreferencesEfModule.DefaultConnectionName,
            StudioPreferencesEfModule.DefaultSqliteConnectionString)),
        (typeof(PublishingSnapshotReviewDbContext), new EfModuleBinding(
            "Publishing",
            PublishingSnapshotReviewEfModule.HistoryTableName,
            typeof(PublishingSnapshotReviewDbContext).Assembly.GetName().Name,
            PublishingSnapshotReviewEfModule.DefaultConnectionName,
            PublishingSnapshotReviewEfModule.DefaultSqliteConnectionString)),
        (typeof(WorkflowsDesignDbContext), new EfModuleBinding(
            "Workflows Design",
            WorkflowsDesignEfModule.HistoryTableName,
            typeof(WorkflowsDesignDbContext).Assembly.GetName().Name,
            WorkflowsDesignEfModule.DefaultConnectionName,
            WorkflowsDesignEfModule.DefaultSqliteConnectionString)),
        (typeof(RuntimeDbContext), new EfModuleBinding(
            "Runtime",
            RuntimeEfModule.HistoryTableName,
            typeof(RuntimeDbContext).Assembly.GetName().Name,
            RuntimeEfModule.DefaultConnectionName,
            RuntimeEfModule.DefaultSqliteConnectionString)),
        (typeof(ExecutionPlacementDbContext), new EfModuleBinding(
            "Distributed runtime execution placement",
            ExecutionPlacementEfModule.HistoryTableName,
            typeof(ExecutionPlacementDbContext).Assembly.GetName().Name,
            ExecutionPlacementEfModule.DefaultConnectionName,
            ExecutionPlacementEfModule.DefaultSqliteConnectionString)),
        (typeof(ExecutionCommandTransportDbContext), new EfModuleBinding(
            "Distributed runtime execution command transport",
            ExecutionCommandTransportEfModule.HistoryTableName,
            typeof(ExecutionCommandTransportDbContext).Assembly.GetName().Name,
            ExecutionCommandTransportEfModule.DefaultConnectionName,
            ExecutionCommandTransportEfModule.DefaultSqliteConnectionString)),
        (typeof(IdentityIamDbContext), new EfModuleBinding(
            "Identity IAM",
            IdentityIamEfModule.HistoryTableName,
            typeof(IdentityIamDbContext).Assembly.GetName().Name,
            IdentityIamEfModule.DefaultConnectionName,
            IdentityIamEfModule.DefaultSqliteConnectionString)),
        (typeof(IdentityProviderConfigurationDbContext), new EfModuleBinding(
            "Identity provider-configuration",
            IdentityProviderConfigurationEfModule.HistoryTableName,
            typeof(IdentityProviderConfigurationDbContext).Assembly.GetName().Name,
            IdentityProviderConfigurationEfModule.DefaultConnectionName,
            IdentityProviderConfigurationEfModule.DefaultSqliteConnectionString)),
        (typeof(EfOpenTelemetryDbContext), new EfModuleBinding(
            "OpenTelemetry",
            EfOpenTelemetryModule.HistoryTableName,
            typeof(EfOpenTelemetryDbContext).Assembly.GetName().Name,
            EfOpenTelemetryModule.DefaultConnectionName,
            EfOpenTelemetryModule.DefaultSqliteConnectionString)),
        (typeof(StructuredLogsDbContext), new EfModuleBinding(
            "Structured Logs",
            StructuredLogsEfModule.HistoryTableName,
            typeof(StructuredLogsDbContext).Assembly.GetName().Name,
            StructuredLogsEfModule.DefaultConnectionName,
            StructuredLogsEfModule.DefaultSqliteConnectionString)),
        (typeof(ActivitiesDesignDbContext), new EfModuleBinding(
            "Activities Design",
            ActivitiesDesignEfModule.HistoryTableName,
            typeof(ActivitiesDesignDbContext).Assembly.GetName().Name,
            ActivitiesDesignEfModule.DefaultConnectionName,
            ActivitiesDesignEfModule.DefaultSqliteConnectionString)),
        (typeof(Elsa3ImportDbContext), new EfModuleBinding(
            "Elsa 3 import",
            Elsa3ImportEfModule.HistoryTableName,
            typeof(Elsa3ImportDbContext).Assembly.GetName().Name,
            Elsa3ImportEfModule.DefaultConnectionName,
            Elsa3ImportEfModule.DefaultSqliteConnectionString))
    ];

    [Fact]
    public void Covers_all_13_modules()
    {
        Assert.Equal(13, Expected.Length);
    }

    [Theory]
    [MemberData(nameof(ExpectedCases))]
    public void For_reproduces_the_modules_hand_written_binding(Type contextType, EfModuleBinding expected)
    {
        Assert.Equal(expected, EfModuleBinding.For(contextType));
    }

    public static IEnumerable<object[]> ExpectedCases() =>
        Expected.Select(entry => new object[] { entry.ContextType, entry.Expected });
}
