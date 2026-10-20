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
