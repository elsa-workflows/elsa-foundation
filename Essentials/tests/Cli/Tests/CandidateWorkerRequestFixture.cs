using Elsa.Cli.Worker;

namespace Elsa.Cli.Tests;

/// <summary>One valid private capture for transport and process tests; it makes no host-parity claim.</summary>
internal static class CandidateWorkerRequestFixture
{
    public static WorkerRequest Create()
    {
        var capture = Guid.NewGuid().ToString("N");
        return new WorkerRequest
        {
            Command = WorkerCommands.InspectCandidate,
            HostDirectory = "/compiled/host",
            HostName = "Example.Host",
            DepsFile = "/compiled/host/Example.Host.deps.json",
            PackageRoots = [],
            Candidate = new WorkerCandidatePayload
            {
                Version = 1,
                Source = "captured-workbench-json-v1",
                InvocationId = Guid.NewGuid().ToString("N"),
                CaptureId = capture,
                Shell = "default",
                Environment = "Production",
                AcceptedFeatureIds = ["Probe"],
                RemovedFeatureIds = [],
                Files = new[] { "appsettings.json", "shells.json", "shells.Production.json" }
                    .Select(name => new WorkerCandidateFile
                    {
                        Name = name, CaptureId = capture, Content = Convert.ToBase64String("{}"u8.ToArray())
                    }).ToArray()
            }
        };
    }
}
