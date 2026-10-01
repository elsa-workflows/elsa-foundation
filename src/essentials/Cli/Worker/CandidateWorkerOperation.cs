namespace Elsa.Cli.Worker;

/// <summary>Admits only captured candidate requests and contains all closure diagnostics at the private boundary.</summary>
public sealed class CandidateWorkerOperation
{
    private readonly Func<WorkerRequest, CancellationToken, Task<WorkerResponse>> runHost;

    public CandidateWorkerOperation(Func<WorkerRequest, CancellationToken, Task<WorkerResponse>> runHost)
    {
        ArgumentNullException.ThrowIfNull(runHost);
        this.runHost = runHost;
    }

    public async Task<WorkerResponse> RunAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            WorkerContract.ValidateCandidateRequest(request);
        }
        catch (WorkerRefusal)
        { return WorkerRefusal.Usage("candidate-request-invalid", "The candidate worker request is invalid.").ToResponse(); }

        try
        {
            return await runHost(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw; }
        catch (WorkerRefusal refusal)
        {
            // Legacy loader refusals contain paths and raw exception details. Even known candidate codes
            // get a local message instead of trusting an exception raised inside selected host code.
            return refusal.Code switch
            {
                "candidate-package-unavailable" => WorkerRefusal.Resolution(refusal.Code, "The selected host package closure could not be loaded.").ToResponse(),
                "candidate-closure-changed" => WorkerRefusal.Resolution(refusal.Code, "The selected installed host closure changed during inspection.").ToResponse(),
                "candidate-capability-unavailable" => WorkerRefusal.Resolution(refusal.Code, "The selected host has no complete candidate inspection capability.").ToResponse(),
                "candidate-response-invalid" => WorkerRefusal.Resolution(refusal.Code, "The candidate host response is invalid.").ToResponse(),
                "candidate-response-too-large" => WorkerRefusal.Resolution(refusal.Code, "The candidate host response exceeds the supported bound.").ToResponse(),
                "candidate-request-too-large" => WorkerRefusal.Usage(refusal.Code, "The candidate host request exceeds the supported bound.").ToResponse(),
                _ => Unavailable()
            };
        }
        catch (Exception failure) when (failure is BadImageFormatException || WorkerRunner.IsNonFatal(failure))
        { return Unavailable(); }
    }

    private static WorkerResponse Unavailable() => WorkerRefusal.Resolution("candidate-host-unavailable",
        "The selected installed host closure could not be inspected.").ToResponse();
}
