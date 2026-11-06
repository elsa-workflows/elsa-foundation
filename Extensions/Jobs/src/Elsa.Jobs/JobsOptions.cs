using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Jobs.Core;

namespace Elsa.Jobs;

/// <summary>Host-owned configuration copied at registration; explicit worker scopes never come from submitted input.</summary>
public sealed class JobsOptions
{
    public bool EnableWorker { get; set; }
    /// <summary>Runs the independent schedule loop when the worker is enabled; producer-only registration never starts it.</summary>
    public bool MaterializeSchedules { get; set; } = true;
    public IList<JobScope> WorkerScopes { get; } = new List<JobScope>();
    public IList<string> Queues { get; } = new List<string> { "default" };
    public int MaximumConcurrentAttempts { get; set; } = 2;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RenewalInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Maximum occurrence admissions requested per provider call, from 1 through 1000.</summary>
    public int MaximumOccurrencesPerMaterialization { get; set; } = 32;
    /// <summary>Delay after every bounded schedule call, from 10 milliseconds through one minute.</summary>
    public TimeSpan MaterializationInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Cooperative I/O bound for a schedule call, distinct from physical attempt lease/timeout policy.</summary>
    public TimeSpan MaterializationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Maximum complete schema/value envelope bytes; defaults to 1 MiB and may only be lowered.</summary>
    public int MaximumPayloadBytes { get; set; } = JobPayloadLimits.MaximumBytes;
    public int MaximumProgressBytes { get; set; } = JobPayloadLimits.MaximumProgressBytes;
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };
}

internal sealed record JobRuntimeSettings(bool EnableWorker, IReadOnlyList<JobScope> WorkerScopes,
    IReadOnlyList<string> Queues, int MaximumConcurrentAttempts, TimeSpan PollInterval, TimeSpan LeaseDuration,
    TimeSpan RenewalInterval, TimeSpan ShutdownGracePeriod, int MaximumPayloadBytes, int MaximumProgressBytes,
    JsonSerializerOptions JsonSerializerOptions, bool MaterializeSchedules, int MaximumOccurrencesPerMaterialization,
    TimeSpan MaterializationInterval, TimeSpan MaterializationTimeout)
{
    internal static JobRuntimeSettings Capture(JobsOptions options)
    {
        if (options.MaximumConcurrentAttempts is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(options.MaximumConcurrentAttempts));
        if (options.PollInterval < TimeSpan.FromMilliseconds(10) || options.PollInterval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options.PollInterval));
        if (options.LeaseDuration < TimeSpan.FromSeconds(1) || options.LeaseDuration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(options.LeaseDuration));
        if (options.RenewalInterval < TimeSpan.FromMilliseconds(100) || options.RenewalInterval > options.LeaseDuration / 3)
            throw new ArgumentOutOfRangeException(nameof(options.RenewalInterval));
        if (options.ShutdownGracePeriod < TimeSpan.Zero || options.ShutdownGracePeriod > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(options.ShutdownGracePeriod));
        if (options.MaximumOccurrencesPerMaterialization is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumOccurrencesPerMaterialization));
        if (options.MaterializationInterval < TimeSpan.FromMilliseconds(10) || options.MaterializationInterval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options.MaterializationInterval));
        if (options.MaterializationTimeout < TimeSpan.FromMilliseconds(100) || options.MaterializationTimeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(options.MaterializationTimeout));
        if (options.MaximumPayloadBytes is < 1 or > JobPayloadLimits.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(options.MaximumPayloadBytes));
        if (options.MaximumProgressBytes is < 1 or > JobPayloadLimits.MaximumProgressBytes) throw new ArgumentOutOfRangeException(nameof(options.MaximumProgressBytes));
        ArgumentNullException.ThrowIfNull(options.JsonSerializerOptions);
        var workerScopes = options.WorkerScopes.ToArray();
        foreach (var scope in workerScopes) JobValidation.Scope(scope);
        if (workerScopes.Distinct().Count() != workerScopes.Length) throw new ArgumentException("Worker scopes must be distinct.", nameof(options));
        if (options.EnableWorker && workerScopes.Length == 0) throw new ArgumentException("An enabled worker requires explicit trusted scopes.", nameof(options));
        var queues = options.Queues.ToArray();
        if (queues.Length is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(options.Queues));
        foreach (var queue in queues) JobValidation.Identity(queue);
        if (queues.Distinct(StringComparer.Ordinal).Count() != queues.Length) throw new ArgumentException("Queues must be distinct.", nameof(options));
        var json = new JsonSerializerOptions(options.JsonSerializerOptions);
        json.MakeReadOnly(populateMissingResolver: true);
        return new(options.EnableWorker, Array.AsReadOnly(workerScopes), Array.AsReadOnly(queues), options.MaximumConcurrentAttempts,
            options.PollInterval, options.LeaseDuration, options.RenewalInterval, options.ShutdownGracePeriod,
            options.MaximumPayloadBytes, options.MaximumProgressBytes, json, options.MaterializeSchedules,
            options.MaximumOccurrencesPerMaterialization, options.MaterializationInterval, options.MaterializationTimeout);
    }
}
