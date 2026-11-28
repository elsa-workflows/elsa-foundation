using System.Text.Json;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>The immutable recipient for one occurrence of a workflow user task.</summary>
public sealed record NativeUserTaskRecipient(Contract.ActorRef Actor, string Frame, string Wait, string TaskId);

/// <summary>Installs the task after its owning wait commits. A retry of the same recipient must return the same task.</summary>
public interface INativeUserTaskOpenJobs
{
    ValueTask OpenAsync(string tenant, NativeUserTaskRecipient recipient, NativeUserTaskRequest request, CancellationToken cancellationToken);
}

internal static class NativeUserTaskWaits
{
    internal const string Kind = "user-task";
    internal static readonly TimeSpan OpenTimeout = TimeSpan.FromMinutes(1);
    private const string OpenJobPrefix = "native.user-task.open/";

    internal static bool TryGetOpenWait(string jobKey, out string wait)
    {
        wait = jobKey.StartsWith(OpenJobPrefix, StringComparison.Ordinal) ? jobKey[OpenJobPrefix.Length..] : "";
        return wait.Length != 0;
    }

    internal static string OpenJobKey(string wait) => OpenJobPrefix + wait;

    internal sealed record OpenArguments(NativeUserTaskRecipient Recipient, NativeUserTaskRequest Request);
    internal sealed record Opened(string TaskId);
}

internal sealed partial class Burst
{
    private bool CanOpenUserTask(Transition transition) =>
        options.UserTaskOpenJobs is not null && turn.HasExactIdentity && turn.SavesWaitRecipes &&
        turn.TestScopeId is null && _s.Mode != Modes.Ephemeral &&
        transition.Triggers is [UserTaskWaitTrigger] && transition.Effects is null or [];

    private void RegisterUserTask(Frame frame, string wait, NativeUserTaskRequest request)
    {
        var taskId = Kernel.Model.Contracts.EffectKey.For(turn.Actor.Id, frame.Id, wait, frame.Attempt);
        var recipient = new NativeUserTaskRecipient(turn.Actor, frame.Id, wait, taskId);
        var arguments = new NativeUserTaskWaits.OpenArguments(recipient, request);
        var timeout = NativeUserTaskWaits.OpenTimeout;
        var job = new Contract.JobSpec(NativeUserTaskWaits.OpenJobKey(wait), JsonSerializer.Serialize(arguments))
        {
            Deadline = checked(turn.Now + (long)timeout.TotalMilliseconds)
        };
        _s.Waits.Add(wait, new WaitEntry(frame.Id, NativeUserTaskWaits.Kind, taskId) { Job = job });
        turn.StartUserTaskJob(job.Key, arguments, timeout);
    }

    private bool HandleUserTaskOpen(Contract.JobFinished result)
    {
        if (!NativeUserTaskWaits.TryGetOpenWait(result.Key, out var id)) return false;
        if (!_s.Waits.TryGetValue(id, out var wait) || wait.Kind != NativeUserTaskWaits.Kind || wait.Job?.Key != result.Key ||
            !_s.Frames.TryGetValue(wait.Frame, out var frame)) return true;

        if (result.Error is null && result.Result is { } body && OpenedTaskId(body) == wait.Key)
        {
            _s.Waits[id] = wait with { Job = null };
            return true;
        }

        _s.Waits.Remove(id);
        Fail(frame, new ActivityFault("user-task.open-unavailable", "The user task could not be opened."));
        return true;
    }

    private static string? OpenedTaskId(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<NativeUserTaskWaits.Opened>(body)?.TaskId;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
