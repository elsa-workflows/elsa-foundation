using Elsa.Primitives.Contracts;

namespace Elsa.Primitives.Identity;

/// <summary>
/// Holds the shared, monotonic state for Snowflake identifier generation: the configured worker id, the last observed
/// timestamp, and the per-millisecond sequence counter.
/// </summary>
/// <remarks>
/// This type is registered as a singleton so that the sequence counter is shared across the whole process, while the
/// <see cref="SnowflakeIdentityGenerator"/> that consumes it remains scoped and supplies the (scoped) clock per call.
/// All mutation is guarded by a lock; identifier generation is therefore serialized but allocation-free and fast.
/// </remarks>
public sealed class SnowflakeIdentitySequence
{
    private const int WorkerIdBits = 10;
    private const int SequenceBits = 12;
    private const long MaxWorkerId = (1L << WorkerIdBits) - 1; // 1023
    private const long SequenceMask = (1L << SequenceBits) - 1; // 4095

    private readonly long _workerId;
    private readonly long _epochMs;
    private readonly Lock _lock = new();
    private long _lastTimestamp = -1;
    private long _sequence;

    public SnowflakeIdentitySequence(SnowflakeIdentityGeneratorOptions options)
    {
        if (options.WorkerId is < 0 or > MaxWorkerId)
            throw new ArgumentOutOfRangeException(nameof(options), options.WorkerId, $"WorkerId must be between 0 and {MaxWorkerId}.");

        _workerId = options.WorkerId;
        _epochMs = options.Epoch.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Produces the next 63-bit identifier value using the supplied clock for the timestamp component.
    /// </summary>
    public long Next(ISystemClock systemClock)
    {
        lock (_lock)
        {
            var timestamp = systemClock.UtcNow.ToUnixTimeMilliseconds();

            if (timestamp < _lastTimestamp)
                throw new InvalidOperationException($"Clock moved backwards by {_lastTimestamp - timestamp} ms; refusing to generate an identifier.");

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & SequenceMask;

                // Sequence exhausted for this millisecond: spin until the next one.
                if (_sequence == 0)
                    timestamp = WaitNextMillisecond(systemClock, _lastTimestamp);
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = timestamp;

            return ((timestamp - _epochMs) << (WorkerIdBits + SequenceBits))
                   | (_workerId << SequenceBits)
                   | _sequence;
        }
    }

    private static long WaitNextMillisecond(ISystemClock systemClock, long lastTimestamp)
    {
        long timestamp;
        do
        {
            timestamp = systemClock.UtcNow.ToUnixTimeMilliseconds();
        } while (timestamp <= lastTimestamp);

        return timestamp;
    }
}
