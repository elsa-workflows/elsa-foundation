using System.Net;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeApiHttpTests
{
    [Theory]
    [InlineData("empty-records")]
    [InlineData("empty-bytes")]
    [InlineData("unretained-effect")]
    [InlineData("too-many-effects")]
    [InlineData("file-retry")]
    [InlineData("unknown-page-offset")]
    [InlineData("source-acknowledgement")]
    [InlineData("missing-retry-time")]
    [InlineData("unexpected-retry-time")]
    [InlineData("unknown-phase")]
    [InlineData("retry-at-deadline")]
    [InlineData("retry-after-deadline")]
    [InlineData("acknowledged-without-journal")]
    [InlineData("acknowledged-but-waiting")]
    [InlineData("retained-but-acknowledged")]
    [InlineData("captured-wrong-status")]
    public async Task Connector_api_rejects_incoherent_custom_inspection_instead_of_presenting_false_progress(string defect)
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync(configureServices: services =>
            services.AddScoped<IEtlConnectorRuntimeInspector>(provider => new IncoherentInspector(
                (IEtlConnectorRuntimeInspector)provider.GetRequiredService<IEtlRuntimeStore>(), defect)));
        await SeedConnectorAsync(host, "incoherent-inspection", new("tenant-a", "partition-a"));
        using var response = await host.SendAsync(HttpMethod.Get,
            "/etl/runs/incoherent-inspection/connector-partitions", permissions: ReadPermissions);
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "etl.provider-invalid");
        Assert.DoesNotContain("private-invalid-phase", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private sealed class IncoherentInspector(IEtlConnectorRuntimeInspector inner, string defect) : IEtlConnectorRuntimeInspector
    {
        public int MaximumPageSize => inner.MaximumPageSize;
        public ValueTask<EtlConnectorRunInspection?> FindConnectorInspectionAsync(EtlScope scope, string runId,
            CancellationToken cancellationToken = default) => inner.FindConnectorInspectionAsync(scope, runId, cancellationToken);

        public async ValueTask<IReadOnlyList<EtlConnectorPartitionInspection>> ListConnectorPartitionsAsync(EtlScope scope,
            string runId, int afterPartitionOrdinal, int take, CancellationToken cancellationToken = default)
        {
            var page = await inner.ListConnectorPartitionsAsync(scope, runId, afterPartitionOrdinal, take, cancellationToken);
            return page.Select(Corrupt).ToArray();
        }

        private EtlConnectorPartitionInspection Corrupt(EtlConnectorPartitionInspection row)
        {
            var retry = new EtlConnectorRetryInspection("waiting", 1, DateTimeOffset.MaxValue, DateTimeOffset.UnixEpoch, 503);
            var effect = new EtlConnectorEffectInspection(EtlConnectorKind.Http, 1, "retained", null);
            var owed = new EtlConnectorOwedInspection(1, 1, true, effect, null);
            return defect switch
            {
                "empty-records" => row with { OwedBatch = owed with { RecordCount = 0 } },
                "empty-bytes" => row with { OwedBatch = owed with { InputBytes = 0 } },
                "unretained-effect" => row with { OwedBatch = owed with { OutputRetained = false } },
                "too-many-effects" => row with { OwedBatch = owed with { Errors = effect } },
                "file-retry" => row with { OwedBatch = owed with { Main = effect with { Kind = EtlConnectorKind.Jsonl, Retry = retry } } },
                "unknown-page-offset" => row with { Http = row.Http! with { NextItem = 1 } },
                "source-acknowledgement" => row with { Http = row.Http! with { Fetch = retry with { Phase = "acknowledged", NotBefore = null } } },
                "missing-retry-time" => row with { Http = row.Http! with { Fetch = retry with { NotBefore = null } } },
                "unexpected-retry-time" => row with { Http = row.Http! with { Fetch = retry with { Phase = "captured" } } },
                "unknown-phase" => row with { Http = row.Http! with { Fetch = retry with { Phase = "private-invalid-phase", NotBefore = null } } },
                "retry-at-deadline" => row with { Http = row.Http! with { Fetch = retry with { NotBefore = retry.Deadline } } },
                "retry-after-deadline" => row with { Http = row.Http! with { Fetch = retry with { Deadline = DateTimeOffset.UnixEpoch.AddSeconds(-1) } } },
                "acknowledged-without-journal" => row with { OwedBatch = owed with { Main = effect with { State = "acknowledged" } } },
                "acknowledged-but-waiting" => row with { OwedBatch = owed with { Main = effect with { State = "acknowledged", Retry = retry } } },
                "retained-but-acknowledged" => row with { OwedBatch = owed with { Main = effect with { Retry = retry with { Phase = "acknowledged", NotBefore = null, LastStatusCode = 204 } } } },
                "captured-wrong-status" => row with { Http = row.Http! with { Fetch = retry with { Phase = "captured", NotBefore = null } } },
                _ => throw new ArgumentOutOfRangeException(nameof(defect))
            };
        }
    }
}
