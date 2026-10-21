using CShells.Features;
using Elsa.Connections.Core.Contracts;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Elsa.Tasks.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "EtlRuntime", DisplayName = "ETL Runtime", Description = "Bounded execution support for published finite file and HTTP ETL pipelines.", DependsOn = new object[] { "Tasks" })]
public class EtlRuntimeFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Snapshot root", Description = "Absolute host-approved root for bounded ETL source and lookup snapshots.", Category = "Storage")]
    public string SnapshotRoot { get; set; } = string.Empty;

    [ManifestSetting(DisplayName = "File bindings", Description = "Trusted, revision-pinned host files available to ETL definitions.", Category = "Bindings")]
    public List<EtlHostFileBindingOptions> FileBindings { get; set; } = [];

    [ManifestSetting(DisplayName = "HTTP bindings", Description = "Trusted scoped HTTP endpoints, revision policies and optional connection aliases.", Category = "Bindings")]
    public List<EtlHostHttpBindingOptions> HttpBindings { get; set; } = [];

    [ManifestSetting(DisplayName = "Maximum concurrent attempts", Description = "Maximum actual ETL attempt tasks that may hold process-local capacity.", Category = "Capacity")]
    public int MaxConcurrentAttempts { get; set; } = 2;

    [ManifestSetting(DisplayName = "Maximum live records", Description = "Shared logical record allowance held by running ETL attempts.", Category = "Capacity")]
    public long MaxLiveRecords { get; set; } = 20_000;

    [ManifestSetting(DisplayName = "Maximum live input bytes", Description = "Shared input-byte allowance held by running ETL attempts.", Category = "Capacity")]
    public long MaxLiveInputBytes { get; set; } = 32L * 1024 * 1024;

    [ManifestSetting(DisplayName = "Maximum live lookup bytes", Description = "Shared lookup-byte allowance held by running ETL attempts.", Category = "Capacity")]
    public long MaxLiveLookupBytes { get; set; } = 128L * 1024 * 1024;

    [ManifestSetting(DisplayName = "Maximum live output bytes", Description = "Shared output-byte allowance held by running ETL attempts.", Category = "Capacity")]
    public long MaxLiveOutputBytes { get; set; } = 64L * 1024 * 1024;

    [ManifestSetting(DisplayName = "Maximum lookup records", Description = "Maximum lookup rows materialized for one ETL attempt.", Category = "Capacity")]
    public long MaxLookupRecords { get; set; } = 100_000;

    [ManifestSetting(DisplayName = "Maximum lookup cells", Description = "Maximum lookup rows times schema columns materialized per attempt; bounds sparse-row object expansion independently of file bytes.", Category = "Capacity")]
    public long MaxLookupCells { get; set; } = 1_000_000;

    [ManifestSetting(DisplayName = "Maximum partitions", Description = "Maximum partition count permitted by one published ETL pipeline.", Category = "Capacity")]
    public int MaxPartitions { get; set; } = 128;

    [ManifestSetting(DisplayName = "Maximum snapshot bytes", Description = "Maximum size of each host-approved ETL snapshot.", Category = "Capacity")]
    public long MaxSnapshotBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    [ManifestSetting(DisplayName = "Maximum retained output bytes", Description = "Host ceiling for route-body bytes retained across output plans for one ETL run.", Category = "Capacity")]
    public long MaxRetainedOutputBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    [ManifestSetting(DisplayName = "HTTP limits", Description = "Host ceilings for finite HTTP pages, retention and retries; independent of authored limits.", Category = "Capacity")]
    public EtlHttpHostLimits HttpLimits { get; set; } = new();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = EtlRuntimeOptions.Create(this);

        services.AddSingleton(options);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IFileChunkPublisherFactory, FileChunkPublisherFactory>();
        services.TryAddSingleton<EtlAttemptBudget>(_ => new EtlAttemptBudget(options.MaxConcurrentAttempts, options.Capacity));
        services.AddScoped(_ => new HostFileBindings(options.FileBindings));
        services.TryAddScoped<IEtlBindingCatalog>(provider => provider.GetRequiredService<HostFileBindings>());
        services.AddScoped(provider => options.HttpBindings.WithConnections(
            provider.GetService<IConnectionResolver>(),
            provider.GetService<IConnectionTypeRegistry>(),
            provider.GetService<IAuthenticationSchemeRegistry>(),
            provider.GetServices<IHttpRequestAuthenticator>()));
        services.AddScoped(provider => new HostConnectorBindings(
            provider.GetRequiredService<HostFileBindings>(), provider.GetRequiredService<HostHttpBindings>(), options.MaxPartitions));
        services.TryAddScoped<IEtlConnectorBindingCatalog>(provider => provider.GetRequiredService<HostConnectorBindings>());
        services.TryAddScoped<IEtlConnectorBindingResolver>(provider => provider.GetRequiredService<HostConnectorBindings>());
        services.AddScoped<EtlExecutableReader>();
        services.AddScoped<EtlConnectorExecutableReader>();
        services.AddScoped<EtlBatchProcessor>();
        services.AddScoped(provider => new EtlFilePreparer(
            provider.GetRequiredService<HostFileBindings>(),
            options.SnapshotRoot,
            provider.GetRequiredService<IFileChunkPublisherFactory>()));
        services.AddScoped(provider => new EtlConnectorPreparer(
            provider.GetRequiredService<HostFileBindings>(), options.SnapshotRoot,
            provider.GetRequiredService<IFileChunkPublisherFactory>()));
        services.AddScoped<EtlHttpTransport>();
        // Resolve this optional execution path only after the worker discovers a connector-capable store.
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<EtlConnectorExecution>(provider));
        services.AddScoped<EtlExecution>();
        services.AddScoped<EtlAcceptedOutputMaterializer>();
        services.AddScoped<IEtlRunAdmissionService>(provider => provider.GetRequiredService<EtlExecution>());
        // The generic host owns IHostedService; CShells owns IBackgroundTask through Tasks.
        // Both paths resolve the same idempotently started worker for this provider generation.
        services.AddSingleton<EtlRuntimeWorker>();
        services.AddSingleton<IBackgroundTask>(provider => provider.GetRequiredService<EtlRuntimeWorker>());
        services.AddHostedService(provider => provider.GetRequiredService<EtlRuntimeWorker>());
    }
}

/// <summary>A trusted host file revision available to ETL runtime requests.</summary>
public sealed class EtlHostFileBindingOptions
{
    /// <summary>The trusted tenant identifier, or null for the explicit null-tenant scope.</summary>
    public string? TenantId { get; set; }

    /// <summary>The required opaque persistence partition for this binding.</summary>
    public string PersistencePartition { get; set; } = string.Empty;

    /// <summary>The bounded alias referenced by a published ETL pipeline.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>The file role: Source, Lookup, MainOutput, or ErrorOutput.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The immutable host revision identifier.</summary>
    public string Revision { get; set; } = string.Empty;

    /// <summary>The absolute host-controlled path for the file.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether this revision is offered for new requests. Defaults to true; set false for retained historical revisions.</summary>
    public bool IsCurrent { get; set; } = true;

    /// <summary>The required SHA-256 for immutable source and lookup inputs.</summary>
    public string? ExpectedSha256 { get; set; }

    /// <summary>Whether a source or lookup input is immutable.</summary>
    public bool ImmutableInput { get; set; }

    /// <summary>Whether output publication is atomic and create-only.</summary>
    public bool AtomicPublication { get; set; }
}
