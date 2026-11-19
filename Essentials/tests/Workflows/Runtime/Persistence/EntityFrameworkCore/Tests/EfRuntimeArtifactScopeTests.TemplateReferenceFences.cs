using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Theory]
    [InlineData("wrapper")] [InlineData("node")] [InlineData("nested")]
    public async Task A_new_workflow_fences_its_present_template_in_the_same_commit(string route)
    {
        await using var database = await Database.CreateAsync();
        await using var writer = database.Open("tenant-a");
        var template = Template("template", "template-hash"); await writer.Template.SaveAsync(template);
        var before = await writer.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        await writer.Executable.SaveAsync(TemplateWorkflow(template, route));
        await using var fresh = database.Open("tenant-a");
        var after = await fresh.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(before.Revision + 1, after.Revision); Assert.Equal(before.IncarnationId, after.IncarnationId);
        Assert.Equal(before.ContentJson, after.ContentJson); Assert.NotNull(await fresh.Executable.FindAsync("workflow"));
    }

    [Theory]
    [InlineData("closed")] [InlineData("direct")] [InlineData("metadata")] [InlineData("nested")] [InlineData("shared")]
    public async Task A_new_parent_template_fences_present_dependency_material_once_per_commit(string route)
    {
        await using var database = await Database.CreateAsync();
        await using var writer = database.Open("tenant-a");
        var leaf = Template("leaf", "leaf-hash"); await writer.Template.SaveAsync(leaf);
        var before = await writer.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        await writer.Template.SaveAsync(TemplateParent(leaf, route));
        await using var fresh = database.Open("tenant-a");
        var after = await fresh.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync(row => row.Id == before.Id);
        Assert.Equal(before.Revision + 1, after.Revision); Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(before.IncarnationId, after.IncarnationId); Assert.NotNull(await fresh.Template.FindAsync("parent"));
    }

    [Fact]
    public async Task Template_reference_creation_fences_only_its_actual_persistence_scope()
    {
        await using var database = await Database.CreateAsync();
        await using var first = database.Open("tenant-a"); await using var other = database.Open("tenant-b");
        var template = Template("template", "template-hash"); await first.Template.SaveAsync(template); await other.Template.SaveAsync(template);
        await first.Store.SaveAsync(Reference("source", template.TemplateId));
        await using var fresh = database.Open("tenant-a");
        var rows = await fresh.Context.ExecutableActivityTemplates.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, rows.Single(row => row.ScopeKey == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("tenant-a")).Revision);
        Assert.Equal(1, rows.Single(row => row.ScopeKey == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("tenant-b")).Revision);
        Assert.NotNull(await fresh.Store.FindAsync("source")); Assert.Null(await other.Store.FindAsync("source"));
    }

    [Fact]
    public async Task Publication_adoption_fences_actual_material_and_source_without_rewriting_the_winning_payload()
    {
        await using var database = await Database.CreateAsync();
        await using var writer = database.Open("tenant-a");
        var template = Template("template", "template-hash"); var reference = Reference("source", template.TemplateId);
        var commit = new EfActivityPublicationRuntimeCommit(writer.Template, writer.Store);
        Assert.True(await commit.CommitAsync(template, reference));
        var material = await writer.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        var source = await writer.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.False(await commit.CommitAsync(template, reference with { CreatedAt = reference.CreatedAt.AddMinutes(1), PublishedAt = reference.PublishedAt?.AddMinutes(1) }));
        await using var fresh = database.Open("tenant-a");
        var afterMaterial = await fresh.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        var afterSource = await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(material.Revision + 1, afterMaterial.Revision); Assert.Equal(source.Revision + 1, afterSource.Revision);
        Assert.Equal(material.ContentJson, afterMaterial.ContentJson); Assert.Equal(source.ContentJson, afterSource.ContentJson);
        Assert.Equal(material.IncarnationId, afterMaterial.IncarnationId); Assert.Equal(source.IncarnationId, afterSource.IncarnationId);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(reference, (await fresh.Store.FindAsync("source"))!));
    }

    [Fact]
    public async Task Adoption_refuses_a_source_revision_advanced_by_actual_SQL_before_its_save_and_rolls_back_the_unit()
    {
        await using var database = await Database.CreateAsync();
        var template = Template("template", "template-hash"); var reference = Reference("source", template.TemplateId);
        long materialRevision; WorkflowExecutableSourceReferenceEntity original;
        await using (var seed = database.Open("tenant-a"))
        {
            await new EfActivityPublicationRuntimeCommit(seed.Template, seed.Store).CommitAsync(template, reference);
            materialRevision = (await seed.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).Revision;
            original = await seed.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        }
        var advance = new AdvanceAdoptedSourceRevision(original.Id);
        await using (var failing = database.Open("tenant-a", advance))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
                new EfActivityPublicationRuntimeCommit(failing.Template, failing.Store).CommitAsync(template, reference).AsTask());
            Assert.IsType<DbUpdateConcurrencyException>(EfRelationalExceptionClassifier.FindSaveFailure(failure));
        }
        Assert.Equal(1, advance.Changed);
        await using var fresh = database.Open("tenant-a");
        Assert.Equal(materialRevision, (await fresh.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).Revision);
        var after = await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(original.Revision, after.Revision); Assert.Equal(original.ContentJson, after.ContentJson);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(reference, (await fresh.Store.FindAsync("source"))!));
        Assert.False(await new EfActivityPublicationRuntimeCommit(fresh.Template, fresh.Store).CommitAsync(template, reference));
    }

    private sealed class AdvanceAdoptedSourceRevision(string id) : SaveChangesInterceptor
    {
        public int Changed { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var owner = (RuntimeDbContext)eventData.Context!;
            Changed = await owner.WorkflowExecutableSourceReferences.Where(row => row.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            return result;
        }
    }

    [Theory]
    [InlineData("workflow")] [InlineData("parent")] [InlineData("source")] [InlineData("publication")]
    public async Task Material_fence_and_referencer_roll_back_together_after_actual_SQL_before_commit(string kind)
    {
        await using var database = await Database.CreateAsync();
        var leaf = Template("leaf", "leaf-hash");
        await using (var seed = database.Open("tenant-a")) await seed.Template.SaveAsync(leaf);
        var fault = new AfterTemplateFenceSave();
        await using (var failing = database.Open("tenant-a", fault))
        {
            var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => WriteTemplateReferencer(failing, leaf, kind));
            Assert.Equal(kind == "publication" ? "publishing" : "saving", failure.Operation);
            var cause = failure.InnerException;
            while (cause is RuntimeArtifactEntityFrameworkPersistenceException) cause = cause.InnerException;
            Assert.NotNull(fault.Failure); Assert.Same(fault.Failure, cause);
        }
        Assert.NotNull(fault.PhysicalRevision); Assert.True(fault.PhysicalRevision > 1);
        await using var fresh = database.Open("tenant-a");
        var material = await fresh.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(1, material.Revision); Assert.Equal(leaf.TemplateId, (await fresh.Template.FindAsync("leaf"))!.TemplateId);
        Assert.Empty(await fresh.Context.WorkflowExecutables.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        Assert.Null(await fresh.Template.FindAsync("parent"));
        await WriteTemplateReferencer(fresh, leaf, kind);
        Assert.True((await fresh.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync(row => row.Id == material.Id)).Revision > 1);
    }

    [Theory]
    [InlineData("workflow")] [InlineData("parent")] [InlineData("source")] [InlineData("publication")]
    public async Task A_committed_referencer_invalidates_an_older_real_template_delete_CAS(string kind)
    {
        await using var database = await Database.CreateAsync();
        var leaf = Template("leaf", "leaf-hash");
        await using (var seed = database.Open("tenant-a")) await seed.Template.SaveAsync(leaf);
        await using var stale = database.Open("tenant-a");
        var candidate = await stale.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        var claim = await stale.Context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync();
        await using (var writer = database.Open("tenant-a")) await WriteTemplateReferencer(writer, leaf, kind);
        await using (var transaction = await stale.Context.Database.BeginTransactionAsync())
        {
            stale.Context.Remove(candidate); stale.Context.Remove(claim);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        await using var fresh = database.Open("tenant-a");
        Assert.NotNull(await fresh.Template.FindAsync(leaf.TemplateId));
        Assert.Single(await fresh.Context.ExecutableActivityTemplateHashClaims.AsNoTracking().Where(row => row.TemplateHash == leaf.TemplateHash).ToArrayAsync());
    }

    [Theory]
    [InlineData("schema", false)] [InlineData("schema", true)]
    [InlineData("claim-missing", false)] [InlineData("claim-missing", true)]
    [InlineData("claim-owner", false)] [InlineData("claim-owner", true)]
    [InlineData("claim-incarnation", false)] [InlineData("claim-incarnation", true)]
    [InlineData("id-projection", false)] [InlineData("id-projection", true)]
    [InlineData("hash-projection", false)] [InlineData("hash-projection", true)]
    [InlineData("scope-projection", false)] [InlineData("scope-projection", true)]
    [InlineData("scope-hash", false)] [InlineData("scope-hash", true)]
    [InlineData("claim-scope", false)] [InlineData("claim-scope", true)]
    [InlineData("orphan-hash-projection", false)] [InlineData("orphan-hash-projection", true)]
    public async Task Physical_template_corruption_refuses_new_referencers_before_any_write(string corruption, bool source)
    {
        await using var database = await Database.CreateAsync();
        await using var writer = database.Open("tenant-a"); var template = Template("template", "template-hash"); await writer.Template.SaveAsync(template);
        var row = await writer.Context.ExecutableActivityTemplates.SingleAsync();
        var claim = await writer.Context.ExecutableActivityTemplateHashClaims.SingleAsync();
        if (corruption == "schema") row.SchemaVersion = "999.0.0";
        else if (corruption == "claim-missing") writer.Context.Remove(claim);
        else if (corruption == "claim-owner") claim.TemplateId = EfRelationalIdentity.Encode("foreign");
        else if (corruption == "id-projection") row.TemplateIdHash = "foreign";
        else if (corruption == "hash-projection") row.TemplateHashHash = "foreign";
        else if (corruption == "scope-projection") row.ScopeKey = EfRelationalIdentity.Encode("foreign");
        else if (corruption == "scope-hash") row.ScopeKeyHash = "foreign";
        else if (corruption == "claim-scope") claim.ScopeKey = EfRelationalIdentity.Encode("foreign");
        else if (corruption == "orphan-hash-projection") { row.TemplateHashHash = "foreign"; writer.Context.Remove(claim); }
        else claim.IncarnationId = "foreign";
        await writer.Context.SaveChangesAsync(); writer.Context.ChangeTracker.Clear();
        Task Write() => source ? writer.Store.SaveAsync(Reference("source", template.TemplateId)).AsTask() : writer.Executable.SaveAsync(TemplateWorkflow(template, "wrapper")).AsTask();
        if (corruption == "schema") await Assert.ThrowsAsync<EfSchemaVersionSkewException>(Write);
        else await Assert.ThrowsAsync<InvalidDataException>(Write);
        await using var fresh = database.Open("tenant-a");
        Assert.Empty(await fresh.Context.WorkflowExecutables.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        Assert.Equal(1, (await fresh.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).Revision);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Publication_refuses_shared_context_but_different_material_and_reference_scope_before_effects(bool reverse)
    {
        await using var database = await Database.CreateAsync(); await using var shared = database.Open("tenant-a");
        var codec = FenceCodec();
        var templates = new EfExecutableActivityTemplateStore(shared.Context, new Accessor(PersistenceAccessContext.Scoped(new(reverse ? "tenant-b" : "tenant-a"))), codec);
        var sources = new EfWorkflowExecutableSourceReferenceStore(shared.Context, new Accessor(PersistenceAccessContext.Scoped(new(reverse ? "tenant-a" : "tenant-b"))), codec);
        var template = Template("template", "template-hash");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfActivityPublicationRuntimeCommit(templates, sources)
            .CommitAsync(template, Reference("source", template.TemplateId)).AsTask());
        await using var fresh = database.Open("tenant-a");
        Assert.Empty(await fresh.Context.ExecutableActivityTemplates.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fresh.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fresh.Context.ExecutableActivityTemplateHashClaims.AsNoTracking().ToArrayAsync());
        Assert.True(await new EfActivityPublicationRuntimeCommit(fresh.Template, fresh.Store).CommitAsync(template, Reference("source", template.TemplateId)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Publication_captures_each_operation_scope_before_awaited_storage_even_if_original_accessors_move(bool sourceOnly)
    {
        await using var database = await Database.CreateAsync();
        var materialAccess = new MovableFenceAccess(); var sourceAccess = new MovableFenceAccess();
        var pivot = new FenceScopePivot(() =>
        {
            sourceAccess.Current = PersistenceAccessContext.Scoped(new("tenant-b"));
            if (!sourceOnly) materialAccess.Current = sourceAccess.Current;
        });
        await using var shared = database.Open("tenant-a", pivot);
        var codec = FenceCodec();
        var templates = new EfExecutableActivityTemplateStore(shared.Context, materialAccess, codec);
        var sources = new EfWorkflowExecutableSourceReferenceStore(shared.Context, sourceAccess, codec);
        var commit = new EfActivityPublicationRuntimeCommit(templates, sources);
        var template = Template("template", "template-hash");
        Assert.True(await commit.CommitAsync(template, Reference("source", template.TemplateId)));
        Assert.True(pivot.Ran);
        await using var first = database.Open("tenant-a"); await using var other = database.Open("tenant-b");
        Assert.NotNull(await first.Template.FindAsync(template.TemplateId)); Assert.NotNull(await first.Store.FindAsync("source"));
        Assert.Null(await other.Template.FindAsync(template.TemplateId)); Assert.Null(await other.Store.FindAsync("source"));
        // Capture again for a new unit of work: this is not a service-lifetime scope cache.
        materialAccess.Current = PersistenceAccessContext.Scoped(new("tenant-b"));
        Assert.True(await commit.CommitAsync(template, Reference("source-next", template.TemplateId)));
        Assert.NotNull(await other.Template.FindAsync(template.TemplateId)); Assert.NotNull(await other.Store.FindAsync("source-next"));
        Assert.Null(await first.Store.FindAsync("source-next"));
    }

    private static IRuntimeRecoveryContinuationCodec FenceCodec() => new HmacRuntimeRecoveryContinuationCodec(
        Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('t', 32), AllowEphemeralDevelopmentKey = false }));

    private sealed class MovableFenceAccess : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("tenant-a"));
    }

    private sealed class FenceScopePivot(Action pivot) : DbTransactionInterceptor
    {
        public bool Ran { get; private set; }
        public override ValueTask<InterceptionResult<System.Data.Common.DbTransaction>> TransactionStartingAsync(
            System.Data.Common.DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<System.Data.Common.DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (!Ran) { Ran = true; pivot(); }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Publication_refuses_present_exact_dependency_with_a_different_hash_without_changing_winning_material()
    {
        await using var database = await Database.CreateAsync(); await using var writer = database.Open("tenant-a");
        var leaf = Template("leaf", "leaf-hash"); await writer.Template.SaveAsync(leaf);
        var valid = TemplateParent(leaf, "closed");
        var wrong = new ExecutableActivityTemplate(valid.TemplateId, valid.TemplateHash, valid.Root, valid.ResumeTargets,
            valid.DirectDependencies, [new(leaf.TemplateId, "wrong-hash")], valid.RuntimeRequirements, valid.ProviderFingerprint,
            valid.CompatibilityMetadata, valid.CreatedAt);
        await Assert.ThrowsAsync<InvalidDataException>(() => new EfActivityPublicationRuntimeCommit(writer.Template, writer.Store)
            .CommitAsync(wrong, Reference("source", wrong.TemplateId)).AsTask());
        Assert.Null(await writer.Template.FindAsync(wrong.TemplateId)); Assert.Null(await writer.Store.FindAsync("source"));
        var physical = await writer.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(1, physical.Revision);
        Assert.True(await new EfActivityPublicationRuntimeCommit(writer.Template, writer.Store).CommitAsync(valid, Reference("source", valid.TemplateId)));
    }

    [Fact]
    public async Task Raw_parent_first_staging_and_inlined_imports_remain_available_without_installing_templates()
    {
        await using var database = await Database.CreateAsync(); await using var writer = database.Open("tenant-a");
        var missing = Template("missing", "missing-hash");
        await writer.Template.SaveAsync(TemplateParent(missing, "closed"));
        await writer.Executable.SaveAsync(TemplateWorkflow(missing, "nested"));
        Assert.NotNull(await writer.Template.FindAsync("parent")); Assert.NotNull(await writer.Executable.FindAsync("workflow"));
        Assert.Null(await writer.Template.FindAsync("missing"));
        // Root publication is stronger than inert raw staging and must refuse this unresolved exact dependency.
        await Assert.ThrowsAsync<InvalidDataException>(() => new EfActivityPublicationRuntimeCommit(writer.Template, writer.Store)
            .CommitAsync(TemplateParent(missing, "closed"), Reference("source", "parent")).AsTask());
        Assert.Null(await writer.Store.FindAsync("source"));
    }

    private static async Task WriteTemplateReferencer(Fixture writer, ExecutableActivityTemplate leaf, string kind)
    {
        switch (kind)
        {
            case "workflow": await writer.Executable.SaveAsync(TemplateWorkflow(leaf, "nested")); break;
            case "parent": await writer.Template.SaveAsync(TemplateParent(leaf, "shared")); break;
            case "source": await writer.Store.SaveAsync(Reference("source", leaf.TemplateId)); break;
            default: await new EfActivityPublicationRuntimeCommit(writer.Template, writer.Store)
                .CommitAsync(leaf, Reference("source", leaf.TemplateId)); break;
        }
    }

    private sealed class AfterTemplateFenceSave : SaveChangesInterceptor
    {
        public long? PhysicalRevision { get; private set; }
        public SqliteException? Failure { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var owner = (RuntimeDbContext)eventData.Context!;
            PhysicalRevision = await owner.ExecutableActivityTemplates.AsNoTracking().Where(row => row.TemplateHash == "leaf-hash")
                .Select(row => row.Revision).SingleAsync(cancellationToken);
            try
            {
                await owner.Database.ExecuteSqlRawAsync("SELECT * FROM elsa_test_missing_template_fence_failure", cancellationToken);
                throw new InvalidOperationException("The provider failure control did not fail.");
            }
            catch (SqliteException failure) { Failure = failure; throw; }
        }
    }

    private static WorkflowExecutable TemplateWorkflow(ExecutableActivityTemplate template, string route)
    {
        var basis = Executable("workflow"); var metadata = new Dictionary<string, string> { ["activity.templateHash"] = template.TemplateHash };
        ExecutableNode Node(string id, IReadOnlyDictionary<string, string>? values = null, params ExecutableNode[] children) => new(id, id,
            basis.RootActivity.ActivityType, basis.RootActivity.ActivityTypeVersion, basis.RootActivity.Descriptor,
            new Dictionary<string, RuntimeInputBinding>(), values ?? new Dictionary<string, string>(), children.Length == 0 ? [] : [new ExecutableChildSlot("body", children)]);
        var root = route == "nested" ? Node("root", children: [Node("boundary", metadata)]) : Node("root", route == "node" ? metadata : null);
        return new(basis.Identity, root, basis.ResumeTargets, basis.CreatedAt, route == "wrapper" ? metadata : new Dictionary<string, string>(), basis.IncidentStrategy);
    }

    private static ExecutableActivityTemplate TemplateParent(ExecutableActivityTemplate leaf, string route)
    {
        var metadata = new Dictionary<string, string> { ["activity.templateHash"] = leaf.TemplateHash };
        var root = route == "nested" ? new ExecutableNode("root", "root", leaf.Root.ActivityType, leaf.Root.ActivityTypeVersion, leaf.Root.Descriptor,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), [new ExecutableChildSlot("body", [new ExecutableNode("boundary", "boundary",
                leaf.Root.ActivityType, leaf.Root.ActivityTypeVersion, leaf.Root.Descriptor, new Dictionary<string, RuntimeInputBinding>(), metadata)])]) : leaf.Root;
        return new("parent", "parent-hash", root, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            route is "direct" or "shared" ? [new("definition", "version", "1", leaf.TemplateId, leaf.TemplateHash, "occurrence", ActivityInvocationOrigin.Empty)] : [],
            route is "closed" or "shared" ? [new(leaf.TemplateId, leaf.TemplateHash)] : [], [], "test/1",
            route is "metadata" or "shared" ? metadata : new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
    }
}
