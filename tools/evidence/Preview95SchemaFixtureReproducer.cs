using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;

namespace Elsa.Persistence.Groundwork.SchemaFixtureReproducer;

internal static class Program
{
    private static readonly DateTimeOffset EvidenceTime =
        DateTimeOffset.Parse("2026-07-30T00:00:00Z");

    public static void Main(string[] args)
    {
        if (args is not [var outputDirectory] || string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Pass exactly one canonical-fixture output directory.");

        Directory.CreateDirectory(outputDirectory);
        var manifest =
            new GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema().CreateManifest();
        Write("sqlite", PhysicalSchemaTargetCompiler.Compile(
            manifest,
            SqliteGroundworkCapabilities.Provider,
            SqliteGroundworkCapabilities.PhysicalNames));
        Write("sql-server", PhysicalSchemaTargetCompiler.Compile(
            manifest,
            SqlServerGroundworkCapabilities.Provider,
            SqlServerGroundworkCapabilities.PhysicalNames));
        Write("postgresql", PhysicalSchemaTargetCompiler.Compile(
            manifest,
            PostgreSqlGroundworkCapabilities.Provider,
            PostgreSqlGroundworkCapabilities.PhysicalNames));
        Write("mongodb", MongoDbPhysicalStorageModel.Compile(
            manifest,
            MongoDbGroundworkCapabilities.Provider,
            PhysicalNamePolicy.Identity).Target);

        return;

        void Write(string provider, PhysicalSchemaTarget target)
        {
            var plan = PhysicalSchemaDiffPlanner.Plan(
                target,
                PhysicalSchemaHistoryState.Empty,
                EvidenceTime);
            if (!plan.IsApplicable)
            {
                throw new InvalidOperationException(string.Join(
                    "; ",
                    plan.Diagnostics.Select(diagnostic =>
                        $"{diagnostic.Code}: {diagnostic.Message}")));
            }

            var acknowledgements = plan.Operations
                .Where(operation => operation is not RecordPhysicalSchemaAppliedStateOperation)
                .Select(operation => new PhysicalSchemaOperationAcknowledgement(
                    operation.Identity,
                    operation.Fingerprint,
                    EvidenceTime))
                .ToArray();
            var applied = plan.Complete(acknowledgements, EvidenceTime);
            File.WriteAllText(
                Path.Combine(outputDirectory, $"{provider}-applied-state.json"),
                PhysicalSchemaAppliedStateSerializer.Serialize(applied));
        }
    }
}
