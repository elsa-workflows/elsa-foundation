using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Unknown_artifact_format_is_refused_without_persisting_a_terminal_result()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var known = ConnectorInvocation(files.Scope, "unknown-terminal-format");
        var artifact = known.Artifact;
        var unsupported = known with
        {
            Artifact = new EtlArtifact(artifact.FormatVersion + 1, artifact.ArtifactId, artifact.ContentHash,
                artifact.DefinitionId, artifact.DefinitionVersion, artifact.Pipeline)
        };
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false);

        await using var operation = host.Services.CreateAsyncScope();
        var services = operation.ServiceProvider;
        var store = services.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Invalid,
            (await store.RejectAdmissionAsync(unsupported, new EtlRunDiagnostic("etl.unsupported-format"))).Status);
        Assert.Equal(EtlStoreStatus.Invalid, (await store.CancelAsync(unsupported)).Status);
        Assert.Null(await store.FindRunAsync(unsupported.Scope, unsupported.RunId));
        var db = services.GetRequiredService<EtlRuntimeDbContext>();
        Assert.Empty(await db.Runs.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Terminals.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Outbox.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task V2_cancel_before_admission_persists_a_replayable_terminal_envelope_and_outbox()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var invocation = ConnectorInvocation(files.Scope, "v2-cancel-before-admission");
        var firstHost = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false);
        string payloadHash;

        await using (var operation = firstHost.Services.CreateAsyncScope())
        {
            var services = operation.ServiceProvider;
            var store = services.GetRequiredService<IEtlRuntimeStore>();
            var bindings = EtlRuntimeFixtures.Admission(invocation.RunId, invocation.Scope).Bindings;

            Assert.Equal(EtlStoreStatus.Invalid, (await store.AdmitAsync(new(invocation, bindings))).Status);
            Assert.Null(await store.FindRunAsync(invocation.Scope, invocation.RunId));
            Assert.Null(await store.ClaimPreparationAsync(invocation.Scope, "v2-file-store-check"));
            Assert.Null(await store.ClaimPartitionAsync(invocation.Scope, invocation.RunId, "v2-file-store-check"));

            var cancelled = await store.CancelAsync(invocation);
            Assert.Equal(EtlStoreStatus.Applied, cancelled.Status);
            Assert.Equal(EtlTerminalOutcome.Cancelled, Assert.IsType<EtlTerminalResult>(cancelled.Result).Outcome);
            Assert.Equal(EtlStoreStatus.Replay, (await store.CancelAsync(invocation)).Status);

            var changed = invocation with
            {
                ResultAddress = invocation.ResultAddress with { ActivityExecutionId = "different-activity" }
            };
            Assert.Equal(EtlStoreStatus.Conflict, (await store.CancelAsync(changed)).Status);

            var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(invocation.Scope, invocation.RunId));
            Assert.Equal(EtlRunStatus.Cancelled, run.Status);
            Assert.Equal(invocation.Artifact.FormatVersion, run.Invocation.Artifact.FormatVersion);
            Assert.Null(run.Bindings);
            Assert.Null(run.Prepared);
            Assert.Equal(cancelled.Result, run.TerminalResult);
            await AssertNoFileWorkAsync(services, store, invocation);

            var outbox = Assert.IsType<EtlRuntimeOutboxRecord>(await files.ReadOutboxAsync(firstHost.Services, invocation.RunId));
            Assert.Null(outbox.Disposition);
            Assert.Equal(0, outbox.Attempts);
            payloadHash = outbox.PayloadHash;
        }

        await files.StopHostAsync(firstHost);
        var reopened = await files.StartHostAsync(EfMigratePolicy.Validate, includeBindings: false);
        await using (var operation = reopened.Services.CreateAsyncScope())
        {
            var services = operation.ServiceProvider;
            var store = services.GetRequiredService<IEtlRuntimeStore>();
            var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(invocation.Scope, invocation.RunId));
            Assert.Equal(EtlRuntimeIdentity.InvocationFingerprint(invocation), EtlRuntimeIdentity.InvocationFingerprint(run.Invocation));
            Assert.Equal(EtlRunStatus.Cancelled, run.Status);
            Assert.Equal(EtlTerminalOutcome.Cancelled, run.TerminalResult!.Outcome);
            Assert.Null(await store.ClaimPreparationAsync(invocation.Scope, "v2-reopen-check"));
            Assert.Null(await store.ClaimPartitionAsync(invocation.Scope, invocation.RunId, "v2-reopen-check"));
            await AssertNoFileWorkAsync(services, store, invocation);

            var delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(invocation.Scope, "v2-terminal-reader"));
            Assert.Equal(invocation.RunId, delivery.RunId);
            Assert.Equal(run.TerminalResult, delivery.Result);
            Assert.Equal(payloadHash, delivery.PayloadHash);
        }
    }

    [Fact]
    public async Task V2_admission_refusal_persists_its_terminal_envelope_and_rejects_changed_replays()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var invocation = ConnectorInvocation(files.Scope, "v2-refused-admission");
        var firstHost = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false);
        string payloadHash;
        var diagnostic = new EtlRunDiagnostic("etl.connector-runtime-unavailable");

        await using (var operation = firstHost.Services.CreateAsyncScope())
        {
            var services = operation.ServiceProvider;
            var store = services.GetRequiredService<IEtlRuntimeStore>();
            var bindings = EtlRuntimeFixtures.Admission(invocation.RunId, invocation.Scope).Bindings;

            Assert.Equal(EtlStoreStatus.Invalid, (await store.AdmitAsync(new(invocation, bindings))).Status);
            Assert.Null(await store.FindRunAsync(invocation.Scope, invocation.RunId));

            var refused = await store.RejectAdmissionAsync(invocation, diagnostic);
            Assert.Equal(EtlStoreStatus.Applied, refused.Status);
            Assert.Equal(EtlStoreStatus.Replay, (await store.RejectAdmissionAsync(invocation, diagnostic)).Status);
            Assert.Equal(EtlStoreStatus.Conflict,
                (await store.RejectAdmissionAsync(invocation, new EtlRunDiagnostic("etl.other-refusal"))).Status);

            var changed = ConnectorInvocation(files.Scope, invocation.RunId,
                EtlV2TestPipelines.Create() with
                {
                    Mapping = [
                        new EtlOutputField("id", EtlFieldOrigin.Source, "id"),
                        new EtlOutputField("name", EtlFieldOrigin.Source, "name")
                    ]
                });
            Assert.Equal(EtlStoreStatus.Conflict, (await store.RejectAdmissionAsync(changed, diagnostic)).Status);

            var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(invocation.Scope, invocation.RunId));
            Assert.Equal(EtlRunStatus.BusinessError, run.Status);
            Assert.Equal(EtlTerminalOutcome.BusinessError, run.TerminalResult!.Outcome);
            Assert.Equal(diagnostic, run.TerminalResult.Diagnostic);
            Assert.Null(run.Bindings);
            Assert.Null(run.Prepared);
            await AssertNoFileWorkAsync(services, store, invocation);

            var outbox = Assert.IsType<EtlRuntimeOutboxRecord>(await files.ReadOutboxAsync(firstHost.Services, invocation.RunId));
            Assert.Null(outbox.Disposition);
            Assert.Equal(0, outbox.Attempts);
            payloadHash = outbox.PayloadHash;
        }

        await files.StopHostAsync(firstHost);
        var reopened = await files.StartHostAsync(EfMigratePolicy.Validate, includeBindings: false);
        await using (var operation = reopened.Services.CreateAsyncScope())
        {
            var services = operation.ServiceProvider;
            var store = services.GetRequiredService<IEtlRuntimeStore>();
            var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(invocation.Scope, invocation.RunId));
            Assert.Equal(EtlRuntimeIdentity.InvocationFingerprint(invocation), EtlRuntimeIdentity.InvocationFingerprint(run.Invocation));
            Assert.Equal(EtlRunStatus.BusinessError, run.Status);
            Assert.Equal(diagnostic, run.TerminalResult!.Diagnostic);
            Assert.Null(await store.ClaimPreparationAsync(invocation.Scope, "v2-reopen-check"));
            Assert.Null(await store.ClaimPartitionAsync(invocation.Scope, invocation.RunId, "v2-reopen-check"));
            await AssertNoFileWorkAsync(services, store, invocation);

            var delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(invocation.Scope, "v2-terminal-reader"));
            Assert.Equal(run.TerminalResult, delivery.Result);
            Assert.Equal(payloadHash, delivery.PayloadHash);
        }
    }

    private static EtlInvocation ConnectorInvocation(EtlScope scope, string runId, EtlPipelineV2? pipeline = null)
    {
        var result = new EtlArtifactCompiler().Compile("orders", 1, pipeline ?? EtlV2TestPipelines.Create());
        Assert.Empty(result.Diagnostics);
        var artifact = Assert.IsType<EtlArtifact>(result.Artifact);
        Assert.Equal(EtlArtifactV2Json.FormatVersion, artifact.FormatVersion);
        return EtlRuntimeFixtures.Admission(runId, scope).Invocation with { Artifact = artifact };
    }

    private static async Task AssertNoFileWorkAsync(IServiceProvider services, IEtlRuntimeStore store, EtlInvocation invocation)
    {
        var scopeKey = EtlRuntimeIdentity.ScopeKey(invocation.Scope);
        var runKey = EtlRuntimeRecordCodec.EncodeIdentity(invocation.RunId, "run id");
        var db = services.GetRequiredService<EtlRuntimeDbContext>();
        Assert.Empty(await db.PreparedRuns.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.RunKey == runKey).ToListAsync());
        Assert.Empty(await db.Partitions.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.RunKey == runKey).ToListAsync());
        Assert.Empty(await db.Reservations.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.RunKey == runKey).ToListAsync());
        Assert.Empty(await db.Manifest.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.RunKey == runKey).ToListAsync());
        Assert.Null(await store.ClaimPreparationAsync(invocation.Scope, "v2-no-file-work"));
        Assert.Null(await store.ClaimPartitionAsync(invocation.Scope, invocation.RunId, "v2-no-file-work"));
    }
}
