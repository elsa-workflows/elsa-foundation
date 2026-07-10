namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

internal static class TestWait
{
    /// <summary>Polls <paramref name="probe"/> until it passes or the timeout elapses.</summary>
    public static async Task<bool> ForConditionAsync(Func<bool> probe, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (probe())
                return true;
            await Task.Delay(25);
        }

        return probe();
    }
}
