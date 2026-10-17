using CShells.Features;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench.Tests.BackgroundActivities;

[ShellFeature("BackgroundActivityWitness", DependsOn = new object[] { "BackgroundActivities", "WorkflowsRuntimeEntityFrameworkCore" })]
public sealed class BackgroundActivityWitnessFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IActivityReconciliationSource, BackgroundProbeCatalog>();
        var provider = services.Last(service => service.ServiceType == typeof(IBackgroundActivityWorkStore));
        services.Remove(provider);
        services.AddScoped<IBackgroundActivityWorkStore>(scope => new ReceiptDeliveryBarrier(
            (IBackgroundActivityWorkStore)(provider.ImplementationFactory?.Invoke(scope) ??
                ActivatorUtilities.CreateInstance(scope, provider.ImplementationType!))));
    }
}

// Feed packages contribute their catalog explicitly; the host's CLR source scans only its own binary folder.
public sealed class BackgroundProbeCatalog : IActivityReconciliationSource
{
    public string SourceId => "BackgroundActivityWitness";
    public string SourceKind => "OwnedProcessTest";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>([
            new(null, "1.0.0", typeof(BackgroundProbe).FullName!, "Background probe", "Tests", null,
                "elsa.clr-activity", "1", WellKnownRuntimeActivityConsumers.ClrActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(BackgroundProbe))),
                [Input(nameof(BackgroundProbe.DirectoryPath), "String"), Input(nameof(BackgroundProbe.Key), "String"),
                    Input(nameof(BackgroundProbe.WaitForRelease), "Boolean")], [], [])
        ]);

    private static InputDefinition Input(string name, string alias) =>
        new(name, name, new TypeReference(alias), null, name, null, IsNullable: false, IsRequired: true);
}

/// <summary>A test-only external body whose barrier and invocation journal survive termination of its host.</summary>
[BackgroundExecutionSafe]
[ActivityOutcome("Done")]
public sealed class BackgroundProbe : Activity<string>
{
    [ActivityInput] public string DirectoryPath { get; set; } = null!;
    [ActivityInput] public string Key { get; set; } = null!;
    [ActivityInput] public bool WaitForRelease { get; set; } = true;

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        Directory.CreateDirectory(DirectoryPath);
        // Each body writes a separate key. A second line after restart proves physical re-execution rather than
        // merely a fresh in-process coordinator. The logical invocation and attempt must remain stable.
        await File.AppendAllTextAsync(Path.Combine(DirectoryPath, Key + ".started"),
            $"{Environment.ProcessId}|{context.InvocationId}|{context.AttemptId}{Environment.NewLine}", context.CancellationToken);
        try
        {
            while (WaitForRelease && !File.Exists(Path.Combine(DirectoryPath, Key + ".release")))
                await Task.Delay(25, context.CancellationToken);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            await File.WriteAllTextAsync(Path.Combine(DirectoryPath, Key + ".cancelled"), context.InvocationId);
            throw;
        }
        return ActivityTransition.Complete(Key);
    }
}
