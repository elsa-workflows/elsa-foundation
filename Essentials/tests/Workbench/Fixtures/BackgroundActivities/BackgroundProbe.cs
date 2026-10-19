using System.Text.Json;
using CShells.Features;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workbench.Tests.BackgroundActivities;

[ShellFeature("BackgroundActivityWitness", DependsOn = new object[] { "BackgroundActivities", "WorkflowsRuntimeEntityFrameworkCore" })]
public class BackgroundActivityWitnessFeature : IShellFeature
{
    public string? JournalDirectory { get; set; }
    public string Role { get; set; } = "standalone";

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var journal = JournalDirectory ?? Path.Combine(Environment.CurrentDirectory, "background-witness");
        if (!Path.IsPathFullyQualified(journal))
            throw new InvalidOperationException("The witness journal must use an absolute path.");
        if (string.IsNullOrWhiteSpace(Role) || Role.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new InvalidOperationException("The witness role may contain only ASCII letters, digits, and hyphens.");
        services.AddScoped<IActivityReconciliationSource, BackgroundProbeCatalog>();
        services.AddSingleton<ActivationTokenCancellationWitness>(serviceProvider =>
            new ActivationTokenCancellationWitness(journal, Role, serviceProvider.GetRequiredService<IHostApplicationLifetime>()));
        services.AddShellInitializer<ActivationTokenCancellationInitializer>(LifecyclePhase.Start, int.MaxValue);
        var providers = services.Where(service => service.ServiceType == typeof(IBackgroundActivityWorkStore) && !service.IsKeyedService).ToArray();
        if (providers.Length != 1)
            throw new InvalidOperationException($"The witness requires exactly one background work store, found {providers.Length}.");
        var provider = providers[0];
        services.Remove(provider);
        services.Add(new ServiceDescriptor(typeof(IBackgroundActivityWorkStore), scope => new ReceiptDeliveryBarrier(
            (IBackgroundActivityWorkStore)(provider.ImplementationInstance ?? provider.ImplementationFactory?.Invoke(scope) ??
                ActivatorUtilities.CreateInstance(scope, provider.ImplementationType!)), journal, Role,
            ownsInner: provider.ImplementationInstance is null), provider.Lifetime));
    }
}

/// <summary>Records when the shell activation token passed to task startup is later cancelled.</summary>
internal sealed class ActivationTokenCancellationWitness(
    string journal,
    string role,
    IHostApplicationLifetime applicationLifetime) : IDisposable
{
    private CancellationTokenRegistration _registration;

    public void Register(CancellationToken cancellationToken) =>
        _registration = cancellationToken.Register(static state => ((ActivationTokenCancellationWitness)state!).RecordCancellation(), this);

    public void Dispose() => _registration.Dispose();

    private void RecordCancellation()
    {
        var path = Path.Combine(journal, $"activation-token-cancelled.{role}.{Guid.NewGuid():N}.json");
        BackgroundWitnessJournal.TryWriteJsonAtomically(path, new
        {
            ProcessId = Environment.ProcessId,
            RecordedAt = DateTimeOffset.UtcNow,
            Role = role,
            ApplicationStopping = applicationLifetime.ApplicationStopping.IsCancellationRequested
        });
    }
}

/// <summary>Non-disposable adapter so the temporary initializer scope cannot unregister the witness.</summary>
internal sealed class ActivationTokenCancellationInitializer(ActivationTokenCancellationWitness witness) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        witness.Register(cancellationToken);
        return Task.CompletedTask;
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
        // A separate atomic record per physical invocation supports overlapping old and replacement hosts.
        // Appending to one file could lose a line or fault a body because another process holds the file.
        var startedPath = Path.Combine(DirectoryPath, $"{Key}.started.{Guid.NewGuid():N}.json");
        await BackgroundWitnessJournal.WriteJsonAtomicallyAsync(startedPath, new
        {
            ProcessId = Environment.ProcessId,
            RecordedAt = DateTimeOffset.UtcNow,
            context.InvocationId,
            context.AttemptId
        }, context.CancellationToken);
        try
        {
            while (WaitForRelease && !File.Exists(Path.Combine(DirectoryPath, Key + ".release")))
                await Task.Delay(25, context.CancellationToken);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            await BackgroundWitnessJournal.TryWriteJsonAtomicallyAsync(
                Path.Combine(DirectoryPath, $"{Key}.cancelled.{Guid.NewGuid():N}.json"), new
                {
                    ProcessId = Environment.ProcessId,
                    RecordedAt = DateTimeOffset.UtcNow,
                    Key,
                    context.InvocationId,
                    context.AttemptId
                });
            await File.WriteAllTextAsync(Path.Combine(DirectoryPath, Key + ".cancelled"), context.InvocationId);
            throw;
        }

        // This records passing the release gate; transition serialization and disposal still follow.
        await TryWriteBodyReleasedAsync(context);
        return ActivityTransition.Complete(Key);
    }

    private async Task TryWriteBodyReleasedAsync(ActivityExecutionContext context)
    {
        var path = Path.Combine(DirectoryPath, Key + ".body-released.json");
        await BackgroundWitnessJournal.TryWriteJsonAtomicallyAsync(path, new
        {
            ProcessId = Environment.ProcessId,
            RecordedAt = DateTimeOffset.UtcNow,
            context.InvocationId,
            context.AttemptId
        });
    }
}

internal static class BackgroundWitnessJournal
{
    public static void TryWriteJsonAtomically<T>(string path, T value)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value));
            File.Move(temporaryPath, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Diagnostic filesystem failures must not replace the activity or store outcome.
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary diagnostic file must not alter cancellation behavior.
            }
        }
    }

    public static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken, bool overwrite = false)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(value), cancellationToken);
            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static async Task TryWriteJsonAtomicallyAsync<T>(string path, T value)
    {
        try
        {
            await WriteJsonAtomicallyAsync(path, value, CancellationToken.None, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Diagnostic filesystem failures must not replace the activity or store outcome.
        }
    }
}
