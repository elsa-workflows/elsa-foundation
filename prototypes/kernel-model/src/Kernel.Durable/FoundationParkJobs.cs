using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

internal sealed record ParkJobRecipe(string Key, string? Arguments, long? Deadline);
internal sealed record ParkJobOpen(IReadOnlyList<ParkJobRecipe> Recipes, string? Registry, long? NextGeneration,
    string? Restarts, IReadOnlyList<InboxKey> Pending, IReadOnlyList<Message> Writes);

public sealed partial class InMemoryStore
{
    private const int JobDispositionEntryBytes = 40;

    internal ParkJobOpen? PlanFoundationJobs(InstanceRow row, IReadOnlyList<ParkJobRecipe> recipes, long timerBytes)
    {
        var registered = JobRegistry.Read(row.Jobs);
        if (registered.Count != recipes.Count || recipes.Count > 0 && row.Creation <= 0) return null;
        if (registered.Count == 0) return new(recipes, row.Jobs, row.NextJobGeneration, null, [], []);

        var byKey = new Dictionary<string, ParkJobRecipe>(StringComparer.Ordinal);
        foreach (var recipe in recipes)
            if (recipe.Key is null || !FoundationMetadataLimits.FitsName(recipe.Key, preservesCodeUnits: true) ||
                !FoundationMetadataLimits.FitsContent(recipe.Arguments) || !byKey.TryAdd(recipe.Key, recipe))
                return null;
        var pending = new List<InboxKey>();
        var writes = new List<Message>();
        var previousRestarts = ReadJobRestarts(row.ParkRestartedJobs);
        var restarts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var origin = CreationProtocol.Origin(row.Id, row.Creation);
        var bytes = timerBytes;
        var nextGeneration = row.NextJobGeneration ?? 1;
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
                var digest = Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(key)));
                var restart = ticket is not { Done: false } && result is not ({ Done: false } or { KeptBy: ParkKeepReason.Arrived });
                var deadline = recipe.Deadline;
                var timerId = deadline is null ? null : FormattableString.Invariant($"{origin}#recover-job:{nextGeneration}");
                var payload = Json.Write(new JobTicket(row.Model, key, recipe.Arguments, deadline, timerId));
                bytes += RestoreWaitAllowance + Encoding.UTF8.GetByteCount(payload);
                if (bytes > MaxRestoreBytes || !FoundationMetadataLimits.FitsContent(payload)) return null;

                if (!restart)
                {
                    if (ticket is { Done: false }) pending.Add(ticketKey);
                    if (previousRestarts.TryGetValue(digest, out var restarted) && restarted == generation)
                        restarts.Add(digest, generation);
                    continue;
                }

                var newKey = InboxKey.Ticket(row.Id, key, row.Creation, nextGeneration);
                var job = new Message(newKey.Id, new(row.Id), Kinds.Job, payload, row.Id,
                    TargetCreation: row.Creation, SenderCreation: row.Creation) { JobGeneration = nextGeneration };
                writes.Add(job.WithInboxScope(newKey.Scope).AsKernelTicket());
                if (timerId is not null)
                {
                    var timeout = new Message(timerId, new(row.Id), Kinds.JobDeadline, Json.Write(new JobDeadline(key)), row.Id,
                        Due: deadline, TargetCreation: row.Creation, SenderCreation: row.Creation);
                    bytes += RestoreWaitAllowance + Encoding.UTF8.GetByteCount(timeout.Payload!);
                    writes.Add(timeout.AsIssued().AsKernelTicket());
                }
                if (bytes > MaxRestoreBytes || nextGeneration == long.MaxValue) return null;
                registered[index] = new(key, nextGeneration);
                restarts.Add(digest, nextGeneration++);
            }
        }
        return writes.Any(message => !FoundationMetadataLimits.FitsComposed(message)) ? null :
            new(recipes, JobRegistry.Write(registered), nextGeneration, WriteJobRestarts(restarts), pending, writes);
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
