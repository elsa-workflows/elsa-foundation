using CShells.Lifecycle;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Drives every declared target's admission in the <see cref="LifecyclePhase.Prepare"/> phase. This is the
/// single registered initializer type for Groundwork admission regardless of how many targets or providers a
/// host composes, so admitting two SQLite targets is no different from admitting one.
/// <para>
/// Admission runs sequentially in ordinal target order. Schema admission takes provider-level transition locks
/// and happens once at startup, so concurrency buys little while making a failure much harder to attribute to
/// a specific target.
/// </para>
/// </summary>
public sealed class GroundworkTargetAdmissionInitializer(IEnumerable<IGroundworkTargetAdmission> admissions)
    : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => AdmitAllAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => AdmitAllAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task AdmitAllAsync(CancellationToken cancellationToken)
    {
        foreach (var admission in admissions.OrderBy(candidate => candidate.TargetName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await admission.AdmitAsync(cancellationToken);
        }
    }
}
