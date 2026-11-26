using System.Collections.Concurrent;
using System.Collections.Immutable;
using CShells;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>A small in-process proof of CShells generation metadata and activation rollback seams.</summary>
public sealed class CompositionGenerationSeamProbeTests
{
    [Fact]
    public async Task Candidate_snapshot_is_generation_bound_and_shells_activate_independently()
    {
        var state = new CandidateState();
        state.Set("default", new Candidate("first"));
        var participant = new ProbeParticipant();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTransient<IShellInitializer, CandidateInitializer>();
        services.AddSingleton<IShellGenerationActivationParticipant>(participant);
        services.AddCShells(builder => builder.AddBlueprintProvider(_ => new ProbeBlueprintProvider(state)));

        await using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<IShellRegistry>();
        ActivationGate? gate = null;
        Task<ReloadResult>? pausedDefaultReload = null;

        try
        {
            var first = await registry.GetOrActivateAsync("default");
            Assert.Equal("first", first.Descriptor.Metadata["candidate"]);

            state.Set("default", new Candidate("second"));
            var secondReload = await registry.ReloadAsync("default");
            Assert.Null(secondReload.Error);
            var second = registry.GetActive("default");
            Assert.Same(secondReload.NewShell, second);
            Assert.Equal("second", second!.Descriptor.Metadata["candidate"]);
            Assert.Equal("first", first.Descriptor.Metadata["candidate"]);

            state.Set("default", new Candidate("initializer-failure", FailInitializer: true));
            var initializerFailure = await registry.ReloadAsync("default");
            Assert.NotNull(initializerFailure.Error);
            Assert.Null(initializerFailure.NewShell);
            Assert.Same(second, registry.GetActive("default"));
            Assert.Equal(ShellLifecycleState.Active, second.State);

            state.Set("default", new Candidate("participant-failure", FailCommit: true));
            var participantFailure = await registry.ReloadAsync("default");
            Assert.NotNull(participantFailure.Error);
            Assert.Null(participantFailure.NewShell);
            Assert.Same(second, registry.GetActive("default"));
            Assert.Equal(ShellLifecycleState.Active, second.State);
            Assert.Contains("participant-failure", participant.RolledBack);

            gate = new ActivationGate();
            state.Set("default", new Candidate("captured-default", Gate: gate));
            state.Set("secondary", new Candidate("secondary-v1"));
            pausedDefaultReload = registry.ReloadAsync("default");
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            state.Set("default", new Candidate("changed-default-source"));
            state.Set("secondary", new Candidate("secondary-v2"));
            var secondaryReload = await registry.ReloadAsync("secondary").WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(secondaryReload.Error);
            var secondary = registry.GetActive("secondary");
            Assert.Same(secondaryReload.NewShell, secondary);
            Assert.Equal("secondary-v2", secondary!.Descriptor.Metadata["candidate"]);
            Assert.Same(second, registry.GetActive("default"));

            gate.Release.TrySetResult();
            var defaultReload = await pausedDefaultReload.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(defaultReload.Error);
            var promotedDefault = registry.GetActive("default");
            Assert.Same(defaultReload.NewShell, promotedDefault);
            Assert.Equal("captured-default", promotedDefault!.Descriptor.Metadata["candidate"]);
            Assert.Same(secondary, registry.GetActive("secondary"));
            Assert.Equal("secondary-v2", secondary.Descriptor.Metadata["candidate"]);

            var mutableMetadata = new Dictionary<string, string> { ["candidate"] = "before" };
            var mutableDescriptor = ShellDescriptor.Create("negative-control", 1, mutableMetadata);
            mutableMetadata["candidate"] = "after";
            Assert.Equal("after", mutableDescriptor.Metadata["candidate"]);
        }
        finally
        {
            gate?.Release.TrySetResult();
            if (pausedDefaultReload is not null)
            {
                try
                {
                    await pausedDefaultReload.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    // The probe's initializer gate is always released above; this bound keeps cleanup finite on a broken path.
                }
            }

            await DrainActiveShellsAsync(registry);
        }
    }

    private static async Task DrainActiveShellsAsync(IShellRegistry registry)
    {
        foreach (var shell in registry.GetActiveShells().ToArray())
        {
            var drain = await registry.DrainAsync(shell);
            await drain.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed record Candidate(string Id, bool FailInitializer = false, bool FailCommit = false, ActivationGate? Gate = null);

    private sealed class CandidateState
    {
        private readonly ConcurrentDictionary<string, Candidate> _candidates = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string name, Candidate candidate) => _candidates[name] = candidate;

        public Candidate Capture(string name) => _candidates.TryGetValue(name, out var candidate)
            ? candidate
            : throw new InvalidOperationException($"No candidate is configured for shell '{name}'.");
    }

    private sealed class ProbeBlueprintProvider(CandidateState state) : IShellBlueprintProvider
    {
        public Task<ProvidedBlueprint?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(name, "default", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "secondary", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<ProvidedBlueprint?>(null);

            var blueprint = new ProbeBlueprint(name, state.Capture(name));
            return Task.FromResult<ProvidedBlueprint?>(new ProvidedBlueprint(blueprint, null));
        }

        public Task<BlueprintPage> ListAsync(BlueprintListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BlueprintPage([], null));
    }

    private sealed class ProbeBlueprint(string name, Candidate candidate) : IShellBlueprint
    {
        public string Name => name;
        public IReadOnlyDictionary<string, string> Metadata { get; } =
            ImmutableDictionary<string, string>.Empty
                .Add("candidate", candidate.Id)
                .Add("fail-commit", candidate.FailCommit.ToString());

        public Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken = default)
        {
            var settings = new ShellSettings(new ShellId(Name));
            settings.ConfigurationData["Probe:FailInitializer"] = candidate.FailInitializer;
            if (candidate.Gate is not null)
                settings.ConfigurationData["Probe:ActivationGate"] = candidate.Gate;
            return Task.FromResult(settings);
        }
    }

    private sealed class CandidateInitializer(ShellSettings settings) : IShellInitializer
    {
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (settings.ConfigurationData.TryGetValue("Probe:FailInitializer", out var fail) && fail is true)
                throw new InvalidOperationException("candidate initializer rejected");

            if (settings.ConfigurationData.TryGetValue("Probe:ActivationGate", out var value) && value is ActivationGate gate)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class ActivationGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ProbeParticipant : IShellGenerationActivationParticipant
    {
        public ConcurrentQueue<string> RolledBack { get; } = new();

        public Task PrepareAsync(IShell shell, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Commit(IShell shell)
        {
            if (shell.Descriptor.Metadata["fail-commit"] == bool.TrueString)
                throw new InvalidOperationException("candidate commit rejected");
        }

        public void Complete(IShell shell) { }

        public void Rollback(IShell shell) => RolledBack.Enqueue(shell.Descriptor.Metadata["candidate"]);
    }
}
