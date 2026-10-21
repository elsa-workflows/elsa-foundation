using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Elsa.Etl.Tests")]

[assembly: EfModule(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignEfModule.Name,
    typeof(Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignDbContext),
    HistoryModule = Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignEfModule.HistoryModuleName,
    Sqlite = typeof(Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignSqliteDbContext),
    DisplayName = "ETL design",
    DefaultConnectionName = Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignEfModule.DefaultConnectionName,
    DefaultSqliteConnectionString = Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignEfModule.DefaultSqliteConnectionString)]
[assembly: ManifestExtension("efModules", "EtlDesign")]
[assembly: EfSchemaFamily(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignEfModule.SchemaFamily,
    "EtlDesign",
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignEfModule.SchemaVersion)]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignEfModule.SchemaFamily,
    typeof(EtlDefinitionDraftRecord),
    nameof(EtlDefinitionDraftRecord.PipelineJson))]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlDesignEfModule.SchemaFamily,
    typeof(EtlPublishedArtifactRecord),
    nameof(EtlPublishedArtifactRecord.ArtifactJson))]

[assembly: EfModule(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.Name,
    typeof(Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeDbContext),
    HistoryModule = Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.HistoryModuleName,
    Sqlite = typeof(Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeSqliteDbContext),
    DisplayName = "ETL runtime")]
[assembly: ManifestExtension("efModules", "EtlRuntime")]
[assembly: EfSchemaFamily(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaFamily,
    "EtlRuntime",
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaVersion)]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaFamily,
    typeof(EtlRuntimeRunRecord),
    nameof(EtlRuntimeRunRecord.InvocationJson), nameof(EtlRuntimeRunRecord.BindingsJson))]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaFamily,
    typeof(EtlConnectorRunRecord),
    nameof(EtlConnectorRunRecord.BindingsJson), nameof(EtlConnectorRunRecord.PreparedJson))]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaFamily,
    typeof(EtlConnectorPartitionRecord),
    nameof(EtlConnectorPartitionRecord.PositionJson))]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaFamily,
    typeof(EtlConnectorBatchRecord),
    nameof(EtlConnectorBatchRecord.DescriptorJson), nameof(EtlConnectorBatchRecord.OutputJson))]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaFamily,
    typeof(EtlConnectorHttpRequestRecord),
    nameof(EtlConnectorHttpRequestRecord.RequestJson))]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaFamily,
    typeof(EtlConnectorHttpPageRecord),
    nameof(EtlConnectorHttpPageRecord.PageJson))]
[assembly: EfSchemaContent(
    Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeEfModule.SchemaFamily,
    typeof(EtlRuntimeTerminalRecord),
    nameof(EtlRuntimeTerminalRecord.ResultJson))]
