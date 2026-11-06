using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

internal sealed record AckFaultOptions(string JobId, string Event, string MarkerFile, string? ReleaseFile = null);

// The same public EF-diagnostic mechanism as the standalone package witness, scoped specifically
// to the exact delivery ACK. It observes public EF metadata; it does not change a proposal or seed state.
internal sealed class AckReplyFault : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly AckFaultOptions _options;
    private readonly IDisposable _all;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly ConcurrentDictionary<Guid, AckEvidence> _writes = new();
    private readonly ConcurrentDictionary<(Guid Context, Guid Transaction), (AckEvidence Evidence, string ReleaseId)> _released = new();
    private readonly ConcurrentDictionary<(Guid Context, Guid Transaction), (LostAckReply Error, AckEvidence Evidence)> _injected = new();
    private int _fired;

    internal AckReplyFault(AckFaultOptions options)
    {
        if (options.Event is not ("before-save" or "after-commit") || string.IsNullOrWhiteSpace(options.JobId) ||
            options.Event == "before-save" && (options.ReleaseFile is null || File.Exists(options.ReleaseFile)))
            throw new InvalidDataException("Invalid owned ACK fault descriptor.");
        _options = options; _all = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == DbLoggerCategory.Name) lock (_subscriptions) _subscriptions.Add(listener.Subscribe(this,
            name => name == CoreEventId.SaveChangesStarting.Name || name == RelationalEventId.TransactionCommitted.Name || name == RelationalEventId.TransactionError.Name));
    }

    public void OnNext(KeyValuePair<string, object?> notification)
    {
        if (notification.Key == RelationalEventId.TransactionError.Name && notification.Value is TransactionErrorEventData { Context: { } failed } error &&
            _injected.TryGetValue((failed.ContextId.InstanceId, error.TransactionId), out var injected) && ReferenceEquals(error.Exception, injected.Error))
        {
            Probe.WriteAsync(_options.MarkerFile + ".propagated.json", new { exactExceptionInstance = true,
                injected.Evidence.ContextId, injected.Evidence.TransactionId, injected.Evidence.JobId,
                injected.Evidence.ProposalSha256, processId = Environment.ProcessId, occurrence = 1,
                exceptionType = nameof(LostAckReply) }).GetAwaiter().GetResult();
        }
        else if (notification.Key == CoreEventId.SaveChangesStarting.Name && notification.Value is DbContextEventData { Context: { } context })
        {
            if (context.GetType().Namespace != "Elsa.Jobs.Persistence.EntityFrameworkCore") return;
            var entries = context.ChangeTracker.Entries().ToArray();
            var delivery = entries.SingleOrDefault(entry => entry.Metadata.ClrType.Name == "JobDeliveryRecord" &&
                entry.State == EntityState.Modified && entry.Property("Acknowledged").IsModified && entry.Property("Acknowledged").CurrentValue is true);
            var job = entries.SingleOrDefault(entry => entry.Metadata.ClrType.Name == "JobRecord" && entry.Property("JobId").CurrentValue as string == _options.JobId);
            if (delivery is null || job is null) return;
            var transaction = context.Database.CurrentTransaction?.TransactionId ?? throw new InvalidDataException("The observed ACK has no transaction.");
            string Proposal() => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                delivery.Properties.OrderBy(property => property.Metadata.Name, StringComparer.Ordinal)
                    .Select(property => new { name = property.Metadata.Name, value = property.CurrentValue }).ToArray())));
            var evidence = new AckEvidence(context.ContextId.InstanceId, transaction, _options.JobId, Proposal());
            _writes[context.ContextId.InstanceId] = evidence;
            if (_options.Event == "before-save") Fire(evidence, false, Proposal);
        }
        else if (notification.Key == RelationalEventId.TransactionCommitted.Name && notification.Value is TransactionEndEventData { Context: { } context } end &&
            _writes.TryRemove(context.ContextId.InstanceId, out var write))
        {
            if (write.TransactionId != end.TransactionId) throw new InvalidDataException("ACK observation lost its exact transaction.");
            if (_options.Event == "after-commit") Fire(write, true);
            else if (_released.TryRemove((write.ContextId, write.TransactionId), out var released))
            {
                if (released.Evidence != write) throw new InvalidDataException("The committed ACK differs from its released proposal.");
                Probe.WriteAsync(_options.MarkerFile + ".committed.json", new { write.ContextId, write.TransactionId, write.JobId,
                    write.ProposalSha256, released.ReleaseId, processId = Environment.ProcessId, occurrence = 1,
                    state = "Committed", commitObserved = true, timedOut = false, proposalUnchanged = true }).GetAwaiter().GetResult();
            }
        }
    }

    private void Fire(AckEvidence evidence, bool commitObserved, Func<string>? currentProposal = null)
    {
        if (Interlocked.Increment(ref _fired) != 1) return;
        Probe.WriteAsync(_options.MarkerFile, new { evidence.ContextId, evidence.TransactionId, evidence.JobId,
            evidence.ProposalSha256, _options.Event, commitObserved, processId = Environment.ProcessId,
            occurrence = 1, state = "Entered" }).GetAwaiter().GetResult();
        if (!commitObserved)
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(_options.ReleaseFile))
            {
                if (watch.Elapsed >= TimeSpan.FromSeconds(45)) TimedOut();
                Thread.Sleep(10);
            }
            if (watch.Elapsed >= TimeSpan.FromSeconds(45)) TimedOut();
            using var request = JsonDocument.Parse(File.ReadAllText(_options.ReleaseFile!));
            var release = request.RootElement;
            if (release.GetProperty("contextId").GetGuid() != evidence.ContextId ||
                release.GetProperty("transactionId").GetGuid() != evidence.TransactionId ||
                release.GetProperty("jobId").GetString() != evidence.JobId ||
                release.GetProperty("proposalSha256").GetString() != evidence.ProposalSha256 ||
                release.GetProperty("processId").GetInt32() != Environment.ProcessId ||
                release.GetProperty("occurrence").GetInt32() != 1 || !release.GetProperty("afterCut").GetBoolean() ||
                release.GetProperty("historyDeletionResult").GetString() != "DependencyRetained" ||
                currentProposal?.Invoke() != evidence.ProposalSha256)
                throw new InvalidDataException("The owned release does not match this unchanged pre-ACK proposal.");
            var releaseId = release.GetProperty("releaseId").GetString();
            if (!Guid.TryParseExact(releaseId, "N", out _)) throw new InvalidDataException("The owned release identity is invalid.");
            _released[(evidence.ContextId, evidence.TransactionId)] = (evidence, releaseId!);
            Probe.WriteAsync(_options.MarkerFile + ".released.json", new { evidence.ContextId, evidence.TransactionId, evidence.JobId,
                evidence.ProposalSha256, releaseId, processId = Environment.ProcessId, occurrence = 1,
                state = "Released", timedOut = false, proposalUnchanged = true, afterCut = true,
                historyDeletionResult = "DependencyRetained" }).GetAwaiter().GetResult();
            return;

            void TimedOut()
            {
                Probe.WriteAsync(_options.MarkerFile + ".timed-out.json", new { evidence.ContextId, evidence.TransactionId,
                    evidence.JobId, evidence.ProposalSha256, processId = Environment.ProcessId, occurrence = 1,
                    state = "TimedOut", timedOut = true }).GetAwaiter().GetResult();
                throw new TimeoutException("The owned pre-ACK barrier was not released.");
            }
        }
        var error = new LostAckReply();
        _injected[(evidence.ContextId, evidence.TransactionId)] = (error, evidence);
        throw error;
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
    public void Dispose() { _all.Dispose(); lock (_subscriptions) foreach (var subscription in _subscriptions) subscription.Dispose(); }
    private sealed record AckEvidence(Guid ContextId, Guid TransactionId, string JobId, string ProposalSha256);
    private sealed class LostAckReply() : DbException("Controlled loss of the committed Jobs ACK response.");
}
