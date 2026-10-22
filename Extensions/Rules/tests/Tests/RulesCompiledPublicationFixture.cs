using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Activities;
using Elsa.Activities.Sequence.Models;
using Elsa.Events;
using Elsa.Primitives.Hosting;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Design;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Tests;

/// <summary>
/// Builds the same Sequence/RunRules activity rows the normal CLR design reconciler produces, then invokes the
/// registered workflow executable compiler. The test supplies a semantic identity because a source-built test
/// host has no deployment receipt; this is compilation/composition evidence, not package-identity evidence.
/// </summary>
internal static class RulesCompiledPublicationFixture
{
    internal const string SequenceNodeId = "sequence-root";
    internal const string RulesNodeId = "rules";
    internal const string ProgramLiteral = "{\"formatVersion\":1,\"languageId\":\"elsa.rules.finite-positive\",\"languageVersion\":1,\"vocabulary\":[\"derived\",\"seed\"],\"rules\":[{\"id\":\"derive\",\"premises\":[\"seed\"],\"conclusion\":\"derived\"}]}";

    internal static async Task<WorkflowExecutable> CompileAsync(CancellationToken cancellationToken = default)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"elsa-rules-compiled-publication-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(tempRoot, "design.db");
        var scanFolder = Path.Combine(tempRoot, "clr");

        try
        {
            Directory.CreateDirectory(scanFolder);
            File.Copy(typeof(RunRules).Assembly.Location, Path.Combine(scanFolder, Path.GetFileName(typeof(RunRules).Assembly.Location)));
            File.Copy(typeof(Sequence).Assembly.Location, Path.Combine(scanFolder, Path.GetFileName(typeof(Sequence).Assembly.Location)));
            File.Copy(typeof(WriteLine).Assembly.Location, Path.Combine(scanFolder, Path.GetFileName(typeof(WriteLine).Assembly.Location)));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IRuleImplementationIdentitySource>(
                new TestImplementationIdentitySource(RuleTestData.Invocation().ExpectedImplementation));

            new PrimitivesFeature().ConfigureServices(services);
            new EventsFeature().ConfigureServices(services);
            new SerializationFeature().ConfigureServices(services);
            new ExpressionsFeature().ConfigureServices(services);
            new ActivitiesRuntimeFeature().ConfigureServices(services);
            new ActivitiesPrimitivesFeature().ConfigureServices(services);
            new ActivitiesSequenceFeature().ConfigureServices(services);
            services.AddWorkflowRuntime();
            new ActivitiesDesignEntityFrameworkCoreFeature
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath};Pooling=False"
            }.ConfigureServices(services);
            new WorkflowsDesignEntityFrameworkCoreFeature
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath};Pooling=False"
            }.ConfigureServices(services);
            new ActivitiesDesignReconciliationFeature().ConfigureServices(services);
            var clrFeature = new ClrActivityReconciliationFeature();
            clrFeature.Options.FolderPath = scanFolder;
            clrFeature.Options.SourceId = "rules-compiled-publication-test";
            clrFeature.ConfigureServices(services);
            new WorkflowsPublishingFeature().ConfigureServices(services);
            new RulesRuntimeFeature().ConfigureServices(services);
            new RulesWorkflowsDesignFeature().ConfigureServices(services);

            await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = root.CreateAsyncScope();
            var provider = scope.ServiceProvider;

            // Reconciled CLR modules own global catalog rows. The generic runtime default scope is "default";
            // explicitly bind global access so the real EF tenant guard compares global rows to global access.
            provider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Global);

            await provider.GetRequiredService<ActivitiesDesignDbContext>().Database.MigrateAsync(cancellationToken);
            await provider.GetRequiredService<WorkflowsDesignDbContext>().Database.MigrateAsync(cancellationToken);

            // Use the runtime's real CLR type-registration startup task so the compiler can resolve the scanner's
            // canonical type aliases and build typed contracts for Sequence and RunRules.
            var registerTypes = provider.GetServices<IStartupTask>()
                .OfType<RegisterActivityTypesStartupTask>()
                .Single();
            await registerTypes.ExecuteAsync(cancellationToken);

            // This is the production reconciliation pipeline: its registered CLR source scans the two real activity
            // assemblies and the EF commands persist the resulting activity definitions and versions.
            await provider.GetRequiredService<IActivityVersionReconciler>().Reconcile(cancellationToken);

            var definitions = provider.GetRequiredService<IActivityDefinitionStore>();
            var versions = provider.GetRequiredService<IActivityDefinitionVersionStore>();
            var rulesVersion = await GetClrVersionAsync(definitions, versions, RunRules.ActivityType, cancellationToken);
            var sequenceVersion = await GetClrVersionAsync(definitions, versions, typeof(Sequence).FullName!, cancellationToken);
            var writeLineVersion = await GetClrVersionAsync(definitions, versions, typeof(WriteLine).FullName!, cancellationToken);

            var rules = new ActivityNode(
                RulesNodeId,
                rulesVersion.Id,
                [
                    new ArgumentState(nameof(RunRules.Program), new ArgumentValue(ProgramLiteral, "Literal"), null, null, null, null),
                    new ArgumentState(nameof(RunRules.InitialFacts), new ArgumentValue(new[] { "seed" }, "Literal"), null, null, null, null),
                    new ArgumentState(nameof(RunRules.MaxRounds), new ArgumentValue(4, "Literal"), null, null, null, null)
                ],
                []);
            var afterRules = new ActivityNode(
                "after-rules",
                writeLineVersion.Id,
                [new ArgumentState("text", new ArgumentValue("after-rules", "Literal"), null, null, null, null)],
                []);
            var sequence = new ActivityNode(
                SequenceNodeId,
                sequenceVersion.Id,
                [],
                [],
                new ActivityNodeStructure(
                    Sequence.StructureKind,
                    Sequence.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new SequenceAuthoredStructure([rules, afterRules]))));
            var state = new WorkflowDefinitionState([], sequence, [], [], null);
            var source = new WorkflowExecutableCompileSource(
                DefinitionId: "rules-compiled-definition",
                DefinitionVersionId: "rules-compiled-version",
                ArtifactVersion: "1.0.0",
                State: state,
                SourceKind: "WorkflowDefinitionVersion",
                SourceId: "rules-compiled-version",
                SourceVersion: "1.0.0");
            var request = new WorkflowExecutableCompileRequest(
                VersionId: source.DefinitionVersionId,
                Scope: WorkflowExecutableReferenceScope.Published,
                CreatedAt: DateTimeOffset.UnixEpoch,
                PublishedAt: DateTimeOffset.UnixEpoch,
                ExpiresAt: null,
                ArtifactIdPrefix: "rules-compiled-")
            {
                Source = source
            };

            return await provider.GetRequiredService<IWorkflowExecutableCompiler>().CompileAsync(request, cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for SQLite handles held by a failed assertion or migration.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only; a lingering file does not affect the following isolated fixture.
            }
        }
    }

    private static async Task<ActivityDefinitionVersion> GetClrVersionAsync(
        IActivityDefinitionStore definitions,
        IActivityDefinitionVersionStore versions,
        string activityTypeKey,
        CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdOrActivityTypeKeyAsync(activityTypeKey, activityTypeKey, cancellationToken)
            ?? throw new InvalidOperationException($"The CLR catalog did not reconcile activity '{activityTypeKey}'.");
        var version = (await versions.ListByDefinitionAsync(definition.Id, cancellationToken)).Single();
        if (version.SourceKind != "CLR" || version.SourceId != "rules-compiled-publication-test")
            throw new InvalidOperationException($"Activity '{activityTypeKey}' did not come from the configured CLR reconciliation source.");
        return version;
    }

    private sealed class TestImplementationIdentitySource(RuleSemanticPin identity) : IRuleImplementationIdentitySource
    {
        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<RuleSemanticPin?>(identity);
        }
    }
}
