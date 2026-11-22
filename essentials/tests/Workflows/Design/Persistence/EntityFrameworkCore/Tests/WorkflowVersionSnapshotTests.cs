using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Services;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class WorkflowVersionSnapshotTests
{
    [Fact]
    public async Task Workflow_snapshot_retains_original_StateSource_without_hydrating_unknown_authoring_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), "elsa-workflow-source-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var services = new ServiceCollection(); services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor());
            services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = "Data Source=" + path + ";Pooling=False" });
            using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await context.Database.EnsureCreatedAsync();
            const string source = " { \"rootActivity\":null, \"futureOwnerField\": { \"λ\" : [ 1, 2 ] } } ";
            context.Definitions.Add(new() { Id = "definition", Name = "Original", TenantId = "partition" });
            context.Versions.Add(new("definition", "1.0.0", source) { Id = "version", TenantId = "partition" }); await context.SaveChangesAsync();
            var reader = scope.ServiceProvider.GetRequiredService<IWorkflowVersionSnapshotReader>();
            Assert.Equal(source, (await reader.FindAsync("version", "partition", new()))!.StateSource);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_workflow_definition_versions SET StateSource={new string('x', 8192)}");
            Assert.Equal("workflow_version_snapshot_byte_limit", (await Assert.ThrowsAsync<WorkflowVersionSnapshotException>(() =>
                reader.FindAsync("version", "partition", new() { MaxSourceBytes = 4096 }).AsTask())).Code);
        }
        finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }

    [Fact]
    public async Task Workflow_snapshot_refuses_an_actual_payload_frame_instead_of_exporting_encoded_storage_as_authored_source()
    {
        var path = Path.Combine(Path.GetTempPath(), "elsa-framed-workflow-source-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var services = new ServiceCollection(); services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor());
            services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = "Data Source=" + path + ";Pooling=False" });
            using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await context.Database.EnsureCreatedAsync();
            var source = "{\"futureOpaqueOwnerField\":\"" + new string('x', 8192) + "\"}";
            context.Definitions.Add(new() { Id = "definition", Name = "Original", TenantId = "partition" });
            context.Versions.Add(new("definition", "1.0.0", source) { Id = "version", TenantId = "partition" }); await context.SaveChangesAsync();
            var framed = EfPayloadCodec.Encode(source, EfPayloadCompression.GZip, 0)!; Assert.True(EfPayloadCodec.IsFrame(framed));
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_workflow_definition_versions SET StateSource={framed}");
            var reader = scope.ServiceProvider.GetRequiredService<IWorkflowVersionSnapshotReader>();
            var error = await Assert.ThrowsAsync<WorkflowVersionSnapshotException>(() => reader.FindAsync("version", "partition", new()).AsTask());
            Assert.Equal("workflow_version_snapshot_payload_frame_unsupported", error.Code); Assert.True(error.Unsupported);
        }
        finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }

    [Theory]
    [InlineData("invalid-utf8", "workflow_version_snapshot_invalid", false)]
    [InlineData("oversized-invalid-utf8", "workflow_version_snapshot_byte_limit", false)]
    [InlineData("blob", "workflow_version_snapshot_byte_limit", false)]
    [InlineData("schema", "workflow_version_snapshot_schema_unsupported", true)]
    public async Task Actual_raw_type_UTF8_and_schema_are_qualified_before_returning_source(string change, string code, bool unsupported)
    {
        var path = Path.Combine(Path.GetTempPath(), "elsa-workflow-snapshot-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var services = new ServiceCollection(); services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor());
            services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = "Data Source=" + path + ";Pooling=False" });
            using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await context.Database.EnsureCreatedAsync();
            context.Definitions.Add(new() { Id = "definition", Name = "Source", TenantId = "partition" });
            context.Versions.Add(new("definition", "1.0.0", "{}") { Id = "version", TenantId = "partition" }); await context.SaveChangesAsync();
            var sql = change switch
            {
                "invalid-utf8" => "UPDATE elsa_workflow_definition_versions SET StateSource=CAST(X'FF' AS TEXT)",
                "oversized-invalid-utf8" => "UPDATE elsa_workflow_definition_versions SET StateSource=CAST(X'FF'||zeroblob(8192) AS TEXT)",
                "blob" => "UPDATE elsa_workflow_definition_versions SET StateSource=X'7B7D'",
                _ => "UPDATE elsa_workflow_definition_versions SET SchemaVersion='future'"
            };
            await context.Database.ExecuteSqlRawAsync(sql);
            var error = await Assert.ThrowsAsync<WorkflowVersionSnapshotException>(() => scope.ServiceProvider.GetRequiredService<IWorkflowVersionSnapshotReader>()
                .FindAsync("version", "partition", new() { MaxSourceBytes = 4096 }).AsTask());
            Assert.Equal(code, error.Code); Assert.Equal(unsupported, error.Unsupported);
        }
        finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_snapshot_refuses_caller_owned_transaction_or_pending_changes_without_taking_ownership(bool pending)
    {
        var path = Path.Combine(Path.GetTempPath(), "elsa-workflow-snapshot-owner-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var services = new ServiceCollection(); services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor());
            services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = "Data Source=" + path + ";Pooling=False" });
            using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await context.Database.EnsureCreatedAsync();
            var definition = new Elsa.Workflows.Design.Persistence.Core.Entities.WorkflowDefinition { Id = "definition", Name = "Source", TenantId = "partition" };
            context.Definitions.Add(definition); await context.SaveChangesAsync();
            await using var transaction = pending ? null : await context.Database.BeginTransactionAsync();
            if (pending) definition.Name = "Unsaved caller value";
            var error = await Assert.ThrowsAsync<WorkflowVersionSnapshotException>(() => scope.ServiceProvider.GetRequiredService<IWorkflowVersionSnapshotReader>()
                .FindAsync("version", "partition", new()).AsTask());
            Assert.True(error.Unsupported);
            if (pending) Assert.True(context.ChangeTracker.HasChanges());
            else Assert.Same(transaction, context.Database.CurrentTransaction);
        }
        finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }

    [Fact]
    public async Task Selected_snapshot_contract_rejects_a_second_owner_at_composition_and_startup()
    {
        var services = new ServiceCollection();
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = "Data Source=:memory:;Pooling=False" });
        services.AddSingleton<IWorkflowVersionSnapshotReader>(new OtherReader());
        Assert.Throws<InvalidOperationException>(() => DesignPersistenceBackend.Find(services)!.EnsureOwnsRegisteredContracts(services));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ValidateDesignPersistenceReplacementContractsStartupTask(services).ExecuteAsync(default));
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = "Data Source=:memory:;Pooling=False" }));
        Assert.Equal(before, services.ToArray());
    }

    private sealed class OtherReader : IWorkflowVersionSnapshotReader
    {
        public ValueTask<WorkflowVersionSnapshot?> FindAsync(string versionId, string expectedPartition, WorkflowVersionSnapshotLimits limits,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<WorkflowVersionSnapshot?>(null);
    }

    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("partition")); }
}
