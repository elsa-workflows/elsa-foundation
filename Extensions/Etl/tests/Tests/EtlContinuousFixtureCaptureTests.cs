using Elsa.Etl.Core;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Etl.Tests;

/// <summary>
/// Captures the spec 1192 continuous-run fixture in <c>Shared/Fixtures/Continuous1192</c>. It only runs when
/// <c>ETL_1192_FIXTURE_CAPTURE_PATH</c> names the database to write, on the 1192 writer the fixture README names.
/// </summary>
public sealed partial class EtlRuntimeJourneyTests
{
    internal const int Continuous1192FixturePort = 29196;

    [Fact]
    public async Task Capture_the_spec_1192_continuous_run_fixture()
    {
        var target = Environment.GetEnvironmentVariable("ETL_1192_FIXTURE_CAPTURE_PATH");
        if (string.IsNullOrEmpty(target)) return;
        // The pinned HTTP binding handles hash the endpoint, so the fixture's loopback port is fixed.
        await using var files = new RuntimeFiles(CsvJourneySource, new("tenant-fixture", "compaction-1192"));
        var feed = new CursorFeed();
        feed.Append("a", 1, "Ada");
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, token) =>
            request.Method == "POST" ? ValueTask.FromResult(new EtlHttpLoopbackResponse(204, "")) : feed.RespondAsync(request, token),
            fixedPort: Continuous1192FixturePort);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, configureRuntime: runtime =>
            ConfigureConnectorBindings(runtime, files.Scope, endpoint, csvSource: false, httpMain: true, httpErrors: true));
        var invocation = ConnectorInvocation(files.Scope, "compaction-1192",
            EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(1) });
        await files.AdmitAsync(host, invocation);
        await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 1);
        feed.Append("a", 2, "Bram");
        feed.Append("b", 1, "Cleo");
        await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 3);
        feed.Append("a", 3, "Dirk");
        feed.Append("a", 4, "Eva");
        feed.Append("b", 2, "Finn");
        await WaitForIdleAsync(host.Services, files.Scope, invocation.RunId, accepted: 6);
        // Idle shards hold no lease and no attached page, so the fixture needs no page-cache files.
        await files.StopHostAsync(host);
        File.Copy(Path.Combine(Path.GetDirectoryName(files.SnapshotRoot)!, "runtime.db"), target, overwrite: false);
    }
}
