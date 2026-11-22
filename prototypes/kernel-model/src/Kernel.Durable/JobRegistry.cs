using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Kernel.Durable;

internal readonly record struct JobRegistration([property: JsonConverter(typeof(DurableKeyJson))] string Key, long Generation);

internal static class JobRegistry
{
    private sealed record Registry(List<JobRegistration> Entries);

    // The owner may inspect the same immutable checkpoint for every eligible job on a pass.
    private static readonly ConditionalWeakTable<InstanceRow, Dictionary<string, long>> Current = new();

    internal static List<JobRegistration> Read(string? jobs) => jobs is null ? []
        : jobs.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal) ? Json.Read<Registry>(jobs).Entries
        : [.. DurableKeyJson.ReadKeys(jobs).Select(key => new JobRegistration(key, 0))];

    internal static string? Write(List<JobRegistration> jobs)
    {
        if (jobs.Count == 0) return null;
        return jobs.Any(job => job.Generation != 0)
            ? Json.Write(new Registry(jobs))
            : DurableKeyJson.WriteKeys(jobs.Select(job => job.Key));
    }

    internal static long? Generation(InstanceRow? row, string key) =>
        row?.Jobs is not null && Current.GetValue(row,
            state => Read(state.Jobs).ToDictionary(job => job.Key, job => job.Generation, StringComparer.Ordinal))
            .TryGetValue(key, out var generation)
            ? generation : null;
}
