using Elsa.Cli.Worker;
using System.Text.Json;

// The worker speaks exactly one protocol on exactly two streams: a request in, one response out. Anything
// else the host's closure decides to print — a logger, a module's own Console.WriteLine — is redirected to
// stderr first, so it reaches the operator without corrupting the response the front end parses.
var response = Console.OpenStandardOutput();
Console.SetOut(Console.Error);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

WorkerResponse result;
try
{
    var request = await JsonSerializer.DeserializeAsync<WorkerRequest>(Console.OpenStandardInput(), WorkerContract.Json, cancellation.Token);
    result = request is null
        ? Failed("invalid-request", "The worker was given an empty request.")
        : await WorkerRunner.RunAsync(request, cancellation.Token);
}
catch (JsonException failure)
{
    result = Failed("invalid-request", $"The worker was not given valid request JSON: {failure.Message}");
}
catch (OperationCanceledException)
{
    // Cancelled, not failed: the front end reports the interruption, and no artifact is claimed either way.
    return ToolExitCode.Refusal;
}

await JsonSerializer.SerializeAsync(response, result, WorkerContract.Json, cancellation.Token);
await response.FlushAsync(cancellation.Token);
return result.ExitCode;

static WorkerResponse Failed(string code, string message) =>
    new() { ExitCode = ToolExitCode.Refusal, Error = new() { Code = code, Message = message } };
