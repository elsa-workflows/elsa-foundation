using CShells.Features;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Rules.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Rules")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "RulesRuntime", DisplayName = "Rules Runtime",
    Description = "Durable finite rule inference with bounded background attempts.", DependsOn = new object[] { "Tasks" })]
public class RulesRuntimeFeature : IShellFeature
{
    /// <summary>Absolute path to the deployment receipt produced for the actual PackageReference host.</summary>
    [ManifestSetting(DisplayName = "Deployment receipt", Description = "Absolute host-owned deployment receipt path.", Category = "Deployment")]
    public string DeploymentReceiptPath { get; set; } = string.Empty;

    /// <summary>Absolute path to that host's active dependency manifest.</summary>
    [ManifestSetting(DisplayName = "Host dependency manifest", Description = "Absolute path to the deployed host's active deps.json.", Category = "Deployment")]
    public string HostDepsPath { get; set; } = string.Empty;

    /// <summary>Absolute directory containing the deployed host and its selected assemblies.</summary>
    [ManifestSetting(DisplayName = "Host root", Description = "Absolute directory containing the deployed host and semantic assets.", Category = "Deployment")]
    public string HostRoot { get; set; } = AppContext.BaseDirectory;

    /// <summary>Maximum number of actual attempt tasks in this host provider.</summary>
    [ManifestSetting(DisplayName = "Maximum concurrent attempts", Description = "Bound on actual running Rules attempts, from 1 through 64.", Category = "Runtime")]
    public int MaxConcurrentAttempts { get; set; } = 2;

    /// <summary>Delay between attempts when no progress is made.</summary>
    [ManifestSetting(DisplayName = "Poll interval milliseconds", Description = "Idle or failed attempt delay, from 10 through 60000 milliseconds.", Category = "Runtime")]
    public int PollIntervalMilliseconds { get; set; } = 250;

    /// <summary>Disable new admissions during rollback while preserving replay, cancellation and recovery.</summary>
    [ManifestSetting(DisplayName = "Accept new invocations", Description = "Disable new admission while retaining existing invocation replay and recovery.", Category = "Runtime")]
    public bool AcceptNewInvocations { get; set; } = true;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (MaxConcurrentAttempts is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentAttempts));
        if (PollIntervalMilliseconds is < 10 or > 60_000) throw new ArgumentOutOfRangeException(nameof(PollIntervalMilliseconds));
        var options = new RulesRuntimeOptions(MaxConcurrentAttempts, TimeSpan.FromMilliseconds(PollIntervalMilliseconds));
        var acceptNewInvocations = AcceptNewInvocations;
        var receipt = DeploymentReceiptPath;
        var deps = HostDepsPath;
        var root = HostRoot;
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRuleImplementationIdentitySource>(_ => new RuleDeploymentIdentitySource(deps, receipt, root));
        services.TryAddScoped<IRuleInvocationFactory, RuleInvocationFactory>();
        services.TryAddScoped<IRuleAdmissionValidator>(provider =>
            new RuleAdmissionValidator(provider.GetRequiredService<IRuleImplementationIdentitySource>(), acceptNewInvocations));
        services.TryAddScoped<RuleRoundEvaluator>();
        services.TryAddScoped<IRuleRoundValidator>(provider => provider.GetRequiredService<RuleRoundEvaluator>());
        services.AddScoped<RuleExecution>();
        services.AddSingleton<RulesRuntimeWorker>();
        services.AddSingleton<IBackgroundTask>(provider => provider.GetRequiredService<RulesRuntimeWorker>());
        services.AddHostedService(provider => provider.GetRequiredService<RulesRuntimeWorker>());
    }
}

internal sealed record RulesRuntimeOptions(int MaxConcurrentAttempts, TimeSpan PollInterval);
