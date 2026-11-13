using System.Data.Common;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Actual SQLite bounded source reads; complete source acquisition, retention and import remain separate.</summary>
public sealed class RuntimeBoundedSourceReadTests
{
    [Fact]
    public async Task Actual_SQLite_native_and_model_reads_preserve_original_owner_pins_and_scope()
    {
        await using var owner = await Owner.Create(); var native = Workflow(); await owner.Store.SaveAsync(native);
        var material = await owner.Store.FindMaterialBoundedAsync(native.Identity.ArtifactId, Owner.Partition, new());
        Assert.NotNull(material); Assert.Equal(native.Identity, material.Material.Identity); Assert.Equal(Owner.Partition, material.Scope); Assert.True(material.StoredPayloadBytes > 0);
        var model = Model(native.Identity); await ((IWorkflowExecutableStore)owner.Store).SaveModelAsync(model);
        var actual = await owner.Store.FindMaterialBoundedAsync(model.Identity.ArtifactId, Owner.Partition, new());
        Assert.Equal(model.Identity, actual!.Material.Identity); Assert.NotNull(actual.Material.Model);
    }

    [Fact]
    public async Task Actual_owner_compressed_payload_is_refused_before_expansion_while_the_ordinary_owner_still_reads_it()
    {
        await using var owner = await Owner.Create(compressed: true); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        await owner.Context.Database.OpenConnectionAsync();
        await using (var command = owner.Context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT ContentJson FROM elsa_runtime_workflow_executable";
            Assert.True(EfPayloadCodec.IsFrame((string)(await command.ExecuteScalarAsync())!));
        }
        await owner.Context.Database.CloseConnectionAsync(); owner.Spy.Commands.Clear();
        var error = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()).AsTask());
        Assert.Equal("runtime_source_payload_frame_unsupported", error.Code); Assert.True(error.Unsupported);
        Assert.All(owner.Spy.Commands.Where(sql => sql.Contains("ContentJson", StringComparison.Ordinal)),
            sql => Assert.True(sql.Contains("typeof(", StringComparison.Ordinal) || sql.Contains("CAST(", StringComparison.Ordinal)));
        Assert.Equal(workflow.Identity, (await owner.Store.FindAsync(workflow.Identity.ArtifactId))!.Identity);
    }

    [Fact]
    public async Task Oversized_stored_payload_is_refused_before_any_payload_fetch_or_owner_hydration()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        await owner.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_runtime_workflow_executable SET ContentJson={new string('x', 8192)}"); owner.Spy.Commands.Clear();
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition,
            new() { MaxPayloadBytes = 4096 }).AsTask());
        Assert.Equal("runtime_source_stored_byte_limit", exception.Code);
        Assert.DoesNotContain(owner.Spy.Commands, sql => sql.Contains("ContentJson", StringComparison.Ordinal) && !sql.Contains("typeof(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_stored_UTF8_is_refused_without_provider_string_repair()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        await owner.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_runtime_workflow_executable SET ContentJson=CAST(X'FF' AS TEXT)");
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()).AsTask());
        Assert.Equal("runtime_source_stored_content_invalid", exception.Code);
    }

    [Theory]
    [InlineData(true, "runtime_source_duplicate_json_property")]
    [InlineData(false, "runtime_source_stored_content_invalid")]
    public async Task Duplicate_and_unknown_source_wire_fields_do_not_disappear_during_hydration(bool duplicate, string code)
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        var row = await owner.Context.WorkflowExecutables.AsNoTracking().SingleAsync();
        var changed = "{\"" + (duplicate ? "identity" : "futureHiddenReference") + "\":null," + row.ContentJson[1..];
        await owner.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_runtime_workflow_executable SET ContentJson={changed}");
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()).AsTask());
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task Matching_invalid_UTF8_incarnations_are_refused_before_EF_can_repair_physical_identity()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        await owner.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_runtime_workflow_executable SET IncarnationId=CAST(X'FF' AS TEXT)");
        await owner.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_runtime_workflow_executable_coordination SET IncarnationId=CAST(X'FF' AS TEXT)");
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() =>
            owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()).AsTask());
        Assert.Equal("runtime_source_stored_content_invalid", exception.Code);
    }

    [Fact]
    public async Task Caller_transaction_is_refused_without_committing_or_disposing_it()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        await using var transaction = await owner.Context.Database.BeginTransactionAsync(); owner.Spy.Commands.Clear();
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() =>
            owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()).AsTask());
        Assert.True(exception.Unsupported); Assert.Equal("runtime_source_storage_profile_unsupported", exception.Code);
        Assert.Same(transaction, owner.Context.Database.CurrentTransaction); Assert.Empty(owner.Spy.Commands);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task An_ordinary_decorator_refuses_bounded_reads_without_forwarding_or_hydrating_payloads()
    {
        await using var owner = await Owner.Create(); owner.Spy.Commands.Clear();
        IWorkflowExecutableStore decorated = new LegacyDecorator(owner.Store);
        var exception = await Assert.ThrowsAsync<RuntimeSourceReadException>(() =>
            decorated.FindMaterialBoundedAsync("exact", Owner.Partition, new()).AsTask());
        Assert.True(exception.Unsupported); Assert.Empty(owner.Spy.Commands);
    }

    [Fact]
    public async Task Exact_selected_scope_is_checked_before_source_queries()
    {
        await using var owner = await Owner.Create(); owner.Spy.Commands.Clear();
        Assert.Equal("runtime_source_scope_mismatch", (await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindMaterialBoundedAsync("id", new("other"), new()).AsTask())).Code);
        Assert.Empty(owner.Spy.Commands);
    }

    [Fact]
    public async Task Caller_pending_changes_are_preserved_when_the_bounded_reader_refuses()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        var row = await owner.Context.WorkflowExecutables.SingleAsync(); row.ArtifactHash = "pending-change";
        await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()).AsTask());
        Assert.Equal(EntityState.Modified, owner.Context.Entry(row).State); Assert.Equal("pending-change", row.ArtifactHash);
    }

    [Fact]
    public async Task Missing_coordination_cannot_be_reported_as_absent_source_material()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        await owner.Context.WorkflowExecutableCoordinations.ExecuteDeleteAsync();
        Assert.Equal("runtime_source_material_pair_invalid", (await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()).AsTask())).Code);
    }

    [Fact]
    public async Task Complete_version_reference_snapshot_keeps_published_and_test_scope_distinct()
    {
        await using var owner = await Owner.Create(); var native = Workflow(); await owner.Store.SaveAsync(native);
        await owner.References.SaveAsync(Reference("published", native.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));
        await owner.References.SaveAsync(Reference("test", native.Identity.ArtifactId, WorkflowExecutableReferenceScope.TestRun));
        var copied = await owner.References.ListByDefinitionVersionBoundedAsync("selected-version", Owner.Partition, new());
        Assert.Equal(2, copied.Count); Assert.Contains(copied, value => value.Reference.Scope == WorkflowExecutableReferenceScope.Published);
        Assert.Contains(copied, value => value.Reference.Scope == WorkflowExecutableReferenceScope.TestRun);
        Assert.All(copied, value => { Assert.Equal(Owner.Partition, value.Scope); Assert.True(value.StoredPayloadBytes > 0); });
    }

    [Fact]
    public async Task Case_aliases_cannot_overwrite_known_owner_wire_fields()
    {
        await using var owner = await Owner.Create(); var workflow = Workflow(); await owner.Store.SaveAsync(workflow);
        var row = await owner.Context.WorkflowExecutables.AsNoTracking().SingleAsync(); var changed = "{\"Identity\":null," + row.ContentJson[1..];
        await owner.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_runtime_workflow_executable SET ContentJson={changed}");
        Assert.Equal("runtime_source_stored_content_invalid", (await Assert.ThrowsAsync<RuntimeSourceReadException>(() =>
            owner.Store.FindMaterialBoundedAsync(workflow.Identity.ArtifactId, Owner.Partition, new()).AsTask())).Code);
    }

    private static WorkflowExecutable Workflow(string id = "root", IReadOnlyCollection<WorkflowExecutableDependency>? dependencies = null)
    {
        var node = new ExecutableNode("node", "node", "fixture.Module", "1.0.0", "fixture.consumer", JsonSerializer.SerializeToElement(new { z = "a" }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var input = new WorkflowExecutableInputContract(1, []); var edges = dependencies ?? []; var hasher = new WorkflowExecutableHasher(); var hash = hasher.ComputeHash(node, input, edges);
        return new(new(id + "-" + hash[7..], "producer", "producer-version", "1.0.0", hash), node, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), input, edges, IncidentStrategyBuiltIns.FaultReference);
    }
    private static RuntimeModelMaterial Model(WorkflowExecutableIdentity child)
    {
        var provisional = new WorkflowExecutableIdentity("model", "model-producer", "model-version", "1.0.0", "initial");
        var seed = new RuntimeModelMaterial(1, provisional, new("fixture.protocol", 1), "fixture-profile", JsonSerializer.SerializeToElement(new { graph = "controlled" }), new(1, []), [], [new("worker", child)], []);
        return new(1, provisional with { ArtifactHash = seed.ComputeContentHash() }, seed.Protocol, seed.Profile, seed.Definition, seed.InputContract, [], seed.Dependencies, []);
    }
    private static WorkflowExecutableSourceReference Reference(string id, string artifact, WorkflowExecutableReferenceScope scope) =>
        new(id, artifact, "WorkflowDefinitionVersion", "selected-version", "1.0.0", "selected-definition", "selected-version", "1.0.0", DateTimeOffset.UnixEpoch,
            scope == WorkflowExecutableReferenceScope.Published ? DateTimeOffset.UnixEpoch : null, scope);
    private sealed class LegacyDecorator(IWorkflowExecutableStore inner) : WorkflowExecutableStoreDecorator(inner);
    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(Owner.Partition); }
    private sealed class Cursor : IRuntimeRecoveryContinuationCodec
    { public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload); public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token); }
    private sealed class Spy : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
    private sealed class Owner : IAsyncDisposable
    {
        public static PersistenceScope Partition { get; } = new("source-partition");
        private readonly string _path = Path.Combine(Path.GetTempPath(), "elsa-release-source-" + Guid.NewGuid().ToString("N") + ".sqlite");
        public Accessor Access { get; } = new(); public Spy Spy { get; } = new();
        public RuntimeSqliteDbContext Context { get; private set; } = null!;
        public EfWorkflowExecutableStore Store { get; private set; } = null!;
        public EfWorkflowExecutableSourceReferenceStore References { get; private set; } = null!;
        public EfExecutableActivityTemplateStore Templates { get; private set; } = null!;
        public static async Task<Owner> Create(bool compressed = false)
        {
            var owner = new Owner();
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=" + owner._path + ";Pooling=False").AddInterceptors(owner.Spy);
            if (compressed) options.UseElsaPayloadCompression(new() { Codec = EfPayloadCompression.GZip, MinimumLength = 0 });
            owner.Context = new(options.Options);
            await owner.Context.Database.EnsureCreatedAsync(); owner.Store = new(owner.Context, owner.Access); owner.References = new(owner.Context, owner.Access, new Cursor());
            owner.Templates = new(owner.Context, owner.Access, new Cursor()); return owner;
        }
        public async ValueTask DisposeAsync()
        { await Context.DisposeAsync(); SqliteConnection.ClearAllPools(); File.Delete(_path); File.Delete(_path + "-wal"); File.Delete(_path + "-shm"); }
    }
}
