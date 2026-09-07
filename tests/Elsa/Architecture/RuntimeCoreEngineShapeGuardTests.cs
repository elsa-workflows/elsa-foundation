using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// ADR 0033 semantic guard: <c>Elsa.Workflows.Runtime.Core</c> is a contracts/models layer and must
/// not silently re-absorb engine logic. The mechanical dependency-envelope guards cannot catch this
/// class of drift (the original breach compiled cleanly), so this test asserts the *shape* of the
/// assembly: no concrete engine-role types in <c>.Core</c>.
/// </summary>
/// <remarks>
/// The predicate keys on type-name role suffixes and the <c>InMemory*</c> store prefix, scoped to
/// concrete classes (non-abstract, non-record, non-compiler-generated). It deliberately does NOT
/// key on "has methods": the W28 Models/ boundary audit found ~55 of 77 model files legitimately
/// carry behavior (validating constructors, factories, computed properties) and they stay in
/// <c>.Core</c> by user ratification (2026-07-05). Records are excluded for the same reason —
/// engine services are classes, models are records/enums/DTO classes.
/// </remarks>
public sealed class RuntimeCoreEngineShapeGuardTests
{
    /// <summary>Engine role suffixes per ADR 0033's guardrail clause.</summary>
    private static readonly string[] EngineRoleSuffixes =
    [
        "Service",
        "Handler",
        "Dispatcher",
        "Drainer",
        "Orchestrator",
        "Materializer",
        "Committer",
        "Scheduler",
        "Router",
        "Pipeline",
        "Session",
        "Scanner",
        "Processor"
    ];

    [Fact]
    public void RuntimeCore_ContainsNoEngineShapedTypes()
    {
        var offenders = EngineShapedTypes(typeof(WorkflowExecutable).Assembly)
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Engine-shaped types found in Elsa.Workflows.Runtime.Core; they belong in the " +
            "Elsa.Workflows.Runtime implementation package (ADR 0033):" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Predicate-liveness check: the suffix predicate must match the real engine types in the
    /// implementation assembly, so <see cref="RuntimeCore_ContainsNoEngineShapedTypes"/> cannot
    /// pass vacuously if the predicate rots.
    /// </summary>
    [Fact]
    public void EngineAssembly_ContainsTheEngineShapedTypes()
    {
        var engineAssembly = typeof(RuntimeCheckpointCommitter).Assembly;
        Assert.NotEqual(typeof(WorkflowExecutable).Assembly, engineAssembly);

        var matches = EngineShapedTypes(engineAssembly).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("RuntimeCheckpointCommitter", matches);
        Assert.Contains("WorkflowSchedulerDrainer", matches);
        Assert.Contains("WorkflowDrainOrchestrator", matches);
        Assert.Contains("StimulusRouter", matches);
        Assert.Contains("RuntimeActivityInputMaterializer", matches);
        Assert.Contains("RuntimeCoalescingSession", matches);
        Assert.Contains("InMemoryRuntimeRecoveryScanner", matches);
        Assert.Contains("RuntimePostCommitOutboxProcessor", matches);
        Assert.Contains("InMemoryBookmarkStateStore", matches);
    }

    [Fact]
    public void RuntimeRecoveryCursorStore_LivesInTheRuntimeImplementationAssembly()
    {
        Assert.Same(typeof(RuntimeCheckpointCommitter).Assembly, typeof(InMemoryRuntimeRecoverySweepCursorStore).Assembly);
        Assert.NotSame(typeof(WorkflowExecutable).Assembly, typeof(InMemoryRuntimeRecoverySweepCursorStore).Assembly);
    }

    private static IEnumerable<Type> EngineShapedTypes(Assembly assembly) =>
        assembly.GetTypes().Where(IsEngineShaped);

    private static bool IsEngineShaped(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsNested)
            return false;

        if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) || type.Name.Contains('<'))
            return false;

        // Records are models by construction in this repo; engine services are plain classes.
        if (type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null)
            return false;

        if (type.Name.StartsWith("InMemory", StringComparison.Ordinal))
            return true;

        return EngineRoleSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal));
    }
}
