namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>Waits for something a test does not control the timing of, and fails with what the caller says is worth knowing.</summary>
internal static class Polling
{
    /// <summary>Polls <paramref name="condition"/> every <paramref name="interval"/> until it holds, failing with <paramref name="failure"/> after <paramref name="patience"/>.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan patience, TimeSpan interval, Func<Task<string>> failure)
    {
        var deadline = DateTimeOffset.UtcNow + patience;
        while (!await condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"Not met within {patience}. {await failure()}");
            await Task.Delay(interval);
        }
    }

    /// <inheritdoc cref="UntilAsync(Func{Task{bool}}, TimeSpan, TimeSpan, Func{Task{string}})"/>
    public static Task UntilAsync(Func<Task<bool>> condition, TimeSpan patience, TimeSpan interval, Func<string> failure) =>
        UntilAsync(condition, patience, interval, () => Task.FromResult(failure()));

    /// <inheritdoc cref="UntilAsync(Func{Task{bool}}, TimeSpan, TimeSpan, Func{Task{string}})"/>
    public static Task UntilAsync(Func<bool> condition, TimeSpan patience, TimeSpan interval, string failure = "") =>
        UntilAsync(() => Task.FromResult(condition()), patience, interval, () => failure);
}
