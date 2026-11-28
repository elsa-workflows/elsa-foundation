using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>The immutable recipient for one occurrence of a workflow user task.</summary>
public sealed record NativeUserTaskRecipient(Contract.ActorRef Actor, string Frame, string Wait, string TaskId);

/// <summary>Delivery of the accepted journal receipt to its exact Actor wait.</summary>
public sealed record NativeUserTaskAccepted(NativeUserTaskRecipient Recipient, string CompletionId);

/// <summary>The exact task ownership observed in one committed workflow checkpoint.</summary>
public sealed record NativeUserTaskCheckpoint(bool IsWaiting, string? AcceptedCompletionId, string? ConsumedCompletionId);

/// <summary>Runs the durable task journal effects of a qualified Actor wait.</summary>
public interface INativeUserTaskJobs
{
    /// <summary>
    /// Opens the task after its owning wait commits. An exact repeated open succeeds.
    /// False definitively refuses the request before creating a task. Uncertain attempts throw so they can replay.
    /// </summary>
    ValueTask<bool> OpenAsync(string tenant, NativeUserTaskRecipient recipient, string nodeId, NativeUserTaskRequest request,
        CancellationToken cancellationToken);

    /// <summary>Settles the exact accepted reply after Actor acceptance. A repeat returns the same frozen reply.</summary>
    ValueTask<NativeUserTaskResponse?> SettleAsync(string tenant, NativeUserTaskRecipient recipient, string completionId,
        CancellationToken cancellationToken);

    /// <summary>Confirms the task journal has durably recorded this exact committed completion.</summary>
    ValueTask<bool> CanReleaseCompletionAsync(string tenant, NativeUserTaskRecipient recipient, string nodeId, string completionId,
        CancellationToken cancellationToken);
}

internal static class NativeUserTaskWaits
{
    internal const string Kind = "user-task";
    internal const int OutstandingLimit = 64;
    internal const string RetryKind = "native.user-task.retry";
    internal static readonly TimeSpan JournalJobTimeout = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan JournalRetryDelay = TimeSpan.FromSeconds(5);
    private const string OpenJobPrefix = "native.user-task.open/";
    private const string SettleJobPrefix = "native.user-task.settle/";

    internal static bool TryGetJob(string jobKey, out string wait, out bool settling)
    {
        settling = jobKey.StartsWith(SettleJobPrefix, StringComparison.Ordinal);
        wait = settling ? jobKey[SettleJobPrefix.Length..] :
            jobKey.StartsWith(OpenJobPrefix, StringComparison.Ordinal) ? jobKey[OpenJobPrefix.Length..] : "";
        return wait.Length != 0;
    }

    internal static string OpenJobKey(string wait) => OpenJobPrefix + wait;
    internal static string SettleJobKey(string wait) => SettleJobPrefix + wait;

    internal sealed record Consumption(string Frame, string Wait, string Node, string CompletionId);

    internal sealed record OpenArguments(NativeUserTaskRecipient Recipient, string NodeId, NativeUserTaskRequest Request);
    internal sealed record SettlementArguments(NativeUserTaskRecipient Recipient, string CompletionId);
    internal sealed record Opened(string TaskId, bool Succeeded);
    internal sealed record Retry(Contract.TimerSpec Timer, Contract.JobSpec Job);

    internal static T? Read<T>(string? body) where T : class
    {
        try { return body is null ? null : JsonSerializer.Deserialize<T>(body); }
        catch (JsonException) { return null; }
    }
}

internal sealed partial class Burst
{
    private ActivityFault? CheckUserTaskWait(Frame frame, Transition transition)
    {
        if (options.UserTaskJobs is null || !turn.HasExactIdentity || !turn.SavesWaitRecipes || turn.TestScopeId is not null ||
            _s.Mode == Modes.Ephemeral || _wf.Nodes[frame.Node].Type != typeof(CreateUserTask) ||
            transition.Triggers is not [UserTaskWaitTrigger] || transition.Effects is not (null or []))
            return new("user-task.wait-unavailable", "This workflow run cannot open an Actor user task.");

        var outstanding = _s.Waits.Values.Count(wait => wait.Kind == NativeUserTaskWaits.Kind) + (_s.UserTaskConsumptions?.Count ?? 0);
        return outstanding < NativeUserTaskWaits.OutstandingLimit ? null :
            new("user-task.wait-capacity", "This workflow run has reached its limit of outstanding Actor user tasks.");
    }

    private void RegisterUserTask(Frame frame, string wait, NativeUserTaskRequest request)
    {
        var effectKey = Kernel.Model.Contracts.EffectKey.For(turn.Actor.Id, frame.Id, wait, frame.Attempt);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(effectKey)));
        // The physical journal distinguishes this namespace from existing classic hexadecimal task IDs.
        var taskId = "t" + digest[1..];
        var recipient = new NativeUserTaskRecipient(turn.Actor, frame.Id, wait, taskId);
        var arguments = new NativeUserTaskWaits.OpenArguments(recipient, frame.Node, request);
        var job = StartUserTaskJob(NativeUserTaskWaits.OpenJobKey(wait), arguments);
        _s.Waits.Add(wait, new WaitEntry(frame.Id, NativeUserTaskWaits.Kind, taskId) { Job = job });
    }

    private Contract.JobSpec StartUserTaskJob(string key, object arguments)
    {
        var timeout = NativeUserTaskWaits.JournalJobTimeout;
        turn.StartUserTaskJob(key, arguments, timeout);
        return new(key, JsonSerializer.Serialize(arguments)) { Deadline = checked(turn.Now + (long)timeout.TotalMilliseconds) };
    }

    private void AcceptUserTask(string? payload)
    {
        if (options.UserTaskJobs is null || NativeUserTaskWaits.Read<NativeUserTaskAccepted>(payload) is not { } accepted ||
            accepted.Recipient is not { } recipient || recipient.Actor != turn.Actor || string.IsNullOrEmpty(recipient.Wait) ||
            string.IsNullOrWhiteSpace(accepted.CompletionId) || !_s.Waits.TryGetValue(recipient.Wait, out var wait) ||
            wait.Kind != NativeUserTaskWaits.Kind || wait.Frame != recipient.Frame || wait.Key != recipient.TaskId ||
            wait.AcceptedTaskCompletionId is { } prior && prior != accepted.CompletionId)
            return;

        wait = wait with { AcceptedTaskCompletionId = accepted.CompletionId };
        _s.Waits[recipient.Wait] = wait;
        // The task can be visible before its opening job reports back. Persist the accepted reply
        // while that job owns its slot, then start settlement from that exact committed wait.
        if (wait.Job is null && wait.TaskRetry is null && !_s.Agenda.Exists(work => work.Resume && !work.Gone && work.Wait == recipient.Wait))
            StartSettlement(recipient.Wait, wait);
    }

    private void RecordUserTaskConsumption(Frame frame, string id, WaitEntry wait, JsonNode? payload)
    {
        var response = Values.To<NativeUserTaskResponse>(payload);
        if (response is null || response.TaskId != wait.Key || response.CompletionId != wait.AcceptedTaskCompletionId)
            throw new InvalidOperationException("The completed activity does not own this task response.");
        (_s.UserTaskConsumptions ??= new(StringComparer.Ordinal)).Add(wait.Key,
            new(frame.Id, id, frame.Node, response.CompletionId));
    }

    private void AcknowledgeUserTask(string? payload)
    {
        if (NativeUserTaskWaits.Read<NativeUserTaskAccepted>(payload) is not { Recipient: { } recipient } acknowledged ||
            recipient.Actor != turn.Actor || string.IsNullOrWhiteSpace(recipient.TaskId) ||
            _s.UserTaskConsumptions is not { } consumptions ||
            consumptions.GetValueOrDefault(recipient.TaskId) is not { } exact ||
            exact.Frame != recipient.Frame || exact.Wait != recipient.Wait || exact.CompletionId != acknowledged.CompletionId)
            return;

        consumptions.Remove(recipient.TaskId);
        if (consumptions.Count == 0) _s.UserTaskConsumptions = null;
    }

    private void StartSettlement(string id, WaitEntry wait)
    {
        var recipient = new NativeUserTaskRecipient(turn.Actor, wait.Frame, id, wait.Key);
        var arguments = new NativeUserTaskWaits.SettlementArguments(recipient, wait.AcceptedTaskCompletionId!);
        _s.Waits[id] = wait with { Job = StartUserTaskJob(NativeUserTaskWaits.SettleJobKey(id), arguments) };
    }

    private bool HandleUserTaskJob(Contract.JobFinished result)
    {
        if (!NativeUserTaskWaits.TryGetJob(result.Key, out var id, out var settling)) return false;
        if (!_s.Waits.TryGetValue(id, out var wait) || wait.Kind != NativeUserTaskWaits.Kind || wait.Job?.Key != result.Key ||
            !_s.Frames.TryGetValue(wait.Frame, out var frame)) return true;

        _s.Waits[id] = wait with { Job = null };
        if (settling)
        {
            if (result.Error is not null)
                RetryUserTaskJob(id, wait);
            else if (NativeUserTaskWaits.Read<NativeUserTaskResponse>(result.Result) is { } response &&
                response.TaskId == wait.Key && response.CompletionId == wait.AcceptedTaskCompletionId)
                Resume(id, Values.From(response));
            else
                _s.Waits[id] = wait with { Job = null, AcceptedTaskCompletionId = null };
            return true;
        }

        if (wait.AcceptedTaskCompletionId is not null)
        {
            StartSettlement(id, wait);
            return true;
        }

        if (result.Error is null && NativeUserTaskWaits.Read<NativeUserTaskWaits.Opened>(result.Result) is { } opening &&
            opening.TaskId == wait.Key)
        {
            if (opening.Succeeded) return true;
            _s.Waits.Remove(id);
            Fail(frame, new ActivityFault("user-task.open-refused", "The task assignment, form or input values could not be accepted."));
            return true;
        }

        // A failed or timed-out job may already have committed the journal record. The exact
        // wait and its original typed input survive a delayed retry and can accept an early reply.
        RetryUserTaskJob(id, wait);
        return true;
    }

    private void RetryUserTaskJob(string id, WaitEntry wait)
    {
        var due = checked(turn.Now + (long)NativeUserTaskWaits.JournalRetryDelay.TotalMilliseconds);
        var payload = new TimerFired(id);
        var timer = new Contract.TimerSpec(turn.SendToSelf(NativeUserTaskWaits.RetryKind, payload, due),
            NativeUserTaskWaits.RetryKind, JsonSerializer.Serialize(payload), due);
        _s.Waits[id] = wait with { Job = null, TaskRetry = new(timer, wait.Job!) };
    }

    private void RetryUserTaskJob(Contract.Message message)
    {
        if (NativeUserTaskWaits.Read<TimerFired>(message.Payload) is not { Wait: { } id } ||
            !_s.Waits.TryGetValue(id, out var wait) || wait.TaskRetry is not { } retry ||
            retry.Timer.Id != message.Id || retry.Timer.Due > turn.Now)
            return;

        wait = wait with { TaskRetry = null };
        if (wait.AcceptedTaskCompletionId is not null)
            StartSettlement(id, wait);
        else
            _s.Waits[id] = wait with { Job = StartUserTaskJob(retry.Job.Key, JsonSerializer.Deserialize<JsonElement>(retry.Job.Arguments!)) };
    }
}
