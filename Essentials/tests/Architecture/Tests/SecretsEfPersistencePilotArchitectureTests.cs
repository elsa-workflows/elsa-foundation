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
        "Essentials/src/Persistence/EntityFramework/AssemblyInfo.cs",
        "Essentials/src/Persistence/EntityFramework/EfConnectionDefaults.cs",
        "Essentials/src/Persistence/EntityFramework/EfDatabaseMigrator.cs",
        "Essentials/src/Persistence/EntityFramework/EfHostConfigurationReader.cs",
        "Essentials/src/Persistence/EntityFramework/EfHostStoreOptions.cs",
        "Essentials/src/Persistence/EntityFramework/EfMigrateOptions.cs",
        "Essentials/src/Persistence/EntityFramework/EfMigratePolicy.cs",
        "Essentials/src/Persistence/EntityFramework/EfMigrationHistoryRepository.cs",
        "Essentials/src/Persistence/EntityFramework/EfMigrationIncludesAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/EfMigrationsHistory.cs",
        "Essentials/src/Persistence/EntityFramework/EfModuleAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/EfModuleBinding.cs",
        "Essentials/src/Persistence/EntityFramework/EfModuleCatalog.cs",
        "Essentials/src/Persistence/EntityFramework/EfModuleDescriptor.cs",
        "Essentials/src/Persistence/EntityFramework/EfModuleMigrator.cs",
        "Essentials/src/Persistence/EntityFramework/EfOrdinalCollation.cs",
        "Essentials/src/Persistence/EntityFramework/EfPayloadCodec.cs",
        "Essentials/src/Persistence/EntityFramework/EfPayloadColumns.cs",
        "Essentials/src/Persistence/EntityFramework/EfPayloadCompression.cs",
        "Essentials/src/Persistence/EntityFramework/EfPayloadCompressionOptions.cs",
        "Essentials/src/Persistence/EntityFramework/EfPayloadCompressionOptionsExtension.cs",
        "Essentials/src/Persistence/EntityFramework/EfPayloadCompressionSettings.cs",
        "Essentials/src/Persistence/EntityFramework/EfPendingMigrationsException.cs",
        "Essentials/src/Persistence/EntityFramework/EfPersistenceCommand.cs",
        "Essentials/src/Persistence/EntityFramework/EfPersistenceResourceParticipantAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/EfPostMigrationActions.cs",
        "Essentials/src/Persistence/EntityFramework/EfPostMigrationRequiredException.cs",
        "Essentials/src/Persistence/EntityFramework/EfProviderBindingValidator.cs",
        "Essentials/src/Persistence/EntityFramework/EfProviderGuard.cs",
        "Essentials/src/Persistence/EntityFramework/EfProviderNames.cs",
        "Essentials/src/Persistence/EntityFramework/EfRelationalExceptionClassifier.cs",
        "Essentials/src/Persistence/EntityFramework/EfRelationalIdentity.cs",
        "Essentials/src/Persistence/EntityFramework/EfRelationalProviderBinding.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchema.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaChain.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaContentAddressedAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaContentAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaFamilyAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaFinalization.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaIntegrityAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaMigrationsAssembly.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaOptionsExtension.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaRowContent.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaUpcasterAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaVersion.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaVersionMaterializationInterceptor.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaVersionSkewException.cs",
        "Essentials/src/Persistence/EntityFramework/EfSchemaWriteRefusedException.cs",
        "Essentials/src/Persistence/EntityFramework/EfSettingsSection.cs",
        "Essentials/src/Persistence/EntityFramework/EfSharedTransaction.cs",
        "Essentials/src/Persistence/EntityFramework/EfSqliteMigrationLock.cs",
        "Essentials/src/Persistence/EntityFramework/EfSqliteSerialOpenInterceptor.cs",
        "Essentials/src/Persistence/EntityFramework/EfWriteConflict.cs",
        "Essentials/src/Persistence/EntityFramework/EfWriteRetry.cs",
        "Essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationFamilies.cs",
        "Essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationGuard.cs",
        "Essentials/src/Persistence/EntityFramework/ExpandOnlyMigrationOptOutAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/IEfPostMigrationAction.cs",
        "Essentials/src/Persistence/EntityFramework/IEfSchemaUpcaster.cs",
        "Essentials/src/Persistence/EntityFramework/IEfSchemaVersionedContext.cs",
        "Essentials/src/Persistence/EntityFramework/ResourceResolution/EfPersistencePreparation.cs",
        "Essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceConfigurationAdapter.cs",
        "Essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceResourceModels.cs",
        "Essentials/src/Persistence/EntityFramework/ResourceResolution/PersistenceResourceResolver.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfill.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillFinish.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillOptions.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillPasses.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillRun.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillScope.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillSettle.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaBackfillStatusBoard.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/EfSchemaStampedTable.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaBackfill/IEfSchemaRowRewriter.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfContractingMigrationCheck.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfContractingMigrationRefusedException.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfInsertIfAbsent.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaActivationRefusedException.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFamilyWritesRefusedException.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationCheck.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationGatesExtensions.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationOptions.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationRows.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationStore.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaModuleFamilies.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaModuleGate.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaWriteGateInterceptor.cs",
        "Essentials/src/Persistence/EntityFramework/SchemaFinalization/IEfSchemaBackfillStatusSource.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInputsAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionContract.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfCandidateInspectionContract.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfCandidateInspectionOperation.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfMigrationPlan.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfModuleOrder.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfMySqlIdempotentScript.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfPersistenceParticipantCatalog.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfPersistenceResourceValidator.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfProviderAgreement.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingContextContract.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingContextOperation.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingContract.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingExitCode.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingFleet.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingHost.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsAttribute.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingShellDefaultsDeclaration.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/EfToolingTargetSelection.cs",
        "Essentials/src/Persistence/EntityFramework/Tooling/IEfToolingShellDefaults.cs",
        "Essentials/src/Persistence/EntityFramework/UnicodeOrdinalCasingTable.Generated.cs",
        "Essentials/src/Persistence/EntityFramework/UnicodeOrdinalCasingTable.cs",
        "Essentials/src/Persistence/EntityFramework/UsesEfModuleAttribute.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/AssemblyInfo.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Configuration/SecretRecordConfiguration.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/DependencyInjection/SecretsEntityFrameworkCoreRegistration.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Entities/SecretRecord.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928002150_SchemaVersionStamp.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928002150_SchemaVersionStamp.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928040738_SchemaFinalization.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928040738_SchemaFinalization.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20261020202445_FreeNameOnDelete.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20261020202445_FreeNameOnDelete.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/SecretsMySqlDbContextModelSnapshot.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928002110_SchemaVersionStamp.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928002110_SchemaVersionStamp.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928040734_SchemaFinalization.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928040734_SchemaFinalization.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20261020202251_FreeNameOnDelete.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20261020202251_FreeNameOnDelete.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/SecretsPostgreSqlDbContextModelSnapshot.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260910210213_Initial.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260911010804_WidenLookupKeys.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260918221616_OrdinalCollation.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260918221616_OrdinalCollation.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928002031_SchemaVersionStamp.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928002031_SchemaVersionStamp.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928040730_SchemaFinalization.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20260928040730_SchemaFinalization.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20261020202043_FreeNameOnDelete.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/20261020202043_FreeNameOnDelete.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/SqlServer/SecretsSqlServerDbContextModelSnapshot.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260910210210_Initial.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260911010717_WidenLookupKeys.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928002001_SchemaVersionStamp.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928002001_SchemaVersionStamp.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928040726_SchemaFinalization.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20260928040726_SchemaFinalization.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20261020201900_FreeNameOnDelete.Designer.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/20261020201900_FreeNameOnDelete.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/Sqlite/SecretsSqliteDbContextModelSnapshot.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsDbContext.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsEfModule.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsEntityFrameworkCoreFeature.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsMySqlDbContext.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsPostgreSqlDbContext.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsSqlServerDbContext.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsSqliteDbContext.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Serialization/SecretsEfJson.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/EfSecretRepository.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretDocument.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretRevisionMapper.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionContract.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionException.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsProjectionReindex.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsSearchKeys.cs",
        "Essentials/src/Secrets/Persistence/EntityFrameworkCore/Stores/SecretsUnicodeOrdinalIgnoreCaseV1.cs",
        "Essentials/tests/Persistence/EntityFramework/BindingDriftTests/EfRelationalProviderBindingDriftTests.Reuse.cs",
        "Essentials/tests/Persistence/EntityFramework/BindingDriftTests/EfRelationalProviderBindingDriftTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/BackfillScenario.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/BackfillWithdrawalRace.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/CommandCaptureInterceptor.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/ConfigurationServices.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/ContractingModule.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/ContractingPairModule.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/ContractingSeedScenarios.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/DiagnosticsPersistenceBindingTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfConnectionDefaultsTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfContractingMigrationRefusalTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfDatabaseMigratorTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfHostConfigurationReaderTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfMigrateOptionsTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfMigrationsHistoryTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfOrdinalCollationTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfPayloadCodecTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfPayloadColumnsTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfPayloadCompressionSettingsTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfPersistenceCommandTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfPostMigrationActionsTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfProviderBindingValidatorTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfProviderGuardTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfRelationalExceptionClassifierTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfRelationalIdentityTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfRelationalProviderBindingTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaBackfillOptionsTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaBackfillTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaChainStoreTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaChainTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFamilyCatalogAcrossAssembliesTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationInterleavingTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationStoreTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationTestFilter.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaFinalizationTestSupport.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaModuleGateTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaUpcasterFixtureSupport.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionMaterializationInterceptorTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionSkewTestSupport.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaVersionTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaWriteGateInterceptorTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaWriteRefusedExceptionTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSqliteSerialOpenInterceptorTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfSqliteSerialOpenStressTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfToolingConfigurationContextTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfToolingTargetSelectionTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/EfWriteRetryTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/FakeFleet.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/FinalizationInsert.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/MigrationsAssemblyAssert.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/PersistenceConfigurationAdapterTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/PersistenceResolutionEvidenceTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/PersistenceResourceResolverTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/PersistenceResourceValidationTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/ProviderFailures.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/SchemaChains.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/SchemaGateSupport.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/SharedPersistenceLayoutTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/SqliteMigrationLockRow.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/StartupInsertRace.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/SyntheticOrders.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/TemporarySqliteDatabase.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/TemporarySqliteDatabaseTests.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/UnorderedRowLimitGuard.cs",
        "Essentials/tests/Persistence/EntityFramework/Tests/UnorderedRowLimitGuardTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/MySqlContainerFixture.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/MySqlEfSecretRepositoryTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Program.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlEfSecretRepositoryTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgreSqlSecretsShellJourneyTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/PostgresContainerFixture.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/SecretsPackageFeedProbeTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Support/SecretsPackageFeedProbeRunner.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerContainerFixture.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/SqlServerEfSecretRepositoryTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfMigrationGeneratorTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfModuleMigrationTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreFeatureTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEntityFrameworkCoreShellReloadTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsFeatureKeySettingsShellTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsHostCatalog.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceCompositionTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsPersistenceHostJourneyTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionContractTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsProjectionReindexTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsSearchKeysTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/SqliteEfSecretRepositoryTests.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PersistenceCliProcessRunner.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PreReuseSecretsModel.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/PreStampSecretsModel.cs",
        "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Support/SecretNameReuseScenario.cs"
    ];

    [Fact]
    public void Pilot_paths_and_projects_are_the_reviewed_adr_0072_allowlist()
    {
        Assert.Equal(
            [
                "Essentials/src/Persistence/EntityFramework/",
                "Essentials/src/Secrets/Persistence/EntityFrameworkCore/",
                "Essentials/tests/Persistence/EntityFramework/",
                "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/"
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
        var policy = PackageIncludes(RepoPath("Essentials", "src", "Persistence", "EntityFramework", "Elsa.Persistence.EntityFramework.csproj"));
        var module = PackageIncludes(RepoPath("Essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore", "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"));

        Assert.Equal(PolicyPackages, policy);
        Assert.Equal(ModulePackages, module);
        Assert.Empty(policy.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
        Assert.Empty(module.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void PostgreSql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "Essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj"));

        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void MySql_shell_proof_supplies_exactly_one_relational_provider_package()
    {
        var packages = PackageIncludes(RepoPath(
            "Essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "MySql", "Tests",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests.csproj"));

        Assert.Equal(
            ["MySql.EntityFrameworkCore"],
            packages.Intersect(ForbiddenProviderPackages, StringComparer.Ordinal));
    }

    [Fact]
    public void Package_feed_probe_has_one_provider_and_no_compile_time_secrets_ef_module_reference()
    {
        var projectPath = RepoPath(
            "Essentials", "tests", "Secrets", "Persistence", "EntityFrameworkCore", "PostgreSql", "PackageFeedProbe",
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
            "Essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore",
            "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj");
        var project = XDocument.Load(projectPath);
        var metadataItem = Assert.Single(project.Descendants("None"), element =>
            string.Equals(element.Attribute("Update")?.Value, "nuplane.json", StringComparison.Ordinal));
        Assert.Equal("true", metadataItem.Attribute("Pack")?.Value);
        Assert.Equal("/", metadataItem.Attribute("PackagePath")?.Value);

        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            RepoPath("Essentials", "src", "Secrets", "Persistence", "EntityFrameworkCore", "nuplane.json")));
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
        var workbench = XDocument.Load(RepoPath("Essentials", "apps", "Elsa.Workbench", "Elsa.Workbench.csproj"));
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
                     "Essentials/apps/Elsa.Workbench/shells.json",
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
