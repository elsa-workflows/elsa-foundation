using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests;

internal static class EtlRuntimeFixtures
{
    internal static readonly string Hash = new('a', 64);

    public static EtlRunAdmission Admission(string runId = "run-1", EtlScope? scope = null)
    {
        var pipeline = EtlArtifactJson.Write(EtlTestPipelines.Create());
        var artifact = new EtlArtifact(1, "artifact-1", EtlArtifactJson.Hash(pipeline), "orders", 1, pipeline);
        var address = new EtlNativeResultAddress(1, "workflow-1", "activity-1", "executable-1", Hash,
            "node-1", "bookmark-1", "registration-1", "target-1", "etl-completion", Hash,
            "etl-result", 1, "once", EtlNativeResultAddress.CompletionProviderId);
        return new(new(scope ?? new EtlScope("tenant-a", "partition-a"), runId, artifact, address), new(
            new("source", "v1", Hash, Hash), new("lookup", "v1", Hash, Hash),
            new("output", "v1", Hash, null), new("errors", "v1", Hash, null)));
    }

    public static EtlPreparedRun Prepared(long recordCount = 4) => new(1,
        new(Hash + ".csv", Hash, 128), new(new string('b', 64) + ".csv", Hash, 64),
        new(new string('c', 64) + ".index", Hash, checked((recordCount + 1) * 24)), recordCount);
}
