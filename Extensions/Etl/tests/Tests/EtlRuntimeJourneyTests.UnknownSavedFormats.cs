using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Theory]
    [InlineData("artifact")]
    [InlineData("prepared")]
    public async Task Claimed_partition_execution_refuses_an_unknown_saved_format_without_progress(string format)
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var (host, invocation) = await files.PrepareWorkerRunAsync(
            EtlTestPipelines.Create(), $"unknown-execution-{format}", CancellationToken.None);

        EtlPartitionLease lease;
        await using (var operation = host.Services.CreateAsyncScope())
        {
            var services = operation.ServiceProvider;
            var store = services.GetRequiredService<IEtlRuntimeStore>();
            lease = Assert.IsType<EtlPartitionLease>(
                await store.ClaimPartitionAsync(files.Scope, invocation.RunId, "format-refusal-worker"));

            var db = services.GetRequiredService<EtlRuntimeDbContext>();
            var run = await db.Runs.SingleAsync();
            if (format == "artifact")
            {
                var savedInvocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
                var artifact = savedInvocation.Artifact;
                var unsupported = new EtlArtifact(artifact.FormatVersion + 1, artifact.ArtifactId,
                    artifact.ContentHash, artifact.DefinitionId, artifact.DefinitionVersion, artifact.Pipeline);
                run.InvocationJson = EtlRuntimeRecordCodec.Write(savedInvocation with { Artifact = unsupported });
            }
            else
            {
                var prepared = await db.PreparedRuns.SingleAsync();
                prepared.FormatVersion = EtlPreparedRun.CurrentFormatVersion + 1;
            }

            await db.SaveChangesAsync();
        }

        await using (var operation = host.Services.CreateAsyncScope())
        {
            var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await execution.ExecutePartitionAsync(lease));
        }

        await using var verification = host.Services.CreateAsyncScope();
        var persisted = verification.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
        var runAfter = await persisted.Runs.AsNoTracking().SingleAsync();
        var partitions = await persisted.Partitions.AsNoTracking().ToListAsync();

        Assert.Equal((int)EtlRunStatus.Running, runAfter.Status);
        Assert.Equal(0, runAfter.AcceptedRecords);
        Assert.Equal(0, runAfter.RejectedRecords);
        Assert.Equal(0, runAfter.FilteredRecords);
        Assert.Equal(0, runAfter.OutstandingRecords);
        Assert.Equal(0, runAfter.OutstandingBytes);
        Assert.NotEmpty(partitions);
        Assert.All(partitions, partition => Assert.Equal(partition.StartOrdinal, partition.NextOrdinal));
        Assert.False(Directory.Exists(files.OutputRoot));
        Assert.False(Directory.Exists(files.ErrorRoot));
        Assert.Empty(await persisted.Reservations.AsNoTracking().ToListAsync());
        Assert.Empty(await persisted.Manifest.AsNoTracking().ToListAsync());
        Assert.Empty(await persisted.Terminals.AsNoTracking().ToListAsync());
        Assert.Empty(await persisted.Outbox.AsNoTracking().ToListAsync());

        if (format == "prepared")
            Assert.Equal(EtlPreparedRun.CurrentFormatVersion + 1,
                (await persisted.PreparedRuns.AsNoTracking().SingleAsync()).FormatVersion);
        else
            Assert.Throws<InvalidDataException>(() =>
                EtlRuntimeRecordCodec.ReadInvocation(runAfter.InvocationJson));
    }
}
