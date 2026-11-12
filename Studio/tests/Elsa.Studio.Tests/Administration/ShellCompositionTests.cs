using CShells;
using CShells.Lifecycle;
using Elsa.Studio.Hosting.Nuplane;

namespace Elsa.Studio.Tests.Administration;

/// <summary>
/// Pins #4092: a feature apply that loads the catalog while another apply's save reloads configuration must reach the
/// revision check (and so a conflict), not fail with CShells' "configuration changed while composing" refusal.
/// </summary>
public sealed class ShellCompositionTests
{
    private static InvalidOperationException ConfigurationChanged() =>
        new("Configuration changed while composing shell 'Authoring'. Retry activation or reload.");

    [Fact]
    public async Task Composes_once_when_the_first_attempt_succeeds()
    {
        var blueprint = new QueuedBlueprint(null);

        var settings = await ShellComposition.ComposeAsync(blueprint, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal("Authoring", settings.Id.Name);
        Assert.Equal(1, blueprint.Composes);
    }

    [Fact]
    public async Task Composes_again_when_configuration_changed_while_composing()
    {
        var blueprint = new QueuedBlueprint(ConfigurationChanged(), ConfigurationChanged(), null);

        var settings = await ShellComposition.ComposeAsync(blueprint, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal("Authoring", settings.Id.Name);
        Assert.Equal(3, blueprint.Composes);
    }

    [Fact]
    public async Task Reports_the_refusal_after_three_attempts_when_configuration_keeps_changing()
    {
        var blueprint = new QueuedBlueprint(ConfigurationChanged(), ConfigurationChanged(), ConfigurationChanged(), null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ShellComposition.ComposeAsync(blueprint, TimeSpan.Zero, CancellationToken.None));

        Assert.StartsWith("Configuration changed ", error.Message);
        Assert.Equal(ShellComposition.MaxAttempts, blueprint.Composes);
    }

    [Fact]
    public async Task Reports_any_other_failure_on_its_first_attempt()
    {
        var blueprint = new QueuedBlueprint(new InvalidOperationException("Shell 'Authoring' is malformed."), null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ShellComposition.ComposeAsync(blueprint, TimeSpan.Zero, CancellationToken.None));

        Assert.Equal("Shell 'Authoring' is malformed.", error.Message);
        Assert.Equal(1, blueprint.Composes);
    }

    [Fact]
    public async Task A_cancellation_during_the_pause_propagates_without_another_attempt()
    {
        using var cancellation = new CancellationTokenSource();
        var blueprint = new QueuedBlueprint(ConfigurationChanged(), null) { AfterCompose = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ShellComposition.ComposeAsync(blueprint, TimeSpan.FromMinutes(1), cancellation.Token));

        Assert.Equal(1, blueprint.Composes);
    }

    private sealed class QueuedBlueprint(params Exception?[] outcomes) : IShellBlueprint
    {
        private readonly Queue<Exception?> _outcomes = new(outcomes);

        public int Composes { get; private set; }
        public Action? AfterCompose { get; init; }
        public string Name => "Authoring";
        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

        public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
        {
            Composes++;
            var error = _outcomes.Dequeue();
            AfterCompose?.Invoke();
            return error is null ? Task.FromResult(new ShellSettings(new ShellId(Name))) : Task.FromException<ShellSettings>(error);
        }
    }
}
