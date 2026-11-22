using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

internal sealed record ParkJobRecipe(string Key, string? Arguments, long? Deadline);
internal sealed record ParkJobOpen(IReadOnlyList<ParkJobRecipe> Recipes, string? Registry, long? NextGeneration,
    string? Restarts, IReadOnlyList<InboxKey> Pending, IReadOnlyList<InboxKey> Retained,
    IReadOnlyList<InboxKey> RecalledDeadlines, IReadOnlyList<Message> Writes, long Bytes);

public sealed partial class InMemoryStore
{
    private const int JobDispositionEntryBytes = 40;

    internal static string? JobsAfterRecoveryPage(InstanceRow actor, ParkRecoveryPage? page)
    {
        if (actor.Status == InstanceStatus.Running || actor.Jobs is null || page is null) return actor.Jobs;
        var settled = page.Rows.Where(row => row.JobGeneration.HasValue)
            .ToDictionary(row => row.Cursor.Key, row => (row.Kind, row.JobGeneration!.Value));
        if (settled.Count == 0) return actor.Jobs;
        var jobs = JobRegistry.Read(actor.Jobs);
        var removed = jobs.RemoveAll(job =>
            settled.GetValueOrDefault(InboxKey.JobResult(actor.Id, job.Key, actor.Creation, job.Generation)) == (Kinds.JobFinished, job.Generation) ||
            settled.GetValueOrDefault(InboxKey.Ticket(actor.Id, job.Key, actor.Creation, job.Generation)) == (Kinds.Job, job.Generation));
        return removed == 0 ? actor.Jobs : JobRegistry.Write(jobs);
    }

    internal ParkJobOpen? PlanFoundationJobs(InstanceRow row, ParkRecoveryMode mode, IReadOnlyList<ParkJobRecipe> recipes, long timerBytes)
    {
        var registered = JobRegistry.Read(row.Jobs);
        if (registered.Count > recipes.Count || registered.Count > 0 && row.Creation <= 0) return null;

        var byKey = new Dictionary<string, ParkJobRecipe>(StringComparer.Ordinal);
        var registeredKeys = registered.Select(job => job.Key).ToHashSet(StringComparer.Ordinal);
        var bytes = timerBytes;
        var nextGeneration = row.NextJobGeneration ?? 1;
        foreach (var recipe in recipes)
        {
            if (recipe.Key is null || !FoundationMetadataLimits.FitsName(recipe.Key, preservesCodeUnits: true) ||
                !FoundationMetadataLimits.FitsContent(recipe.Arguments) || !byKey.TryAdd(recipe.Key, recipe))
                return null;
            if (!registeredKeys.Contains(recipe.Key) && !Charge(EncodeRecipe(recipe, nextGeneration, out _))) return null;
        }
        if (registered.Count == 0) return new(recipes, row.Jobs, row.NextJobGeneration, null, [], [], [], [], bytes);
        var discardedResults = new HashSet<string>(StringComparer.Ordinal);
        var pending = new List<InboxKey>();
        var retained = new List<InboxKey>();
        var recalls = new List<InboxKey>();
        var writes = new List<Message>();
        var previousRestarts = ReadJobRestarts(row.ParkRestartedJobs);
        var restarts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        lock (_gate)
        {
            for (var index = 0; index < registered.Count; index++)
            {
                var (key, generation) = registered[index];
                if (generation < 0 || generation >= nextGeneration || !byKey.TryGetValue(key, out var recipe)) return null;
                var ticketKey = InboxKey.Ticket(row.Id, key, row.Creation, generation);
                var ticket = Messages.GetValueOrDefault(ticketKey);
                if (ticket is not null && !IsJobEnvelope(ticket.Message, row, Kinds.Job, generation)) return null;
                var resultKey = InboxKey.JobResult(row.Id, key, row.Creation, generation);
                var result = ticket is { Done: false } ? null : Messages.GetValueOrDefault(resultKey);
                if (result is not null && (!result.Message.FoundationJobResult || !IsJobEnvelope(result.Message, row, Kinds.JobFinished, generation))) return null;

                // Preserve these full keys even when conversion or sweep has removed the rows.
                if (ticket is { Done: false }) pending.Add(ticketKey);
                else retained.Add(ticketKey);
                retained.Add(resultKey);
                var oldTimer = ticket?.Message.Payload is not null ? ticket.Message.Read<JobTicket>().Timer : null;
                oldTimer ??= DurableTurn.FoundationJobDeadlineId(row.Id, row.Creation, generation);
                var oldDeadline = InboxKey.Issued(oldTimer);

                var digest = Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(key)));
                var payload = EncodeRecipe(recipe, nextGeneration, out var timerId);
                if (!Charge(payload)) return null;
                if (mode == ParkRecoveryMode.Resume && result is { KeptBy: ParkKeepReason.Consumed })
                {
                    retained.Add(oldDeadline);
                    discardedResults.Add(key);
                    continue;
                }
                var keepsResult = result is { Done: false } or { KeptBy: not null };
                var retriesTicket = !keepsResult && mode == ParkRecoveryMode.RetryAll && ticket is { KeptBy: ParkKeepReason.Consumed };
                var restart = ticket is not { Done: false } && !keepsResult && !retriesTicket;
                if (!restart)
                {
                    retained.Add(oldDeadline);
                    if (previousRestarts.TryGetValue(digest, out var restarted) && restarted == generation)
                        restarts.Add(digest, generation);
                    continue;
                }

                if (Messages.GetValueOrDefault(oldDeadline) is { Done: false, Message: { Kind: Kinds.JobDeadline } expiry } &&
                    expiry.Target.Id == row.Id && expiry.TargetCreation == row.Creation && expiry.Read<JobDeadline>().Key == key)
                    recalls.Add(oldDeadline);
                else
                    retained.Add(oldDeadline);
                var newKey = InboxKey.Ticket(row.Id, key, row.Creation, nextGeneration);
                var job = new Message(newKey.Id, new(row.Id), Kinds.Job, payload, row.Id,
                    TargetCreation: row.Creation, SenderCreation: row.Creation) { JobGeneration = nextGeneration };
                writes.Add(job.WithInboxScope(newKey.Scope).AsKernelTicket());
                if (timerId is not null)
                {
                    var timeout = new Message(timerId, new(row.Id), Kinds.JobDeadline, Json.Write(new JobDeadline(key)), row.Id,
                        Due: recipe.Deadline, TargetCreation: row.Creation, SenderCreation: row.Creation);
                    if (!Charge(timeout.Payload!)) return null;
                    writes.Add(timeout.AsIssued().AsKernelTicket());
                }
                if (nextGeneration == long.MaxValue) return null;
                registered[index] = new(key, nextGeneration);
                restarts.Add(digest, nextGeneration++);
            }
        }
        registered.RemoveAll(job => discardedResults.Contains(job.Key));
        return writes.Any(message => !FoundationMetadataLimits.FitsComposed(message)) ? null :
            new(recipes, JobRegistry.Write(registered), nextGeneration, WriteJobRestarts(restarts), pending, retained, recalls, writes, bytes);

        string EncodeRecipe(ParkJobRecipe recipe, long generation, out string? timerId)
        {
            timerId = recipe.Deadline is null ? null : DurableTurn.FoundationJobDeadlineId(row.Id, row.Creation, generation);
            return Json.Write(new JobTicket(row.Model, recipe.Key, recipe.Arguments, recipe.Deadline, timerId));
        }

        bool Charge(string payload)
        {
            bytes += RestoreWaitAllowance + Encoding.UTF8.GetByteCount(payload);
            return bytes <= MaxRestoreBytes && FoundationMetadataLimits.FitsContent(payload);
        }
    }

    private static bool IsJobEnvelope(Message message, InstanceRow row, string kind, long generation) =>
        message.Kind == kind && message.JobGeneration == generation && message.Sender == row.Id && message.SenderCreation == row.Creation &&
        message.Target.Id == row.Id && message.TargetCreation == row.Creation;

    private static Dictionary<string, long> ReadJobRestarts(string? value)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (value is null) return result;
        var bytes = Convert.FromBase64String(value);
        for (var offset = 0; offset < bytes.Length; offset += JobDispositionEntryBytes)
            result.Add(Convert.ToHexString(bytes.AsSpan(offset, 32)), BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset + 32, 8)));
        return result;
    }

    private static string? WriteJobRestarts(SortedDictionary<string, long> restarts)
    {
        if (restarts.Count == 0) return null;
        var bytes = new byte[restarts.Count * JobDispositionEntryBytes];
        var offset = 0;
        foreach (var (digest, generation) in restarts)
        {
            Convert.FromHexString(digest).CopyTo(bytes, offset);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset + 32, 8), generation);
            offset += JobDispositionEntryBytes;
        }
        return Convert.ToBase64String(bytes);
    }

    private void ApplyFoundationJobOpen(ParkJobOpen? jobs)
    {
        if (jobs is null) return;
        foreach (var message in jobs.Writes) Messages.Add(message.InboxKey, new(message, ++_seq, 0));
    }
}
