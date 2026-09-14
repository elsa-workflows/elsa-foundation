using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeArtifactsPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_runtime_artifacts_model_crud_query_and_concurrency() =>
        RuntimeArtifactsProviderSmoke.RunAsync(fixture, "PostgreSql", connection => new BookmarkStatePostgreSqlDbContext(
            new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeArtifactsSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_runtime_artifacts_model_crud_query_and_concurrency() =>
        RuntimeArtifactsProviderSmoke.RunAsync(fixture, "SqlServer", connection => new BookmarkStateSqlServerDbContext(
            new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeArtifactsMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_runtime_artifacts_model_crud_query_and_concurrency() =>
        RuntimeArtifactsProviderSmoke.RunAsync(fixture, "MySql", connection => new BookmarkStateMySqlDbContext(
            new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeArtifactsProviderSmoke
{
    private const string SigningKey = "ef-runtime-provider-smoke-signing-key-32-bytes";
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var connectionString = fixture.ConnectionString;
        var scope = $"provider-artifacts-{Guid.NewGuid():N}";
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = SigningKey,
            AllowEphemeralDevelopmentKey = false
        }));

        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var executable = new EfWorkflowExecutableStore(context, new FixedAccessor(scope));
            var template = new EfExecutableActivityTemplateStore(context, new FixedAccessor(scope), codec);
            var source = new EfWorkflowExecutableSourceReferenceStore(context, new FixedAccessor(scope), codec);

            await executable.SaveAsync(Executable("artifact-a"));
            Assert.NotNull(await executable.FindAsync("artifact-a"));
            await template.SaveAsync(Template("template-a", "template-hash-a"));
            Assert.NotNull(await template.FindByHashAsync("template-hash-a"));
            await source.SaveAsync(Reference("reference-a", "artifact-a", "definition-version-a"));
            Assert.NotNull(await source.FindAsync("reference-a"));

            var page = await source.ListByDefinitionVersionPageAsync(
                new WorkflowExecutableSourceReferenceDefinitionVersionPageQuery("definition-version-a", 1));
            Assert.Equal("reference-a", Assert.Single(page.Items).SourceReferenceId);
            Assert.Null(page.NextContinuationToken);

            var secondScope = scope + "-second";
            var secondSource = new EfWorkflowExecutableSourceReferenceStore(context, new FixedAccessor(secondScope), codec);
            await secondSource.SaveAsync(Reference("reference-b", "artifact-b", "definition-version-a"));
            var across = new EfWorkflowExecutableSourceReferenceStore(
                context,
                new FixedAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("provider-smoke-export"))),
                codec);
            var firstAcrossPage = await across.ListByDefinitionVersionPageAsync(new("definition-version-a", 1));
            var secondAcrossPage = await across.ListByDefinitionVersionPageAsync(new("definition-version-a", 1, firstAcrossPage.NextContinuationToken));
            Assert.Single(firstAcrossPage.Items);
            Assert.Single(secondAcrossPage.Items);
            Assert.Null(secondAcrossPage.NextContinuationToken);
            Assert.Equal(["reference-a", "reference-b"], new[] { firstAcrossPage.Items[0].SourceReferenceId, secondAcrossPage.Items[0].SourceReferenceId }.Order(StringComparer.Ordinal));

            await executable.SaveAsync(Executable("rollback-existing"));
            var coordination = await context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == "rollback-existing");
            context.WorkflowExecutableCoordinations.Remove(coordination);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await Assert.ThrowsAsync<InvalidDataException>(() => executable.SaveBatchAsync(
                [Executable("rollback-new"), Executable("rollback-existing")]).AsTask());
            Assert.Null(await executable.FindAsync("rollback-new"));
        }

        var concurrentReference = Reference("concurrent-reference", "artifact-concurrent", "definition-version-concurrent");
        await using (var leftContext = createContext(connectionString))
        await using (var rightContext = createContext(connectionString))
        {
            var outcomes = await Task.WhenAll(
                Capture(new EfWorkflowExecutableSourceReferenceStore(leftContext, new FixedAccessor(scope), codec).SaveAsync(concurrentReference)),
                Capture(new EfWorkflowExecutableSourceReferenceStore(rightContext, new FixedAccessor(scope), codec).SaveAsync(concurrentReference)));
            Assert.Single(outcomes, outcome => outcome is null);
            Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, outcome => outcome is not null));
        }
    }

    private static WorkflowExecutable Executable(string artifactId)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new WorkflowExecutable(new WorkflowExecutableIdentity(artifactId, "definition", "version", "1", $"hash-{artifactId}"), node, new Dictionary<string, WorkflowExecutableResumeTarget>(), CreatedAt, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableActivityTemplate Template(string id, string hash)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new ExecutableActivityTemplate(id, hash, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "fingerprint", new Dictionary<string, string>(), CreatedAt);
    }

    private static WorkflowExecutableSourceReference Reference(string id, string artifact, string definitionVersion) => new(
        id, artifact, "WorkflowDefinition", "definition", "1", "definition", definitionVersion, "1", CreatedAt, CreatedAt, WorkflowExecutableReferenceScope.Published);

    private static async Task<Exception?> Capture(ValueTask operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class FixedAccessor : IPersistenceAccessContextAccessor
    {
        public FixedAccessor(string scope) : this(PersistenceAccessContext.Scoped(new PersistenceScope(scope))) { }
        public FixedAccessor(PersistenceAccessContext current) => Current = current;
        public PersistenceAccessContext Current { get; }
    }
}
