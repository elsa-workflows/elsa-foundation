using System.Collections.Concurrent;
using CShells.Lifecycle;

namespace Elsa.Workbench.Boot;

/// <summary>
/// Times the lazy shell-activation wall from the host side (spec 129). CShells activates each shell on its first
/// matching request and drives every shell through <see cref="ShellLifecycleState"/> transitions; subscribing to
/// the shell registry is the one host-observable seam for that cliff. This records the wall between a shell first
/// entering <see cref="ShellLifecycleState.Initializing"/> and reaching <see cref="ShellLifecycleState.Active"/>.
///
/// It intentionally cannot attribute time to individual <c>IShellInitializer</c> instances: CShells resolves each
/// initializer by its concrete registered type and invokes <c>InitializeAsync</c> internally, giving the host no
/// interception point between registrations. That per-initializer gap is the key finding gating program unit 2
/// (see docs/reports/cold-start-readiness-2026-07.md).
/// </summary>
public sealed class BootShellActivationObserver(BootPhaseTimeline timeline) : IShellLifecycleSubscriber
{
    private readonly ConcurrentDictionary<string, double> _initializingAt = new(StringComparer.Ordinal);

    public Task OnStateChangedAsync(
        IShell shell,
        ShellLifecycleState previous,
        ShellLifecycleState current,
        CancellationToken cancellationToken = default)
    {
        var name = shell.Descriptor.Name;

        if (current == ShellLifecycleState.Initializing)
        {
            _initializingAt[name] = timeline.ElapsedMs;
            timeline.Mark($"shell:{name}:initializing");
        }
        else if (current == ShellLifecycleState.Active)
        {
            if (_initializingAt.TryRemove(name, out var startedAt))
                timeline.Measure($"shell:{name}:activation-wall", startedAt, "Initializing→Active (all initializers)");
            else
                timeline.Mark($"shell:{name}:active");
        }
        else
        {
            timeline.Mark($"shell:{name}:{current.ToString().ToLowerInvariant()}");
        }

        return Task.CompletedTask;
    }
}
