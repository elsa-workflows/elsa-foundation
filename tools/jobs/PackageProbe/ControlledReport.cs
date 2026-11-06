using System.Text;
using System.Text.Json;
using Elsa.Jobs.Core;

internal static partial class Probe
{
    // v1's record, schemas and implementation pin remain unchanged. Chaos fields
    // belong to this distinct contribution so old accepted v1 input keeps its meaning.
    internal static readonly JobHandlerSupport ControlledSupport = new(new("package.report", "2"),
        new("package.report.controlled.input", 2), new("package.report.controlled.result", 2),
        "package.report.implementation.v2");

    internal static JobGateKey Gate(ProbeCommand command) => command.Gate ?? new(JobGateKind.Queue, "default");

    internal static JobRequest Request(ProbeCommand command)
    {
        var support = command.ControlledInput is null ? Support : ControlledSupport;
        if (command.SubmitUnrelated) support = support with { Handler = new("package.unrelated", support.Handler.Version) };
        var input = command.ControlledInput is { } controlled
            ? JsonSerializer.SerializeToElement(controlled, Json)
            : JsonSerializer.SerializeToElement(command.Input ?? throw new InvalidDataException("Report input required."), Json);
        return new(support.Handler, new(support.InputSchema, input), support.ResultSchema, command.Queue,
            command.Policy ?? new(new(3, TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(1), 2, 0, true),
                TimeSpan.FromMinutes(2), TimeSpan.FromDays(30)), command.NotBefore, command.Association);
    }
}

internal sealed record ControlledReportInput(string Name, ReportLine[] Lines, string EntryDirectory,
    string? ReleaseDirectory = null, int FailUntilAttempt = 0, bool IgnoreCancellation = false,
    string? EffectUrl = null, int WorkMilliseconds = 0, string? FailureFile = null);

internal sealed record ControlledReportResult(string Name, int Lines, int Units, decimal Subtotal,
    string InvocationKey, string? EffectId);

internal sealed class ControlledReportHandler : IJobHandler<ControlledReportInput, ControlledReportResult>
{
    public ControlledReportHandler() { }

    public async ValueTask<ControlledReportResult> ExecuteAsync(ControlledReportInput input,
        JobExecutionContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(input.EntryDirectory);
        var prefix = Path.Combine(input.EntryDirectory, context.AttemptId);
        await Probe.WriteAsync(prefix + ".entry.json", new
        {
            context.JobId, context.AttemptId, context.AttemptNumber, context.IdempotencyKey,
            enteredAt = DateTimeOffset.UtcNow
        });
        var outcome = "ExitedWithoutResult";
        string? effectId = null;
        try
        {
            if (input.EffectUrl is { } destination)
            {
                if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http")
                    throw new InvalidDataException("The qualification destination must be an owned loopback HTTP endpoint.");
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var content = new StringContent(JsonSerializer.Serialize(new
                {
                    invocationKey = context.IdempotencyKey, context.JobId, context.AttemptId,
                    report = input.Name, subtotal = input.Lines.Sum(line => line.Quantity * line.UnitPrice)
                }, Probe.Json), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(uri, content, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                effectId = body.RootElement.GetProperty("effectId").GetString();
                await Probe.WriteAsync(prefix + ".effect.json", new { context.AttemptId, effectId,
                    duplicate = body.RootElement.GetProperty("duplicate").GetBoolean() });
            }
            var workUntil = DateTimeOffset.UtcNow.AddMilliseconds(input.WorkMilliseconds);
            var cancellationRecorded = false;
            while (DateTimeOffset.UtcNow < workUntil || input.ReleaseDirectory is { } releases &&
                   !File.Exists(Path.Combine(releases, context.AttemptNumber + ".release")))
            {
                if (cancellationToken.IsCancellationRequested && !cancellationRecorded)
                {
                    cancellationRecorded = true;
                    await Probe.WriteAsync(prefix + ".cancellation.json", new { context.AttemptId, observedAt = DateTimeOffset.UtcNow });
                }
                await Task.Delay(25, input.IgnoreCancellation ? CancellationToken.None : cancellationToken);
            }
            if (context.AttemptNumber <= input.FailUntilAttempt || input.FailureFile is { } failure && File.Exists(failure))
                throw new InvalidOperationException("Controlled qualification handler failure.");
            outcome = "Succeeded";
            return new(input.Name, input.Lines.Length, input.Lines.Sum(line => line.Quantity),
                input.Lines.Sum(line => line.Quantity * line.UnitPrice), context.IdempotencyKey, effectId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "CancellationObserved";
            throw;
        }
        finally
        {
            await Probe.WriteAsync(prefix + ".exit.json", new { context.AttemptId, outcome, exitedAt = DateTimeOffset.UtcNow });
        }
    }
}
