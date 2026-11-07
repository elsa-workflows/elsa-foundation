using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Qualification-only observation/fault injection over public EF diagnostics. No
// production registration hook, internal type reference or source-project friend is used.
internal sealed record ProbeFaultOptions(string WriteKind, string Event, string Action, string MarkerFile,
    string? ReleaseFile = null, string? TargetJobId = null, bool RequirePhysicalStarted = false,
    bool Repeat = false, int WaitSeconds = 20);

internal sealed class ProbeEfFaults : IObserver<DiagnosticListener>,
    IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private static readonly string BeforeSave = CoreEventId.SaveChangesStarting.Name!;
    private static readonly string AfterCommit = RelationalEventId.TransactionCommitted.Name!;
    private static readonly string BeforeTransaction = RelationalEventId.TransactionStarting.Name!;
    private static readonly string TransactionError = RelationalEventId.TransactionError.Name!;
    private readonly ProbeFaultOptions _options;
    private readonly IDisposable _all;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly ConcurrentDictionary<Guid, WriteEvidence> _writes = new();
    private readonly ConcurrentDictionary<(Guid Context, Guid Transaction), InjectedFailure> _injected = new();
    private int _armed;
    private int _fired;

    internal ProbeEfFaults(ProbeFaultOptions options)
    {
        if (options.WriteKind is not ("admit" or "claim" or "renew" or "complete") ||
            options.Event is not ("before-save" or "after-commit" or "before-transaction") ||
            options.Action is not ("block" or "throw" or "block-then-throw") ||
            options.WaitSeconds is < 1 or > 60 || string.IsNullOrWhiteSpace(options.MarkerFile))
            throw new InvalidDataException("The qualification fault descriptor is invalid.");
        if (options.Action.Contains("block", StringComparison.Ordinal) && options.ReleaseFile is null)
            throw new InvalidDataException("A blocking qualification fault needs an owned release file.");
        _options = options;
        _armed = options.Event == "before-transaction" ? 0 : 1;
        _all = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == DbLoggerCategory.Name)
            lock (_subscriptions)
                _subscriptions.Add(listener.Subscribe(this, name => name == BeforeSave || name == AfterCommit ||
                    name == BeforeTransaction || name == TransactionError));
    }

    internal void ArmAfterHostValidation() => Interlocked.Exchange(ref _armed, 1);

    public void OnNext(KeyValuePair<string, object?> notification)
    {
        if (Volatile.Read(ref _armed) == 0) return;
        if (notification.Key == TransactionError && notification.Value is TransactionErrorEventData { Context: { } failed } error &&
            _injected.TryGetValue((failed.ContextId.InstanceId, error.TransactionId), out var injected) &&
            ReferenceEquals(error.Exception, injected.Exception))
        {
            var count = Interlocked.Increment(ref injected.Observed);
            Probe.WriteAsync(_options.MarkerFile + ".propagated.json", new
            {
                providerEvent = notification.Key, exactExceptionInstance = true,
                contextId = failed.ContextId.InstanceId, transactionId = error.TransactionId,
                count, occurrence = injected.Occurrence, exceptionType = nameof(LostStoreReplyException)
            }).GetAwaiter().GetResult();
        }
        else if (notification.Key == BeforeTransaction && _options.Event == "before-transaction" &&
                 notification.Value is TransactionStartingEventData { Context: { } beginning } start &&
                 beginning.GetType().Namespace!.StartsWith("Elsa.Jobs.Persistence.EntityFrameworkCore", StringComparison.Ordinal))
        {
            // This is a direct claim intent armed after host validation. Selection
            // has not happened and SQLite has not acquired its BEGIN IMMEDIATE lock.
            Fire(new(_options.WriteKind, beginning.ContextId.InstanceId, start.TransactionId,
                _options.TargetJobId ?? throw new InvalidDataException("The direct claim target is required."),
                null, null, null, false, null, null, ChangeTrackerWriteObserved: false), notification.Key, commitObserved: false);
        }
        else if (notification.Key == BeforeSave && notification.Value is DbContextEventData { Context: { } context })
        {
            if (!context.GetType().Namespace!.StartsWith("Elsa.Jobs.Persistence.EntityFrameworkCore", StringComparison.Ordinal)) return;
            var evidence = Observe(context);
            if (evidence is null) return;
            _writes[context.ContextId.InstanceId] = evidence;
            if (_options.Event == "before-save") Fire(evidence, notification.Key, commitObserved: false);
        }
        else if (notification.Key == AfterCommit && notification.Value is TransactionEndEventData { Context: { } committed } end &&
                 _writes.TryRemove(committed.ContextId.InstanceId, out var evidence))
        {
            if (end.TransactionId != evidence.TransactionId)
                throw new InvalidDataException("The qualification commit event did not match the observed transaction.");
            if (_options.Event == "after-commit") Fire(evidence, notification.Key, commitObserved: true);
        }
    }

    private static WriteEvidence? Observe(DbContext context)
    {
        var entries = context.ChangeTracker.Entries().ToArray();
        var job = entries.FirstOrDefault(entry => entry.Metadata.ClrType.Name == "JobRecord");
        if (job is null) return null;
        var attempt = entries.FirstOrDefault(entry => entry.Metadata.ClrType.Name == "JobAttemptRecord");
        var grant = entries.FirstOrDefault(entry => entry.Metadata.ClrType.Name == "JobClaimGrantRecord" && entry.State == EntityState.Added);
        var receipt = entries.FirstOrDefault(entry => entry.Metadata.ClrType.Name == "JobPhysicalReceiptRecord" && entry.State == EntityState.Added);
        var recognition = entries.FirstOrDefault(entry => entry.Metadata.ClrType.Name == "JobRecognitionRecord" && entry.State == EntityState.Added);
        var kind = grant is not null && attempt?.State == EntityState.Added ? "claim"
            : receipt is not null && attempt is not null && Text(attempt, "CompletionReceiptKey") == Text(receipt, "Key") ? "complete"
            : attempt is not null && attempt.Property("LastRenewalDigest").IsModified ? "renew"
            : recognition is not null && job.State == EntityState.Added ? "admit" : null;
        if (kind is null) return null;
        string? resultId = null;
        if (Text(job, "ResultJson") is { } json)
        {
            using var result = JsonDocument.Parse(json);
            resultId = result.RootElement.GetProperty("ResultId").GetString();
        }
        return new(kind, context.ContextId.InstanceId,
            context.Database.CurrentTransaction?.TransactionId ?? throw new InvalidDataException("The qualification write has no EF transaction."),
            Text(job, "JobId")!, attempt is null ? null : Text(attempt, "AttemptId"),
            resultId, receipt is null ? null : Text(receipt, "Key"),
            attempt?.Property("PhysicalStartedAt").CurrentValue is not null,
            attempt is null ? null : (long?)attempt.Property("ExpiresAt").CurrentValue,
            attempt is null ? null : (long?)attempt.Property("OwnershipRevision").CurrentValue);
    }

    private static string? Text(EntityEntry entry, string property) => entry.Property(property).CurrentValue as string;

    private void Fire(WriteEvidence evidence, string eventName, bool commitObserved)
    {
        if (evidence.Kind != _options.WriteKind || _options.TargetJobId is { } job && job != evidence.JobId ||
            _options.RequirePhysicalStarted && !evidence.PhysicalStarted) return;
        var occurrence = Interlocked.Increment(ref _fired);
        if (!_options.Repeat && occurrence != 1) return;
        var marker = occurrence == 1 ? _options.MarkerFile : _options.MarkerFile + "." + occurrence + ".json";
        Probe.WriteAsync(marker, new
        {
            providerEvent = eventName, commitObserved, evidence, occurrence,
            action = _options.Action, observedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();
        if (_options.Action.Contains("block", StringComparison.Ordinal))
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(_options.ReleaseFile))
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(_options.WaitSeconds))
                {
                    Probe.WriteAsync(marker + ".timed-out.json", new { evidence.ContextId, evidence.TransactionId,
                        occurrence, timedOut = true }).GetAwaiter().GetResult();
                    throw new TimeoutException("The owned qualification barrier was not released.");
                }
                Thread.Sleep(10);
            }
            Probe.WriteAsync(marker + ".released.json", new { evidence.ContextId, evidence.TransactionId,
                occurrence, timedOut = false }).GetAwaiter().GetResult();
        }
        if (_options.Action.Contains("throw", StringComparison.Ordinal))
        {
            var error = new LostStoreReplyException();
            _injected[(evidence.ContextId, evidence.TransactionId)] = new(error, occurrence);
            throw error;
        }
    }

    public void OnCompleted() { }
    public void OnError(Exception error) => throw new InvalidOperationException("EF diagnostic observation failed.", error);
    public void Dispose()
    {
        _all.Dispose();
        lock (_subscriptions) foreach (var subscription in _subscriptions) subscription.Dispose();
    }

    private sealed record WriteEvidence(string Kind, Guid ContextId, Guid TransactionId, string JobId,
        string? AttemptId, string? ResultId, string? ReceiptKey, bool PhysicalStarted, long? ExpiresAt,
        long? OwnershipRevision, bool ChangeTrackerWriteObserved = true);

    private sealed class InjectedFailure(Exception exception, int occurrence)
    {
        internal Exception Exception { get; } = exception;
        internal int Occurrence { get; } = occurrence;
        internal int Observed;
    }

    private sealed class LostStoreReplyException() : DbException("Controlled post-commit store reply loss.")
    {
        public override bool IsTransient => false;
    }
}
