using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Owns the Secrets EF pilot allowlist: the exact project and source-file inventory under the pilot's paths.
/// The pilot's bounded policy is superseded, but its admitted implementation stays as the
/// reviewed starting surface; later EF replacements must extend the ratchet with their own evidence.
/// </summary>
/// <remarks>
/// Until #1878 one test here held the Secrets <c>Tooling/</c> project to being the only Secrets EF package
/// referencing a provider engine. With that project retired, no Secrets EF package carries one at all, and
/// that stronger statement is enforced by two guards rather than by naming a project:
/// <see cref="Policy_and_module_packages_stay_provider_free"/> pins the module's own declared packages, and
/// <c>EfCoreDependencyGuardTests.Every_admitted_Secrets_pilot_project_resolves_only_its_reviewed_EF_closure</c>
/// pins every admitted project's <i>resolved</i> closure in both directions — over exactly the project set
/// <see cref="Pilot_paths_and_projects_are_the_reviewed_adr_0072_allowlist"/> holds fixed.
/// </remarks>
public sealed class SecretsEfPersistencePilotArchitectureTests
{
    private static readonly string[] PolicyPackages =
    [
        "CShells",
        "CShells.Abstractions",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.Extensions.Configuration.EnvironmentVariables",
        "Microsoft.Extensions.Configuration.Json",
        "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.Options"
    ];

    private static readonly string[] ModulePackages =
    [
        "CShells.Abstractions",
        "Elsa.Specifications.PackageManifest.Generator",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting.Abstractions"
    ];

    private static readonly string[] ForbiddenProviderPackages =
    [
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "MySql.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore.PostgreSQL"
    ];

    private static readonly string[] PilotSources =
    [
        "server/essentials/src/Persistence/EntityFramework/AssemblyInfo.cs",
        "server/essentials/src/Persistence/EntityFramework/EfConnectionDefaults.cs",
        "server/essentials/src/Persistence/EntityFramework/EfDatabaseMigrator.cs",
        "server/essentials/src/Persistence/EntityFramework/EfHostConfigurationReader.cs",
        "server/essentials/src/Persistence/EntityFramework/EfHostStoreOptions.cs",
        "server/essentials/src/Persistence/EntityFramework/EfMigrateOptions.cs",
        "server/essentials/src/Persistence/EntityFramework/EfMigratePolicy.cs",
        "server/essentials/src/Persistence/EntityFramework/EfMigrationHistoryRepository.cs",
        "server/essentials/src/Persistence/EntityFramework/EfMigrationIncludesAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/EfMigrationsHistory.cs",
        "server/essentials/src/Persistence/EntityFramework/EfModuleAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/EfModuleBinding.cs",
        "server/essentials/src/Persistence/EntityFramework/EfModuleCatalog.cs",
        "server/essentials/src/Persistence/EntityFramework/EfModuleDescriptor.cs",
        "server/essentials/src/Persistence/EntityFramework/EfModuleMigrator.cs",
        "server/essentials/src/Persistence/EntityFramework/EfOrdinalCollation.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPayloadCodec.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPayloadColumns.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPayloadCompression.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPayloadCompressionOptions.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPayloadCompressionOptionsExtension.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPayloadCompressionSettings.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPendingMigrationsException.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPersistenceCommand.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPersistenceResourceParticipantAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPostMigrationActions.cs",
        "server/essentials/src/Persistence/EntityFramework/EfPostMigrationRequiredException.cs",
        "server/essentials/src/Persistence/EntityFramework/EfProviderBindingValidator.cs",
        "server/essentials/src/Persistence/EntityFramework/EfProviderGuard.cs",
        "server/essentials/src/Persistence/EntityFramework/EfProviderNames.cs",
        "server/essentials/src/Persistence/EntityFramework/EfRelationalExceptionClassifier.cs",
        "server/essentials/src/Persistence/EntityFramework/EfRelationalIdentity.cs",
        "server/essentials/src/Persistence/EntityFramework/EfRelationalModelSource.cs",
        "server/essentials/src/Persistence/EntityFramework/EfRelationalProviderBinding.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchema.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaChain.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaContentAddressedAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaContentAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaFamilyAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaFinalization.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaIntegrityAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaMigrationsAssembly.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaOptionsExtension.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaRowContent.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaUpcasterAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaVersion.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaVersionMaterializationInterceptor.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaVersionSkewException.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSchemaWriteRefusedException.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSettingsSection.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSharedTransaction.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSourceModel.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSourceRevisions.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSqliteMigrationLock.cs",
        "server/essentials/src/Persistence/EntityFramework/EfSqliteSerialOpenInterceptor.cs",
        "server/essentials/src/Persistence/EntityFramework/EfWriteConflict.cs",
        "server/essentials/src/Persistence/EntityFramework/EfWriteRetry.cs",
        "server/essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationFamilies.cs",
        "server/essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationGuard.cs",
        "server/essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationOptOutAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/IEfPostMigrationAction.cs",
        "server/essentials/src/Persistence/EntityFramework/IEfSchemaUpcaster.cs",
        "server/essentials/src/Persistence/EntityFramework/IEfSchemaVersionedContext.cs",
        "server/essentials/src/Persistence/EntityFramework/ResourceResolution/EfPersistencePreparation.cs",
        "server/essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceConfigurationAdapter.cs",
        "server/essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceResourceModels.cs",
        "server/essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceResourceResolver.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfill.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillFinish.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillOptions.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillPasses.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillRun.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillScope.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillSettle.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillStatusBoard.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaStampedTable.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaBackfill/IEfSchemaRowRewriter.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfContractingMigrationCheck.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfContractingMigrationRefusedException.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfInsertIfAbsent.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaActivationRefusedException.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFamilyWritesRefusedException.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationCheck.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationGatesExtensions.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationOptions.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationRows.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationStore.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaModuleFamilies.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaModuleGate.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaWriteGateInterceptor.cs",
        "server/essentials/src/Persistence/EntityFramework/SchemaFinalization/IEfSchemaBackfillStatusSource.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInputsAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionContract.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfCandidateInspectionContract.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfCandidateInspectionOperation.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfMigrationPlan.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfModuleOrder.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfMySqlIdempotentScript.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfPersistenceParticipantCatalog.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfPersistenceResourceValidator.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfProviderAgreement.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingContextContract.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingContextOperation.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingContract.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingExitCode.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingFleet.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingHost.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsAttribute.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsDeclaration.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/EfToolingTargetSelection.cs",
        "server/essentials/src/Persistence/EntityFramework/Tooling/IEfToolingShellDefaults.cs",
        "server/essentials/src/Persistence/EntityFramework/UnicodeOrdinalCasingTable.Generated.cs",
        "server/essentials/src/Persistence/EntityFramework/UnicodeOrdinalCasingTable.cs",
        "server/essentials/src/Persistence/EntityFramework/UsesEfModuleAttribute.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/AssemblyInfo.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Configuration/SecretRecordConfiguration.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/DependencyInjection/SecretsEntityFrameworkCoreRegistration.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Entities/SecretRecord.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928002150_SchemaVersionStamp.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928002150_SchemaVersionStamp.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928040738_SchemaFinalization.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928040738_SchemaFinalization.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20261020202445_FreeNameOnDelete.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20261020202445_FreeNameOnDelete.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/SecretsMySqlDbContextModelSnapshot.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928002110_SchemaVersionStamp.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928002110_SchemaVersionStamp.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928040734_SchemaFinalization.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928040734_SchemaFinalization.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20261020202251_FreeNameOnDelete.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20261020202251_FreeNameOnDelete.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/SecretsPostgreSqlDbContextModelSnapshot.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260918221616_OrdinalCollation.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260918221616_OrdinalCollation.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928002031_SchemaVersionStamp.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928002031_SchemaVersionStamp.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928040730_SchemaFinalization.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928040730_SchemaFinalization.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20261020202043_FreeNameOnDelete.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20261020202043_FreeNameOnDelete.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/SecretsSqlServerDbContextModelSnapshot.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928002001_SchemaVersionStamp.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928002001_SchemaVersionStamp.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928040726_SchemaFinalization.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928040726_SchemaFinalization.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20261020201900_FreeNameOnDelete.Designer.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20261020201900_FreeNameOnDelete.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/SecretsSqliteDbContextModelSnapshot.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsDbContext.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsEfModule.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsEntityFrameworkCoreFeature.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsMySqlDbContext.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsPostgreSqlDbContext.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsSqlServerDbContext.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsSqliteDbContext.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Serialization/SecretsEfJson.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/EfSecretRepository.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretDocument.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretRevisionMapper.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionContract.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionException.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionReindex.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsSearchKeys.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsUnicodeOrdinalIgnoreCaseV1.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsV1Upcaster.cs",
        "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsWriteVersion.cs",
        "server/essentials/tests/Persistence/EntityFramework/BindingDriftTests/EfRelationalProviderBindingDriftTests.Reuse.cs",
        "server/essentials/tests/Persistence/EntityFramework/BindingDriftTests/EfRelationalProviderBindingDriftTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/BackfillScenario.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/BackfillWithdrawalRace.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/CommandCaptureInterceptor.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/ConfigurationServices.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/ContractingModule.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/ContractingPairModule.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/ContractingSeedScenarios.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/DiagnosticsPersistenceBindingTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfConnectionDefaultsTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfContractingMigrationRefusalTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfDatabaseMigratorTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfHostConfigurationReaderTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfMigrateOptionsTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfMigrationsHistoryTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfOrdinalCollationTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfPayloadCodecTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfPayloadColumnsTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfPayloadCompressionSettingsTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfPersistenceCommandTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfPostMigrationActionsTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfProviderBindingValidatorTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfProviderGuardTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfRelationalExceptionClassifierTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfRelationalIdentityTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfRelationalModelInitializationTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfRelationalProviderBindingTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaBackfillOptionsTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaBackfillTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaChainStoreTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaChainTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFamilyCatalogAcrossAssembliesTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationInterleavingTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationStoreTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationTestFilter.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationTestSupport.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaModuleGateTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaUpcasterFixtureSupport.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionMaterializationInterceptorTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionSkewTestSupport.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaWriteGateInterceptorTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSchemaWriteRefusedExceptionTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSqliteSerialOpenInterceptorTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfSqliteSerialOpenStressTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfToolingConfigurationContextTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfToolingTargetSelectionTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/EfWriteRetryTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/FakeFleet.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/FinalizationInsert.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/MigrationsAssemblyAssert.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/PersistenceConfigurationAdapterTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/PersistenceResolutionEvidenceTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/PersistenceResourceResolverTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/PersistenceResourceValidationTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/ProviderFailures.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/SchemaChains.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/SchemaGateSupport.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/SharedPersistenceLayoutTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/SourceRevisionSchemaFamilyTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/SourceRevisionTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/SqliteMigrationLockRow.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/StartupInsertRace.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/SyntheticOrders.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/TemporarySqliteDatabase.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/TemporarySqliteDatabaseTests.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/UnorderedRowLimitGuard.cs",
        "server/essentials/tests/Persistence/EntityFramework/Tests/UnorderedRowLimitGuardTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/MySqlContainerFixture.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/MySqlEfSecretRepositoryTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Program.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlEfSecretRepositoryTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlSecretsShellJourneyTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgresContainerFixture.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/SecretsPackageFeedProbeTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Support/SecretsPackageFeedProbeRunner.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerContainerFixture.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerEfSecretRepositoryTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfMigrationGeneratorTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfModuleMigrationTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreFeatureTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreShellReloadTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsFeatureKeySettingsShellTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsHostCatalog.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsNameSchemaTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceCompositionTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceHostJourneyTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionContractTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionReindexTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsSearchKeysTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SqliteEfSecretRepositoryTests.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PersistenceCliProcessRunner.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PreReuseSecretsModel.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PreStampSecretsModel.cs",
        "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/SecretNameReuseScenario.cs",
    ];

    [Fact]
    public void Pilot_paths_and_projects_are_the_reviewed_adr_0072_allowlist()
    {
        Assert.Equal(
            [
                "server/essentials/src/Persistence/EntityFramework/",
                "server/essentials/src/Secrets/Persistence/EntityFrameworkCore/",
                "server/essentials/tests/Persistence/EntityFramework/",
                "server/essentials/tests/Secrets/Persistence/EntityFrameworkCore/"
            ],
            EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.SurfacePathPrefixes);

        var projects = EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.SurfacePathPrefixes
            .SelectMany(prefix => Directory.EnumerateFiles(
                RepoPath(prefix.TrimEnd('/').Split('/')), "*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !IsBuildOutput(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.ProjectPaths, projects);
    }

    [Fact]
    public void Policy_and_module_packages_stay_provider_free()
    {
        var policy = PackageIncludes(RepoPath("server", "essentials", "src", "Persistence", "EntityFramework", "Elsa.Persistence.EntityFramework.csproj"));
        var module = PackageIncludes(RepoPath("server", "essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore", "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"));

        Assert.Equal(PolicyPackages, policy);
        Assert.Equal(ModulePackages, module);
        Assert.Empty(policy.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
        Assert.Empty(module.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void PostgreSql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "server", "essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj"));

        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void MySql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "server", "essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "MySql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests.csproj"));

        Assert.Equal(
            ["MySql.EntityFrameworkCore"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void Package_feed_probe_has_one_provider_and_no_compile_time_secrets_ef_module_reference()
    {
        var projectPath = RepoPath(
            "server", "essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "PackageFeedProbe",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.csproj");
        var project = XDocument.Load(projectPath);
        var packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        var projects = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
        Assert.DoesNotContain(
            projects,
            reference => reference.Contains("Secrets\\Persistence\\EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Module_package_declares_host_integrated_nuplane_loading()
    {
        var projectPath = RepoPath(
            "server", "essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj");
        var project = XDocument.Load(projectPath);
        var metadataItem = Assert.Single(project.Descendants("None"), element =>
            string.Equals(element.Attribute("Update")?.Value, "nuplane.json", StringComparison.Ordinal));
        Assert.Equal("true", metadataItem.Attribute("Pack")?.Value);
        Assert.Equal("/", metadataItem.Attribute("PackagePath")?.Value);

        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            RepoPath("server", "essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore", "nuplane.json")));
        var loading = metadata.RootElement.GetProperty("loading");
        Assert.Equal("HostIntegrated", loading.GetProperty("loadMode").GetString());
        Assert.Equal("DependencyClosure", loading.GetProperty("scope").GetString());
    }

    [Fact]
    public void Pilot_sources_are_the_exact_reviewed_inventory()
    {
        var actual = EfCoreDependencyGuardTests.Adr0072SecretsEfPilot.SurfacePathPrefixes
            .SelectMany(prefix => Directory.EnumerateFiles(RepoPath(prefix.TrimEnd('/').Split('/')), "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/')))
            .Where(path => !IsBuildOutput(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(PilotSources, actual);
    }

    [Fact]
    public void Workbench_enables_the_secrets_ef_module_by_default()
    {
        var workbench = XDocument.Load(RepoPath("server/apps", "Elsa.Workbench.Host", "Elsa.Workbench.Host.csproj"));
        var references = workbench.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();
        Assert.Contains(
            references,
            reference => reference.Contains("Secrets.Persistence.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                         && !reference.Contains("Tooling", StringComparison.OrdinalIgnoreCase));

        // EF is the only Secrets persistence family.
        foreach (var relative in new[]
                 {
                     "server/apps/Elsa.Workbench.Host/shells.json",
                     "docker/compose/elsa-workbench.shells.json"
                 })
        {
            Assert.True(
                DefaultShellFeatures(relative).Contains("SecretsEntityFrameworkCore"),
                $"{relative} must enable SecretsEntityFrameworkCore as the default Secrets store.");
        }
    }

    private static HashSet<string> DefaultShellFeatures(string relative)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(RepoPath(relative.Split('/'))),
            new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        return document.RootElement
            .GetProperty("CShells").GetProperty("Shells").GetProperty("default").GetProperty("Features")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> PackageIncludes(string csproj) =>
        XDocument.Load(csproj)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string RepoPath(params string[] segments) => Path.Join([RepoRoot, ..segments]);
}
