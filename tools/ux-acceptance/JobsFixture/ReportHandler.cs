using System.Collections.Concurrent;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.UxAcceptance;

internal sealed record ReportInput(string Title, decimal[] Amounts, int DelayMilliseconds = 250, string Mode = "success", string Tag = "report");
internal sealed record ReportOutput(string Title, decimal Total, int Count, string[] Lines);
internal sealed class ReportControl
{
    private readonly ConcurrentDictionary<string, byte> _recovered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _barriers = new(StringComparer.Ordinal);
    internal void Recover(string tag) { _recovered[tag] = 1; if (_barriers.TryGetValue(tag, out var barrier)) barrier.TrySetResult(); }
    internal bool Recovered(string tag) => _recovered.ContainsKey(tag);
    internal Task WaitAsync(string tag, CancellationToken ct)
    {
        if (_barriers.Count >= 128 || tag.Length > 128) throw new InvalidOperationException("The owned report barrier bound was exceeded.");
        var barrier = _barriers.GetOrAdd(tag, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (Recovered(tag)) barrier.TrySetResult(); return barrier.Task.WaitAsync(ct);
    }
}
internal sealed class ReportHandler(ReportControl control) : IJobHandler<ReportInput, ReportOutput>
{
    internal static readonly JobHandlerSupport Support = new(new("ux.report", "1"), new("ux.report.input", 1), new("ux.report.result", 1), "owned-report-handler-v1");
    internal static readonly JobHandlerSupport UnrelatedSupport = Support with { Handler = new("ux.unrelated", "1"), ImplementationIdentity = "owned-unrelated-handler-v1" };
    public async ValueTask<ReportOutput> ExecuteAsync(ReportInput input, JobExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 256 || input.Amounts is not { Length: > 0 and <= 100 } || input.DelayMilliseconds is < 0 or > 15000)
            throw new InvalidOperationException("The report request is invalid.");
        await context.Progress.ReportAsync(new(.25, "Preparing report lines", null), cancellationToken);
        // late is an explicit adverse handler which ignores cancellation, never a simulated provider.
        await Task.Delay(input.DelayMilliseconds, input.Mode == "late" ? CancellationToken.None : cancellationToken);
        if (input.Mode == "barrier") await control.WaitAsync(input.Tag, cancellationToken);
        if (input.Mode == "fail-once" && context.AttemptNumber == 1 || input.Mode == "recoverable" && !control.Recovered(input.Tag))
            throw new InvalidOperationException("The owned report source is unavailable.");
        if (input.Mode != "late") cancellationToken.ThrowIfCancellationRequested();
        return new(input.Title, input.Amounts.Sum(), input.Amounts.Length, input.Amounts.Select((amount, index) => $"Line {index + 1}: {amount}").ToArray());
    }
}
