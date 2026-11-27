using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Kernel.Foundation.Contracts;
using Kernel.Native;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelActivityTimeline(KernelRuns runs)
{
    internal sealed record Snapshot(InMemoryActivityExecutionInspectionStore Activities, long? Revision);

    internal static bool Supports(WorkflowRunHeader? run) =>
        run is { Model: NativeExecutionModel.ModelKey, History: Kernel.Foundation.Contracts.History.Full };

    internal async Task<Snapshot?> OpenAsync(KernelRunReader.Run run, long? upperRevision, string? activity, CancellationToken cancellationToken)
    {
        var actor = new ActorRef(run.State.WorkflowExecutionId);
        var result = new InMemoryActivityExecutionInspectionStore();
        if (run.Header is not { } header || !Supports(header))
            return upperRevision is null ? new Snapshot(result, null) : null;

        var frames = new Dictionary<string, JournalFrame>(StringComparer.Ordinal);
        var scheduled = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        long sequence = 0;
        long? revision = null;
        DateTimeOffset? turnTime = null;
        DateTimeOffset? cancelledAt = null;
        PageToken? from = null;
        var finished = false;
        while (!finished)
        {
            var page = await runs.TailAsync(actor, from, cancellationToken);
            if (page.Items.Count == 0) break;
            if (from is null)
            {
                var head = page.JournalHeadRevision ?? throw new NotSupportedException("The kernel journal reader does not expose its committed revision.");
                upperRevision ??= head;
                if (upperRevision > head) return null;
                // A retained termination can bypass the model and therefore has no per-frame cancel entries.
                if (page.JournalCancellation is { } cancellation && cancellation.Revision <= upperRevision)
                    cancelledAt = ReadTime(cancellation.At);
            }
            foreach (var entry in page.Items)
            {
                if (entry.Revision > upperRevision)
                {
                    finished = true;
                    break;
                }
                if (entry.Revision != revision)
                {
                    revision = entry.Revision;
                    turnTime = null;
                }
                if (Apply(entry.Entry))
                {
                    finished = true;
                    break;
                }
            }
            if (page.Next is null) break;
            if (page.Next == from) throw new InvalidOperationException("The kernel journal returned the same continuation.");
            from = page.Next;
        }

        // A retired run may lose its journal while pages are being read.
        if ((await runs.GetAsync(actor, cancellationToken)).Run is not { } current ||
            current.Run.Workflow != header.Workflow)
            return null;

        foreach (var recorded in frames.Values)
        {
            var frame = recorded.CompletedAt is null && cancelledAt is { } terminal
                ? recorded with { Status = ActivityExecutionStatus.Cancelled, CompletedAt = terminal }
                : recorded;
            var node = run.Executable?.NodesById.GetValueOrDefault(frame.Node);
            var provenance = ActivitySchedulingProvenance.Empty with { ParentActivityExecutionId = frame.Parent };
            await result.SaveAsync(new ActivityExecutionInspectionProjection(
                frame.Id, actor.Id, frame.Node, node?.AuthoredActivityId ?? frame.Node, node?.ActivityType ?? "", node?.ActivityTypeVersion ?? "",
                frame.Status, null, frame.Sequence, frame.ScheduledAt, frame.StartedAt, frame.CompletedAt, null, null,
                frame.CompletedAt ?? frame.StartedAt, provenance, [], [], [], [], FrozenDictionary<string, string>.Empty), cancellationToken);
        }
        return new Snapshot(result, upperRevision);

        bool Apply(string entry)
        {
            using var json = JsonDocument.Parse(entry);
            if (json.RootElement.ValueKind != JsonValueKind.String ||
                json.RootElement.GetString() is not { Length: > 2 } text || text[1] != ':')
                return false;
            if (text[0] == 't')
            {
                turnTime = ReadTime(text.AsSpan(2));
                return false;
            }
            if (turnTime is not { } time) return false;
            var split = text.IndexOf(':', 2);
            if (split <= 2 || split == text.Length - 1) return false;
            var id = text[2..split];
            var value = text[(split + 1)..];
            if (text[0] == 'a')
            {
                if (activity is null || activity == id) scheduled.TryAdd(id, time);
                return false;
            }
            if (text[0] == 'b')
            {
                var parentAt = value.LastIndexOf(':');
                if (parentAt <= 0) return false;
                sequence++;
                if (activity is null || activity == id)
                {
                    var parent = parentAt == value.Length - 1 ? null : value[(parentAt + 1)..];
                    var start = scheduled.Remove(id, out var earlier) ? earlier : time;
                    frames.TryAdd(id, new JournalFrame(id, value[..parentAt], parent, sequence, start, time));
                }
                return false;
            }
            var status = text[0] switch
            {
                'c' => ActivityExecutionStatus.Completed,
                'f' => ActivityExecutionStatus.Faulted,
                'x' => ActivityExecutionStatus.Cancelled,
                _ => ActivityExecutionStatus.Running
            };
            if (status == ActivityExecutionStatus.Running || !frames.TryGetValue(id, out var frame) ||
                frame.Node != value || frame.CompletedAt is not null)
                return false;
            frames[id] = frame with { Status = status, CompletedAt = time };
            return activity is not null;
        }
    }

    private static DateTimeOffset? ReadTime(ReadOnlySpan<char> value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) ? ReadTime(milliseconds) : null;

    private static DateTimeOffset? ReadTime(long milliseconds) =>
        milliseconds >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() && milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    private sealed record JournalFrame(string Id, string Node, string? Parent, long Sequence, DateTimeOffset ScheduledAt, DateTimeOffset StartedAt,
        ActivityExecutionStatus Status = ActivityExecutionStatus.Running, DateTimeOffset? CompletedAt = null);
}
