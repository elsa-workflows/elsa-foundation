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
        "essentials/src/Persistence/EntityFramework/AssemblyInfo.cs",
        "essentials/src/Persistence/EntityFramework/EfConnectionDefaults.cs",
        "essentials/src/Persistence/EntityFramework/EfDatabaseMigrator.cs",
        "essentials/src/Persistence/EntityFramework/EfHostConfigurationReader.cs",
        "essentials/src/Persistence/EntityFramework/EfHostStoreOptions.cs",
        "essentials/src/Persistence/EntityFramework/EfMigrateOptions.cs",
        "essentials/src/Persistence/EntityFramework/EfMigratePolicy.cs",
        "essentials/src/Persistence/EntityFramework/EfMigrationHistoryRepository.cs",
        "essentials/src/Persistence/EntityFramework/EfMigrationIncludesAttribute.cs",
        "essentials/src/Persistence/EntityFramework/EfMigrationsHistory.cs",
        "essentials/src/Persistence/EntityFramework/EfModuleAttribute.cs",
        "essentials/src/Persistence/EntityFramework/EfModuleBinding.cs",
        "essentials/src/Persistence/EntityFramework/EfModuleCatalog.cs",
        "essentials/src/Persistence/EntityFramework/EfModuleDescriptor.cs",
        "essentials/src/Persistence/EntityFramework/EfModuleMigrator.cs",
        "essentials/src/Persistence/EntityFramework/EfOrdinalCollation.cs",
        "essentials/src/Persistence/EntityFramework/EfPayloadCodec.cs",
        "essentials/src/Persistence/EntityFramework/EfPayloadColumns.cs",
        "essentials/src/Persistence/EntityFramework/EfPayloadCompression.cs",
        "essentials/src/Persistence/EntityFramework/EfPayloadCompressionOptions.cs",
        "essentials/src/Persistence/EntityFramework/EfPayloadCompressionOptionsExtension.cs",
        "essentials/src/Persistence/EntityFramework/EfPayloadCompressionSettings.cs",
        "essentials/src/Persistence/EntityFramework/EfPendingMigrationsException.cs",
        "essentials/src/Persistence/EntityFramework/EfPersistenceCommand.cs",
        "essentials/src/Persistence/EntityFramework/EfPersistenceResourceParticipantAttribute.cs",
        "essentials/src/Persistence/EntityFramework/EfPostMigrationActions.cs",
        "essentials/src/Persistence/EntityFramework/EfPostMigrationRequiredException.cs",
        "essentials/src/Persistence/EntityFramework/EfProviderBindingValidator.cs",
        "essentials/src/Persistence/EntityFramework/EfProviderGuard.cs",
        "essentials/src/Persistence/EntityFramework/EfProviderNames.cs",
        "essentials/src/Persistence/EntityFramework/EfRelationalExceptionClassifier.cs",
        "essentials/src/Persistence/EntityFramework/EfRelationalIdentity.cs",
        "essentials/src/Persistence/EntityFramework/EfRelationalModelSource.cs",
        "essentials/src/Persistence/EntityFramework/EfRelationalProviderBinding.cs",
        "essentials/src/Persistence/EntityFramework/EfSchema.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaChain.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaContentAddressedAttribute.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaContentAttribute.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaFamilyAttribute.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaFinalization.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaIntegrityAttribute.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaMigrationsAssembly.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaOptionsExtension.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaRowContent.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaUpcasterAttribute.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaVersion.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaVersionMaterializationInterceptor.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaVersionSkewException.cs",
        "essentials/src/Persistence/EntityFramework/EfSchemaWriteRefusedException.cs",
        "essentials/src/Persistence/EntityFramework/EfSettingsSection.cs",
        "essentials/src/Persistence/EntityFramework/EfSharedTransaction.cs",
        "essentials/src/Persistence/EntityFramework/EfSourceModel.cs",
        "essentials/src/Persistence/EntityFramework/EfSourceRevisions.cs",
        "essentials/src/Persistence/EntityFramework/EfSqliteMigrationLock.cs",
        "essentials/src/Persistence/EntityFramework/EfSqliteSerialOpenInterceptor.cs",
        "essentials/src/Persistence/EntityFramework/EfWriteConflict.cs",
        "essentials/src/Persistence/EntityFramework/EfWriteRetry.cs",
        "essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationFamilies.cs",
        "essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationGuard.cs",
        "essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationOptOutAttribute.cs",
        "essentials/src/Persistence/EntityFramework/IEfPostMigrationAction.cs",
        "essentials/src/Persistence/EntityFramework/IEfSchemaUpcaster.cs",
        "essentials/src/Persistence/EntityFramework/IEfSchemaVersionedContext.cs",
        "essentials/src/Persistence/EntityFramework/ResourceResolution/EfPersistencePreparation.cs",
        "essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceConfigurationAdapter.cs",
        "essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceResourceModels.cs",
        "essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceResourceResolver.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfill.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillFinish.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillOptions.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillPasses.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillRun.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillScope.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillSettle.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillStatusBoard.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaStampedTable.cs",
        "essentials/src/Persistence/EntityFramework/SchemaBackfill/IEfSchemaRowRewriter.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfContractingMigrationCheck.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfContractingMigrationRefusedException.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfInsertIfAbsent.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaActivationRefusedException.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFamilyWritesRefusedException.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationCheck.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationGatesExtensions.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationOptions.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationRows.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationStore.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaModuleFamilies.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaModuleGate.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaWriteGateInterceptor.cs",
        "essentials/src/Persistence/EntityFramework/SchemaFinalization/IEfSchemaBackfillStatusSource.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInputsAttribute.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionContract.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfCandidateInspectionContract.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfCandidateInspectionOperation.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfMigrationPlan.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfModuleOrder.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfMySqlIdempotentScript.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfPersistenceParticipantCatalog.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfPersistenceResourceValidator.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfProviderAgreement.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingContextContract.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingContextOperation.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingContract.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingExitCode.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingFleet.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingHost.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsAttribute.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsDeclaration.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/EfToolingTargetSelection.cs",
        "essentials/src/Persistence/EntityFramework/Tooling/IEfToolingShellDefaults.cs",
        "essentials/src/Persistence/EntityFramework/UnicodeOrdinalCasingTable.Generated.cs",
        "essentials/src/Persistence/EntityFramework/UnicodeOrdinalCasingTable.cs",
        "essentials/src/Persistence/EntityFramework/UsesEfModuleAttribute.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/AssemblyInfo.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Configuration/SecretRecordConfiguration.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/DependencyInjection/SecretsEntityFrameworkCoreRegistration.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Entities/SecretRecord.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928002150_SchemaVersionStamp.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928002150_SchemaVersionStamp.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928040738_SchemaFinalization.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928040738_SchemaFinalization.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20261020202445_FreeNameOnDelete.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20261020202445_FreeNameOnDelete.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/SecretsMySqlDbContextModelSnapshot.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928002110_SchemaVersionStamp.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928002110_SchemaVersionStamp.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928040734_SchemaFinalization.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928040734_SchemaFinalization.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20261020202251_FreeNameOnDelete.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20261020202251_FreeNameOnDelete.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/SecretsPostgreSqlDbContextModelSnapshot.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260918221616_OrdinalCollation.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260918221616_OrdinalCollation.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928002031_SchemaVersionStamp.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928002031_SchemaVersionStamp.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928040730_SchemaFinalization.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928040730_SchemaFinalization.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20261020202043_FreeNameOnDelete.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20261020202043_FreeNameOnDelete.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/SecretsSqlServerDbContextModelSnapshot.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928002001_SchemaVersionStamp.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928002001_SchemaVersionStamp.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928040726_SchemaFinalization.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928040726_SchemaFinalization.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20261020201900_FreeNameOnDelete.Designer.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20261020201900_FreeNameOnDelete.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/SecretsSqliteDbContextModelSnapshot.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsDbContext.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsEfModule.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsEntityFrameworkCoreFeature.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsMySqlDbContext.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsPostgreSqlDbContext.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsSqlServerDbContext.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsSqliteDbContext.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Serialization/SecretsEfJson.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/EfSecretRepository.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretDocument.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretRevisionMapper.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionContract.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionException.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionReindex.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsSearchKeys.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsUnicodeOrdinalIgnoreCaseV1.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsV1Upcaster.cs",
        "essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsWriteVersion.cs",
        "essentials/tests/Persistence/EntityFramework/BindingDriftTests/EfRelationalProviderBindingDriftTests.Reuse.cs",
        "essentials/tests/Persistence/EntityFramework/BindingDriftTests/EfRelationalProviderBindingDriftTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/BackfillScenario.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/BackfillWithdrawalRace.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/CommandCaptureInterceptor.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/ConfigurationServices.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/ContractingModule.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/ContractingPairModule.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/ContractingSeedScenarios.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/DiagnosticsPersistenceBindingTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfConnectionDefaultsTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfContractingMigrationRefusalTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfDatabaseMigratorTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfHostConfigurationReaderTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfMigrateOptionsTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfMigrationsHistoryTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfOrdinalCollationTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfPayloadCodecTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfPayloadColumnsTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfPayloadCompressionSettingsTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfPersistenceCommandTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfPostMigrationActionsTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfProviderBindingValidatorTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfProviderGuardTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfRelationalExceptionClassifierTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfRelationalIdentityTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfRelationalModelInitializationTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfRelationalProviderBindingTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaBackfillOptionsTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaBackfillTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaChainStoreTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaChainTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFamilyCatalogAcrossAssembliesTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationInterleavingTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationStoreTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationTestFilter.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationTestSupport.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaModuleGateTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaUpcasterFixtureSupport.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionMaterializationInterceptorTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionSkewTestSupport.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaWriteGateInterceptorTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSchemaWriteRefusedExceptionTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSqliteSerialOpenInterceptorTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfSqliteSerialOpenStressTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfToolingConfigurationContextTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfToolingTargetSelectionTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/EfWriteRetryTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/FakeFleet.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/FinalizationInsert.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/MigrationsAssemblyAssert.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/PersistenceConfigurationAdapterTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/PersistenceResolutionEvidenceTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/PersistenceResourceResolverTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/PersistenceResourceValidationTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/ProviderFailures.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/SchemaChains.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/SchemaGateSupport.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/SharedPersistenceLayoutTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/SourceRevisionSchemaFamilyTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/SourceRevisionTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/SqliteMigrationLockRow.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/StartupInsertRace.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/SyntheticOrders.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/TemporarySqliteDatabase.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/TemporarySqliteDatabaseTests.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/UnorderedRowLimitGuard.cs",
        "essentials/tests/Persistence/EntityFramework/Tests/UnorderedRowLimitGuardTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/MySqlContainerFixture.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/MySqlEfSecretRepositoryTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Program.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlEfSecretRepositoryTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlSecretsShellJourneyTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgresContainerFixture.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/SecretsPackageFeedProbeTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Support/SecretsPackageFeedProbeRunner.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerContainerFixture.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerEfSecretRepositoryTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfMigrationGeneratorTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfModuleMigrationTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreFeatureTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreShellReloadTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsFeatureKeySettingsShellTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsHostCatalog.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsNameSchemaTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceCompositionTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceHostJourneyTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionContractTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionReindexTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsSearchKeysTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SqliteEfSecretRepositoryTests.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PersistenceCliProcessRunner.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PreReuseSecretsModel.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PreStampSecretsModel.cs",
        "essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/SecretNameReuseScenario.cs",
    ];

    [Fact]
    public void Pilot_paths_and_projects_are_the_reviewed_adr_0072_allowlist()
    {
        Assert.Equal(
            [
                "essentials/src/Persistence/EntityFramework/",
                "essentials/src/Secrets/Persistence/EntityFrameworkCore/",
                "essentials/tests/Persistence/EntityFramework/",
                "essentials/tests/Secrets/Persistence/EntityFrameworkCore/"
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
        var policy = PackageIncludes(RepoPath("essentials", "src", "Persistence", "EntityFramework", "Elsa.Persistence.EntityFramework.csproj"));
        var module = PackageIncludes(RepoPath("essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore", "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"));

        Assert.Equal(PolicyPackages, policy);
        Assert.Equal(ModulePackages, module);
        Assert.Empty(policy.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
        Assert.Empty(module.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void PostgreSql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj"));

        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void MySql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "MySql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests.csproj"));

        Assert.Equal(
            ["MySql.EntityFrameworkCore"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void Package_feed_probe_has_one_provider_and_no_compile_time_secrets_ef_module_reference()
    {
        var projectPath = RepoPath(
            "essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "PackageFeedProbe",
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
            "essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj");
        var project = XDocument.Load(projectPath);
        var metadataItem = Assert.Single(project.Descendants("None"), element =>
            string.Equals(element.Attribute("Update")?.Value, "nuplane.json", StringComparison.Ordinal));
        Assert.Equal("true", metadataItem.Attribute("Pack")?.Value);
        Assert.Equal("/", metadataItem.Attribute("PackagePath")?.Value);

        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            RepoPath("essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore", "nuplane.json")));
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
        var workbench = XDocument.Load(RepoPath("apps", "Elsa.Workbench", "Elsa.Workbench.csproj"));
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
                     "apps/Elsa.Workbench/shells.json",
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
