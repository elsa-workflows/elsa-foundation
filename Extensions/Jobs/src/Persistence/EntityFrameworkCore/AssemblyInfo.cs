using System.Runtime.CompilerServices;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: InternalsVisibleTo("Elsa.Jobs.Persistence.ProviderTests")]
[assembly: InternalsVisibleTo("Elsa.Jobs.Tooling")]
[assembly: EfModule(JobsEfModule.Name, typeof(JobsDbContext), HistoryModule = JobsEfModule.Name,
    Sqlite = typeof(JobsSqliteDbContext), PostgreSql = typeof(JobsPostgreSqlDbContext), DisplayName = "Jobs runtime",
    DefaultConnectionName = EfConnectionDefaults.ConnectionName,
    DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString)]
[assembly: ManifestExtension("efModules", "JobsRuntime")]
[assembly: EfSchemaFamily(JobsEfModule.Name, JobsEfModule.Name, JobsEfModule.Version)]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobRecord), nameof(JobRecord.RequestJson), nameof(JobRecord.RequestMetadataJson), nameof(JobRecord.ResultJson), nameof(JobRecord.ResultMetadataJson), nameof(JobRecord.ProgressJson), nameof(JobRecord.UnavailableDiagnosticJson))]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobAttemptRecord), nameof(JobAttemptRecord.DiagnosticJson), nameof(JobAttemptRecord.LastRenewalLeaseJson))]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobClaimGrantRecord), nameof(JobClaimGrantRecord.LeaseJson))]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobPhysicalReceiptRecord), nameof(JobPhysicalReceiptRecord.ReceiptJson), nameof(JobPhysicalReceiptRecord.MetadataJson))]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobHoldRecord), nameof(JobHoldRecord.HoldJson))]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobOperationRecord), nameof(JobOperationRecord.ResultJson))]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobScheduleRecord), nameof(JobScheduleRecord.DefinitionJson), nameof(JobScheduleRecord.UnavailableDiagnosticJson))]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobOccurrenceRecord), nameof(JobOccurrenceRecord.ReceiptJson))]
[assembly: EfSchemaContent(JobsEfModule.Name, typeof(JobDeliveryRecord), nameof(JobDeliveryRecord.AssociationJson), nameof(JobDeliveryRecord.PermissionJson), nameof(JobDeliveryRecord.ProofJson))]
