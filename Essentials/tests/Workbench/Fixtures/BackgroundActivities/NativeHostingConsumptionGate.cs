namespace Elsa.Workbench.Tests.BackgroundActivities;

/// <summary>
/// Holds this test host's receipt and transport consumption without suspending its database or membership threads.
/// One host-wide lock makes the acknowledgement follow every read/lease already in flight across scoped decorators.
/// </summary>
internal sealed class NativeHostingConsumptionGate(string journalDirectory, string role) : IDisposable
{
    private readonly SemaphoreSlim _reads = new(1, 1);
    private string? _acknowledgedToken;

    public async ValueTask<T> ReadAsync<T>(Func<ValueTask<T>> read, T heldResult, CancellationToken cancellationToken)
    {
        await _reads.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var holdPath = Path.Combine(journalDirectory, $"hold-consumption-{role}");
            if (!File.Exists(holdPath))
                return await read();

            var token = await File.ReadAllTextAsync(holdPath, cancellationToken);
            if (!StringComparer.Ordinal.Equals(_acknowledgedToken, token))
            {
                await BackgroundWitnessJournal.WriteJsonAtomicallyAsync(
                    Path.Combine(journalDirectory, $"consumption-held.{role}.json"), new
                    {
                        Stage = "consumption-held",
                        Role = role,
                        ProcessId = Environment.ProcessId,
                        RecordedAt = DateTimeOffset.UtcNow,
                        HoldToken = token
                    }, cancellationToken, overwrite: true);
                _acknowledgedToken = token;
            }

            return heldResult;
        }
        finally
        {
            _reads.Release();
        }
    }

    public void Dispose() => _reads.Dispose();
}
