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
using Elsa.Mediator;
using Elsa.Mediator.Core.Contracts;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Design;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Design.Validations;
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
    internal const string PublicationTenant = "rules-persisted-publication-test";
    internal const string ProgramLiteral = "{\"formatVersion\":1,\"languageId\":\"elsa.rules.finite-positive\",\"languageVersion\":1,\"vocabulary\":[\"derived\",\"seed\"],\"rules\":[{\"id\":\"derive\",\"premises\":[\"seed\"],\"conclusion\":\"derived\"}]}";

    internal static async Task<WorkflowExecutable> CompileAsync(
        int maxRounds = 4,
        IReadOnlyCollection<string>? initialFacts = null,
        CancellationToken cancellationToken = default)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"elsa-rules-compiled-publication-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(tempRoot, "design.db");
        var scanFolder = Path.Combine(tempRoot, "clr");

        try
        {
            CopyClrAssemblies(scanFolder);
            var services = CreateServices(databasePath, scanFolder, persistedPublication: false);
            await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = root.CreateAsyncScope();
            var provider = scope.ServiceProvider;

            await InitializeDesignAsync(provider, persistedPublication: false, cancellationToken);
            var state = await CreateAuthoredStateAsync(provider, maxRounds, initialFacts, cancellationToken);
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
            DeleteOwnedDirectory(tempRoot);
        }
    }

    internal static async Task<RulesPersistedPublicationEvidence> SubmitAndPublishAsync(
        CancellationToken cancellationToken = default)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"elsa-rules-persisted-publication-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(tempRoot, "authoring.db");
        var scanFolder = Path.Combine(tempRoot, "clr");

        try
        {
            CopyClrAssemblies(scanFolder);
            var services = CreateServices(databasePath, scanFolder, persistedPublication: true);
            await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using (var catalogScope = root.CreateAsyncScope())
                await InitializeDesignAsync(catalogScope.ServiceProvider, persistedPublication: true, cancellationToken);

            SubmittedWorkflowDefinition submitted;
            PublishedWorkflowView published;
            await using (var authoringScope = root.CreateAsyncScope())
            {
                var provider = authoringScope.ServiceProvider;
                provider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                    PersistenceAccessContext.Scoped(new PersistenceScope(PublicationTenant)));
                var state = await CreateAuthoredStateAsync(provider, 4, ["seed"], cancellationToken);
                submitted = await provider.GetRequiredService<ISubmitWorkflowDefinitionCommand>().Execute(
                    new DesignOperationKey($"rules-persisted-publication-{Guid.NewGuid():N}"),
                    "Rules publication sequence",
                    "Persisted RunRules sequence used to prove the normal publishing handler path.",
                    state,
                    cancellationToken);
                published = await provider.GetRequiredService<IRequestSender>().Send(
                    new PublishWorkflow(submitted.VersionId, TenantId: PublicationTenant),
                    cancellationToken);
            }

            // Read every write through a fresh provider scope and context under the same tenant as submission.
            await using var readScope = root.CreateAsyncScope();
            var readProvider = readScope.ServiceProvider;
            readProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.Scoped(new PersistenceScope(PublicationTenant)));
            var version = await readProvider.GetRequiredService<IWorkflowDefinitionVersionStore>()
                .GetWithDefinitionAsync(submitted.VersionId, cancellationToken);
            var executable = await readProvider.GetRequiredService<IWorkflowExecutableStore>()
                .FindAsync(published.ArtifactId, cancellationToken)
                ?? throw new InvalidOperationException("The real workflow executable store did not retain the published artifact.");
            var sourceReference = await readProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
                .FindAsync(published.SourceReferenceId, cancellationToken)
                ?? throw new InvalidOperationException("The real workflow source-reference store did not retain the publication reference.");
            var publication = await readProvider.GetRequiredService<IPublicationRecordStore>()
                .FindAsync(published.PublicationId, cancellationToken)
                ?? throw new InvalidOperationException("The real publication ledger did not retain the publication record.");
            return new RulesPersistedPublicationEvidence(submitted, version, published, executable, sourceReference, publication);
        }
        finally
        {
            DeleteOwnedDirectory(tempRoot);
        }
    }

    private static void DeleteOwnedDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup after disposal; never clear process-wide pools shared by other tests.
        }
        catch (UnauthorizedAccessException)
        {
            // The uniquely owned directory can be removed later without affecting another fixture.
        }
    }

    private static ServiceCollection CreateServices(string databasePath, string scanFolder, bool persistedPublication)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
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
            ConnectionString = connectionString
        }.ConfigureServices(services);
        new WorkflowsDesignEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = connectionString
        }.ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);
        var clrFeature = new ClrActivityReconciliationFeature();
        clrFeature.Options.FolderPath = scanFolder;
        clrFeature.Options.SourceId = "rules-compiled-publication-test";
        clrFeature.ConfigureServices(services);
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new RulesRuntimeFeature().ConfigureServices(services);
        new RulesWorkflowsDesignFeature().ConfigureServices(services);

        if (persistedPublication)
        {
            new MediatorFeature().ConfigureServices(services);
            new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
            new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services);
            new WorkflowDesignValidationsFeature().ConfigureServices(services);
            new RuntimeEntityFrameworkCoreFeature
            {
                Provider = "Sqlite",
                ConnectionString = connectionString,
                CacheWorkflowExecutables = false,
                RecoveryContinuationSigningKey = "rules-publication-recovery-key-32-bytes",
                HierarchyCursorSigningKey = "rules-publication-hierarchy-key-32-bytes"
            }.ConfigureServices(services);
            new PublishingEntityFrameworkCoreFeature
            {
                Provider = "Sqlite",
                ConnectionString = connectionString
            }.ConfigureServices(services);
        }

        return services;
    }

    private static void CopyClrAssemblies(string scanFolder)
    {
        Directory.CreateDirectory(scanFolder);
        foreach (var assembly in new[] { typeof(RunRules).Assembly, typeof(Sequence).Assembly, typeof(WriteLine).Assembly })
            File.Copy(assembly.Location, Path.Combine(scanFolder, Path.GetFileName(assembly.Location)));
    }

    private static async Task InitializeDesignAsync(
        IServiceProvider provider,
        bool persistedPublication,
        CancellationToken cancellationToken)
    {
        // Reconciled CLR modules own global catalog rows. Bind global access so the real EF tenant guard compares
        // those rows to global access instead of the generic runtime's "default" scope.
        provider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Global);

        await provider.GetRequiredService<ActivitiesDesignDbContext>().Database.MigrateAsync(cancellationToken);
        await provider.GetRequiredService<WorkflowsDesignDbContext>().Database.MigrateAsync(cancellationToken);
        if (persistedPublication)
        {
            await provider.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(cancellationToken);
            await provider.GetRequiredService<PublishingSnapshotReviewDbContext>().Database.MigrateAsync(cancellationToken);
        }

        // Use the runtime's CLR registration task and real reconciliation pipeline, which persist the catalog versions
        // consumed by both compilation and the ordinary submit/publish workflow.
        var registerTypes = provider.GetServices<IStartupTask>()
            .OfType<RegisterActivityTypesStartupTask>()
            .Single();
        await registerTypes.ExecuteAsync(cancellationToken);
        await provider.GetRequiredService<IActivityVersionReconciler>().Reconcile(cancellationToken);
    }

    private static async Task<WorkflowDefinitionState> CreateAuthoredStateAsync(
        IServiceProvider provider,
        int maxRounds,
        IReadOnlyCollection<string>? initialFacts,
        CancellationToken cancellationToken)
    {
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
                new ArgumentState(nameof(RunRules.InitialFacts), new ArgumentValue(initialFacts?.ToArray() ?? ["seed"], "Literal"), null, null, null, null),
                new ArgumentState(nameof(RunRules.MaxRounds), new ArgumentValue(maxRounds, "Literal"), null, null, null, null)
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
        return new WorkflowDefinitionState([], sequence, [], [], null);
    }

    internal sealed record RulesPersistedPublicationEvidence(
        SubmittedWorkflowDefinition Submitted,
        WorkflowDefinitionVersion Version,
        PublishedWorkflowView Published,
        WorkflowExecutable Executable,
        WorkflowExecutableSourceReference SourceReference,
        PublicationRecord Publication);

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
