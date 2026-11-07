using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Recovery;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Semantic guard: <c>Elsa.Workflows.Runtime.Core</c> is a contracts/models layer and must
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
/// The opaque preparation owner handle is a contract model despite its Session suffix. Its
/// exact, read-only shape is checked below; all other Session types retain the engine predicate.
/// </remarks>
public sealed class RuntimeCoreEngineShapeGuardTests
{
    /// <summary>The engine role suffixes no concrete class in <c>.Core</c> may carry.</summary>
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

    [Fact]
    public void PreparationOwnerSession_IsAnOpaqueReadOnlyContractHandle()
    {
        Assert.True(IsPreparationContractHandle(typeof(WorkflowPreparationOwnerSession)));
        Assert.False(IsEngineShaped(typeof(WorkflowPreparationOwnerSession)));
    }

    [Fact]
    public void PreparationContractException_DoesNotAdmitOtherSessions()
    {
        Assert.False(IsPreparationContractHandle(typeof(UnapprovedPreparationSession)));
        Assert.True(IsEngineShaped(typeof(UnapprovedPreparationSession)));
        Assert.False(IsPreparationContractHandle(typeof(UnapprovedOwnerSession)));
        Assert.True(IsEngineShaped(typeof(UnapprovedOwnerSession)));
    }

    private static IEnumerable<Type> EngineShapedTypes(Assembly assembly) =>
        assembly.GetTypes().Where(IsEngineShaped);

    private static bool IsEngineShaped(Type type)
    {
        if (IsPreparationContractHandle(type))
            return false;

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

    // ADR 0033 permits contract models, including validating/forwarding behavior. This handle
    // owns no engine, lifetime or transaction: the Runtime callback and provider lease own them.
    // A changed shape fails closed here rather than gaining a name-only exception. The Runtime
    // contract tests independently exercise every callback/lease access and expired-view refusal.
    internal static bool IsPreparationContractHandle(Type type)
    {
        if (type != typeof(WorkflowPreparationOwnerSession) || !type.IsSealed ||
            type.BaseType != typeof(object) || type.GetInterfaces().Length != 0 ||
            type.GetConstructors().Length != 0)
            return false;

        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var constructors = type.GetConstructors(declared);
        if (constructors.Length != 1 || !constructors[0].IsAssembly ||
            !constructors[0].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual([typeof(IWorkflowPreparationOwnerLease), typeof(Action)]))
            return false;

        var fields = type.GetFields(declared);
        if (fields.Length != 2 || fields.Any(field => !field.IsPrivate || !field.IsInitOnly || field.IsStatic) ||
            fields.Count(field => field.FieldType == typeof(Action)) != 1 ||
            fields.Count(field => field.FieldType == typeof(IWorkflowPreparationOwnerLease)) != 1)
            return false;

        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(WorkflowPreparationOwnerSession.Owner)] = typeof(WorkflowPreparationOwnerStamp),
            [nameof(WorkflowPreparationOwnerSession.Decision)] = typeof(WorkflowPreparationDecision),
            [nameof(WorkflowPreparationOwnerSession.Lease)] = typeof(IWorkflowPreparationOwnerLease),
            [nameof(WorkflowPreparationOwnerSession.SelectedRoots)] = typeof(IReadOnlyList<WorkflowPreparationRoot>)
        };
        var properties = type.GetProperties(declared);
        if (properties.Length != expectedProperties.Count || properties.Any(property =>
                !expectedProperties.TryGetValue(property.Name, out var expected) || property.PropertyType != expected ||
                property.SetMethod is not null || property.GetMethod is not { IsPublic: true, IsStatic: false }))
            return false;

        var methods = type.GetMethods(declared);
        return type.GetEvents(declared).Length == 0 && methods.Length == 5 &&
               methods.All(method => method.IsPublic && !method.IsStatic && !method.IsVirtual &&
                   method.GetParameters().Length == 0 &&
                   (method.IsSpecialName && properties.Any(property => property.GetMethod == method) ||
                    method.Name == nameof(WorkflowPreparationOwnerSession.EnsureActive) && method.ReturnType == typeof(void)));
    }
}

// Top-level controls exercise the same predicate used for production Core types (nested types
// are intentionally excluded). Neither an overlapping suffix nor a lease-shaped API is admitted.
internal sealed class UnapprovedPreparationSession;

internal sealed class UnapprovedOwnerSession
{
    public IWorkflowPreparationOwnerLease? Lease { get; }
}
