using Elsa.Cli.Worker;
using System.Text.Json;

var ownerMode = args.Length == 7 && args[0] == "--candidate-inspection" && args[1] == "--candidate-owner";
var candidateMode = args is ["--candidate-inspection"];

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (ownerMode)
{
    // The supervisor's stdout/stderr are the forwarded private candidate transport. It never emits its own
    // diagnostics on either stream. Payload status requests frontend termination of the owned scope.
    Console.SetOut(TextWriter.Null);
    Console.SetError(TextWriter.Null);
    return await CandidateProcessOwner.RunSupervisorAsync(args, cancellation.Token);
}

// The worker speaks exactly one protocol on exactly two streams: a request in, one response out. Anything
// else the host's closure decides to print — a logger, a module's own Console.WriteLine — is redirected to
// stderr in normal mode. Candidate mode suppresses both console streams because configuration is private.
var response = Console.OpenStandardOutput();
if (candidateMode)
{
    // Suppress both managed console streams before parsing or loading any selected host code.
    Console.SetOut(TextWriter.Null);
    Console.SetError(TextWriter.Null);
}
else
    Console.SetOut(Console.Error);

WorkerResponse result;
try
{
    var request = candidateMode
        ? await WorkerContract.ReadCandidateRequestAsync(Console.OpenStandardInput(), cancellation.Token)
        : await WorkerContract.ReadRequestAsync(Console.OpenStandardInput(), cancellation.Token);
    result = request is null
        ? candidateMode ? Failed("candidate-request-invalid", "The candidate worker request is invalid.")
            : Failed("invalid-request", "The worker was given an empty request.")
        : candidateMode ? await WorkerRunner.RunCandidateAsync(request, cancellation.Token)
            : await WorkerRunner.RunAsync(request, cancellation.Token);
}
catch (JsonException)
{
    result = candidateMode ? Failed("candidate-request-invalid", "The candidate worker request is invalid.")
        : Failed("invalid-request", "The worker was not given a valid closed request.");
}
catch (WorkerRefusal refusal) when (candidateMode)
{
    result = refusal.Code == "candidate-request-too-large"
        ? Failed("candidate-request-too-large", "The candidate request exceeds the supported size limit.")
        : Failed("candidate-request-invalid", "The candidate worker request is invalid.");
}
catch (OperationCanceledException)
{
    // Cancelled, not failed: the front end reports the interruption, and no artifact is claimed either way.
    return ToolExitCode.Refusal;
}
catch (Exception failure) when (candidateMode && (failure is BadImageFormatException || WorkerRunner.IsNonFatal(failure)))
{
    result = new() { ExitCode = ToolExitCode.ResolutionFailure,
        Error = new() { Code = "candidate-host-unavailable", Message = "The selected installed host closure could not be inspected." } };
}

await JsonSerializer.SerializeAsync(response, result, WorkerContract.Json, cancellation.Token);
await response.FlushAsync(cancellation.Token);
return result.ExitCode;

static WorkerResponse Failed(string code, string message) =>
    new() { ExitCode = ToolExitCode.Refusal, Error = new() { Code = code, Message = message } };
