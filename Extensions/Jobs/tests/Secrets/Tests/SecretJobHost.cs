using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Elsa.Jobs.Secrets;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Jobs.Secrets.Tests;

internal sealed class SecretJobHost(IHost host, string directory, bool ownsDirectory, SecretHandlerControl control, CapturedJobLogs logs) : IAsyncDisposable
{
    internal const string Partition = "jobs-secret-controls";
    internal const string Alpha = "tenant-alpha";
    internal const string Beta = "tenant-beta";
    internal const string Missing = "tenant-missing";
    internal const string AlphaValue = "ALPHA_JOB_SECRET_CANARY_1840";
    internal const string BetaValue = "BETA_JOB_SECRET_CANARY_1840";
    internal const string ProtectedValue = "PROTECTED_JOB_INPUT_CANARY_1840";
    internal static readonly SecretReference Reference = new("Jobs.Shared", SecretTypeNames.Text, "reports");
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly JobHandlerSupport Support = new(new("secret.report", "1"), new("secret.report.input", 1), new("secret.report.result", 1), "secret-report-control-v1");
    internal static readonly JobHandlerSupport NullSupport = new(new("null.report", "1"), new("null.report.input", 1), Support.ResultSchema, "null-report-control-v1");
    internal SecretHandlerControl Control => control;
    internal CapturedJobLogs Logs => logs;
    internal string StorageDirectory => directory;

    internal static async Task<SecretJobHost> StartAsync(string? storageDirectory = null, bool worker = false, bool installSecrets = true,
        SecretHandlerControl? control = null, Action<IServiceCollection>? configure = null,
        Action<JobSecretsOptions>? additionalDeclarations = null, Action<IServiceCollection, string>? configureStorage = null)
    {
        var owned = storageDirectory is null; var directory = storageDirectory ?? Directory.CreateTempSubdirectory("elsa-jobs-secrets-").FullName;
        Directory.CreateDirectory(directory); var builder = Host.CreateApplicationBuilder(); builder.Logging.ClearProviders();
        var logs = new CapturedJobLogs(); builder.Logging.AddProvider(logs);
        builder.Services.AddJobsEntityFrameworkCore(options => options.ConnectionString = "Data Source=" + Path.Combine(directory, "jobs.db") + ";Pooling=False");
        if (installSecrets)
        {
            builder.Services.AddSecrets(); builder.Services.Configure<SecretsOptions>(options => options.EncryptionKey = "owned-jobs-secret-test-key");
            builder.Services.AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=" + Path.Combine(directory, "secrets.db") + ";Pooling=False" });
        }
        builder.Services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
        AddDeclarations(builder.Services, additionalDeclarations);
        var observations = control ?? new SecretHandlerControl(); builder.Services.AddSingleton(observations);
        builder.Services.AddElsaJobs(options =>
        {
            options.EnableWorker = worker; options.MaterializeSchedules = false; options.MaximumConcurrentAttempts = 2;
            options.WorkerScopes.Add(new(Partition, Alpha)); options.WorkerScopes.Add(new(Partition, Beta)); options.WorkerScopes.Add(new(Partition, Missing));
            options.Queues.Clear(); options.Queues.Add("reports"); options.PollInterval = TimeSpan.FromMilliseconds(20);
            options.LeaseDuration = TimeSpan.FromSeconds(10); options.RenewalInterval = TimeSpan.FromSeconds(2); options.ShutdownGracePeriod = TimeSpan.FromSeconds(2);
        });
        if (worker) { builder.Services.AddJobHandler<SecretReportHandler, SecretJobInput, JsonElement>(Support); builder.Services.AddJobHandler<NullReportHandler, JsonElement, JsonElement>(NullSupport); }
        configureStorage?.Invoke(builder.Services, directory); configure?.Invoke(builder.Services); var host = builder.Build();
        try { await host.StartAsync(); return new(host, directory, owned, observations, logs); }
        catch { await ((IAsyncDisposable)host).DisposeAsync(); if (owned) Directory.Delete(directory, true); throw; }
    }
    internal static void AddDeclarations(IServiceCollection services, Action<JobSecretsOptions>? additionalDeclarations = null) => services.AddJobSecrets(options =>
    {
        options.Declarations.Add(new(Support.Handler, Support.InputSchema, [new("/credential", [Reference])], ["/protected"]));
        options.Declarations.Add(new(NullSupport.Handler, NullSupport.InputSchema, [new("/credential", [Reference], required: false)]));
        additionalDeclarations?.Invoke(options);
    });
    internal async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> action)
    { await using var scope = host.Services.CreateAsyncScope(); return await action(scope.ServiceProvider); }
    internal Task<JobAcceptance> SubmitAsync(string tenant, string key, object? input = null, bool nullHandler = false) => UseAsync(async services =>
        await services.GetRequiredService<IJobClient>().SubmitAsync(new(Partition, tenant), new(key, Request(input, nullHandler))));
    internal static JobRequest Request(object? input = null, bool nullHandler = false)
    {
        var support = nullHandler ? NullSupport : Support;
        var value = nullHandler ? input : input ?? new SecretJobInput(Reference, ProtectedValue, "safe");
        return new(support.Handler, new(support.InputSchema, JsonSerializer.SerializeToElement(value, Json)), support.ResultSchema, "reports",
            new(new(2, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), 1, 0, true), TimeSpan.FromSeconds(30), TimeSpan.FromDays(30)), null, null);
    }
    internal Task<JobSnapshot?> ReadAsync(string tenant, string id) => UseAsync(async services => await services.GetRequiredService<IJobQueries>().FindAsync(new(Partition, tenant), id));
    internal async Task<JobSnapshot> WaitAsync(string tenant, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested(); var item = await ReadAsync(tenant, id);
            if (item?.State == JobState.Terminal) return item;
            await Task.Delay(25, timeout.Token);
        }
    }
    internal Task<SecretMetadata> SeedAsync(string tenant, string value, string scope = "reports") => UseAsync(async services =>
        await services.GetRequiredService<ISecretManager>().CreateAsync(tenant, new() { Name = Reference.Name, TypeName = SecretTypeNames.Text,
            Scope = scope, StoreName = SecretStoreNames.Encrypted, Value = value }));
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await host.StopAsync(timeout.Token); } finally { await ((IAsyncDisposable)host).DisposeAsync(); }
        if (ownsDirectory) Directory.Delete(directory, recursive: true);
    }
}

internal sealed record SecretJobInput(SecretReference Credential, string? Protected, string Mode);
internal sealed record SecretObservation(string Tenant, string JobId, int Attempt, string Value, JobHandlerIdentity? Handler);
internal sealed class SecretHandlerControl
{
    internal readonly ConcurrentQueue<SecretObservation> Observations = new();
    internal bool RequireConcurrentTenants;
    private readonly ConcurrentDictionary<string, byte> _arrived = new(StringComparer.Ordinal);
    internal readonly TaskCompletionSource BothTenants = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource FirstRetry = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource ContinueRetry = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async Task ObserveAsync(string value, JobExecutionContext context, string mode, CancellationToken ct)
    {
        Observations.Enqueue(new(context.Scope.TenantId!, context.JobId, context.AttemptNumber, value, context.Handler));
        if (RequireConcurrentTenants)
        {
            _arrived.TryAdd(context.Scope.TenantId!, 0); if (_arrived.Count >= 2) BothTenants.TrySetResult();
            await BothTenants.Task.WaitAsync(ct);
        }
        if (mode == "retry" && context.AttemptNumber == 1) { FirstRetry.TrySetResult(); await ContinueRetry.Task.WaitAsync(ct); }
    }
}
internal sealed class SecretReportHandler : IJobHandler<SecretJobInput, JsonElement>
{
    private readonly string _value; private readonly SecretHandlerControl _control;
    public SecretReportHandler(IJobSecrets secrets, SecretHandlerControl control)
    {
        // Deliberately resolves in construction: the actual worker must already have bound this scope.
        _value = secrets.ResolveAsync(SecretJobHost.Reference).AsTask().GetAwaiter().GetResult(); _control = control;
    }
    public async ValueTask<JsonElement> ExecuteAsync(SecretJobInput input, JobExecutionContext context, CancellationToken cancellationToken)
    {
        await _control.ObserveAsync(_value, context, input.Mode, cancellationToken);
        if (input.Mode is "exception" or "retry" && context.AttemptNumber == 1) throw new InvalidOperationException(_value);
        if (input.Mode == "progress-message") await context.Progress.ReportAsync(new(.5, _value, null), cancellationToken);
        if (input.Mode == "progress-data") await context.Progress.ReportAsync(new(.5, "Working", new(new("safe-progress", 1), JsonSerializer.SerializeToElement(new { Value = _value }))), cancellationToken);
        if (input.Mode == "progress-protected") await context.Progress.ReportAsync(new(.5, input.Protected, null), cancellationToken);
        var result = input.Mode == "result-secret" ? _value : input.Mode == "result-protected" ? input.Protected : "safe-result";
        return JsonSerializer.SerializeToElement(new { Result = result, Count = 42 });
    }
}
internal sealed class NullReportHandler : IJobHandler<JsonElement, JsonElement>
{
    public ValueTask<JsonElement> ExecuteAsync(JsonElement input, JobExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(JsonSerializer.SerializeToElement(new { NullInput = input.ValueKind == JsonValueKind.Null, EmptyObject = input.ValueKind == JsonValueKind.Object && !input.EnumerateObject().Any() }));
}
internal sealed class CapturedJobLogs : ILoggerProvider
{
    internal readonly ConcurrentQueue<string> Entries = new();
    public ILogger CreateLogger(string categoryName) => new CapturedLogger(Entries);
    public void Dispose() { }
    private sealed class CapturedLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(formatter(state, exception) + (exception?.ToString() ?? ""));
    }
}
