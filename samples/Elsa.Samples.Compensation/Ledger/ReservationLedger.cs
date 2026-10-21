using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Elsa.Samples.Compensation.Ledger;

/// <summary>
/// The participant behind <see cref="Activities.Reserve"/> and <see cref="Activities.Release"/>: an external system
/// whose state outlives the workflow host, following the compensation participant contract.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>A reservation is applied at most once per operation key; the same key again returns the original reservation.</item>
/// <item>A release undoes the reservation its target operation key made, when it exists.</item>
/// <item>When it does not exist yet, the release records a tombstone for the target key, and a later reservation with
/// that key is refused, so an effect that arrives late cannot slip in after its undo.</item>
/// <item>A release is done once per its own operation key; the same key again changes nothing.</item>
/// </list>
/// Each resource has one JSON file under <see cref="DirectoryPath"/>, replaced atomically on every change, so a host
/// killed mid-call leaves either the old file or the new one.
/// </remarks>
public sealed partial class ReservationLedger
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TimeProvider _timeProvider;

    public ReservationLedger(string directoryPath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        DirectoryPath = Path.GetFullPath(directoryPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The directory that holds one ledger file per resource.</summary>
    public string DirectoryPath { get; }

    /// <summary>A resource name is one file name: letters, digits, '-', '_' and '.', not starting with '.'.</summary>
    public static bool IsValidResource(string? resource) => resource is not null && ResourcePattern().IsMatch(resource);

    /// <summary>The path of a resource's ledger file.</summary>
    public string FilePath(string resource) =>
        IsValidResource(resource)
            ? Path.Combine(DirectoryPath, resource + ".json")
            : throw new ArgumentException($"'{resource}' is not a valid resource name.", nameof(resource));

    /// <summary>Reads a resource's ledger; a resource never touched has an empty one.</summary>
    public async Task<ResourceLedger> ReadAsync(string resource, CancellationToken cancellationToken = default)
    {
        var path = FilePath(resource);
        if (!File.Exists(path))
            return new ResourceLedger { Resource = resource };

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ResourceLedger>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException($"The ledger file {path} is empty.");
    }

    /// <summary>Reserves <paramref name="resource"/> for <paramref name="operationKey"/>.</summary>
    /// <param name="reject">The participant refuses the request for business reasons, with no effect.</param>
    public Task<ReserveResult> ReserveAsync(string resource, string operationKey, string workflowExecutionId, bool reject,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(resource, ledger =>
        {
            if (ledger.Tombstones.Any(tombstone => tombstone.OperationKey == operationKey))
                return new ReserveResult(ReserveStatus.Refused, null);

            if (reject)
                return new ReserveResult(ReserveStatus.Rejected, null);

            if (ledger.Reservations.FirstOrDefault(reservation => reservation.OperationKey == operationKey) is { } existing)
                return new ReserveResult(ReserveStatus.Repeated, existing.ReservationId);

            var reservationId = $"{resource}-{ledger.Reservations.Count + 1}";
            ledger.Reservations.Add(new ReservationRecord
            {
                ReservationId = reservationId,
                OperationKey = operationKey,
                WorkflowExecutionId = workflowExecutionId,
                Status = ReservationStatus.Reserved,
                ReservedAt = _timeProvider.GetUtcNow()
            });
            return new ReserveResult(ReserveStatus.Reserved, reservationId);
        }, cancellationToken);

    /// <summary>
    /// Releases the reservation that <paramref name="targetOperationKey"/> made, or the reservation
    /// <paramref name="reservationId"/> when no target key is given. The first <paramref name="failTimes"/> calls with one
    /// <paramref name="operationKey"/> fail before any effect, as an unavailable service would.
    /// </summary>
    /// <param name="reservationId">The reservation the forward step reported, when its response arrived. A release whose
    /// target made a different reservation, or none in this resource, is refused as a mismatch.</param>
    public Task<ReleaseResult> ReleaseAsync(string resource, string operationKey, string? targetOperationKey, string? reservationId,
        int failTimes, string workflowExecutionId, CancellationToken cancellationToken = default) =>
        ChangeAsync(resource, ledger =>
        {
            if (ledger.Releases.FirstOrDefault(release => release.OperationKey == operationKey) is { } done)
                return new ReleaseResult(ReleaseStatus.Repeated, done.ReservationId, 0);

            if (targetOperationKey is null && reservationId is null)
                return new ReleaseResult(ReleaseStatus.NoTarget, null, 0);

            var failed = ledger.FailedReleaseAttempts.GetValueOrDefault(operationKey) ?? [];
            if (failed.Count < failTimes)
            {
                failed.Add(_timeProvider.GetUtcNow());
                ledger.FailedReleaseAttempts[operationKey] = failed;
                return new ReleaseResult(ReleaseStatus.Unavailable, null, failed.Count);
            }

            var reservation = targetOperationKey is not null
                ? ledger.Reservations.FirstOrDefault(candidate => candidate.OperationKey == targetOperationKey)
                : ledger.Reservations.FirstOrDefault(candidate => candidate.ReservationId == reservationId);
            if (reservationId is not null && reservation?.ReservationId != reservationId)
                return new ReleaseResult(ReleaseStatus.Mismatch, reservation?.ReservationId, 0);

            var now = _timeProvider.GetUtcNow();
            ReleaseStatus status;
            if (reservation is null)
            {
                // The reservation is not here yet: remember its key so it is refused when it arrives.
                ledger.Tombstones.Add(new TombstoneRecord { OperationKey = targetOperationKey!, ReleasedBy = operationKey, At = now });
                status = ReleaseStatus.Tombstoned;
            }
            else if (reservation.Status == ReservationStatus.Released)
            {
                status = ReleaseStatus.AlreadyReleased;
            }
            else
            {
                reservation.Status = ReservationStatus.Released;
                reservation.ReleasedAt = now;
                reservation.ReleasedBy = operationKey;
                status = ReleaseStatus.Released;
            }

            ledger.Releases.Add(new ReleaseRecord
            {
                OperationKey = operationKey,
                TargetOperationKey = targetOperationKey,
                ReservationId = reservation?.ReservationId,
                WorkflowExecutionId = workflowExecutionId,
                Result = status,
                At = now
            });
            return new ReleaseResult(status, reservation?.ReservationId, 0);
        }, cancellationToken);

    // Read, change and replace one resource's file under a lock, so concurrent runs in one process never lose a write.
    private async Task<T> ChangeAsync<T>(string resource, Func<ResourceLedger, T> change, CancellationToken cancellationToken)
    {
        var path = FilePath(resource);
        var fileLock = FileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var ledger = await ReadAsync(resource, cancellationToken);
            var before = JsonSerializer.Serialize(ledger, JsonOptions);
            var result = change(ledger);
            var after = JsonSerializer.Serialize(ledger, JsonOptions);
            if (before != after)
            {
                Directory.CreateDirectory(DirectoryPath);
                var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
                await File.WriteAllTextAsync(temporary, after, cancellationToken);
                File.Move(temporary, path, overwrite: true);
            }

            return result;
        }
        finally
        {
            fileLock.Release();
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex ResourcePattern();
}
