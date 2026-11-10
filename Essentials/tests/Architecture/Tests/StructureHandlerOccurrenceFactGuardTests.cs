using System.Reflection;
using System.Reflection.Emit;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Every first-party structure handler states <see cref="ActivityChildResultFacts.EarlierProducerRunCanReachConsumer"/>
/// itself (#4053). Publication refuses an activity-result binding without a scope wherever the owning structure leaves
/// the fact unset, so a handler that inherits the interface default silently stops its children's results from being
/// offered. The check reads compiled code: the handler must declare its own <c>AssessChildResult</c>, and it, or a
/// method of the same assembly it calls, must set the fact.
/// </summary>
/// <remarks>
/// A behavioural check over registered handlers would need a meaningful structure payload per handler, which only each
/// handler's own tests can build; those tests pin the value. This guard pins that every handler answers at all.
/// </remarks>
public sealed class StructureHandlerOccurrenceFactGuardTests
{
    private static readonly MethodInfo Assess = typeof(IActivityStructureHandler).GetMethod(nameof(IActivityStructureHandler.AssessChildResult))!;

    private static readonly MethodInfo Setter = typeof(ActivityChildResultFacts)
        .GetProperty(nameof(ActivityChildResultFacts.EarlierProducerRunCanReachConsumer))!.SetMethod!;

    [Fact]
    public void Every_structure_handler_states_whether_an_earlier_producer_run_can_reach_a_reader()
    {
        var handlers = RuntimeLibraries.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IActivityStructureHandler).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(handlers, type => type.Name == "SequenceStructureHandler");
        Assert.True(handlers.Length >= 8, $"Only {handlers.Length} structure handlers were found; discovery has stopped finding them.");
        var silent = handlers.Where(type => !StatesTheFact(type)).Select(type => type.FullName).ToArray();
        Assert.True(silent.Length == 0,
            "These structure handlers do not state EarlierProducerRunCanReachConsumer in their own AssessChildResult:" +
            Environment.NewLine + string.Join(Environment.NewLine, silent));
    }

    /// <summary>The detector itself: the interface default and a declared method that leaves the fact unset are flagged.</summary>
    [Fact]
    public void A_handler_that_inherits_the_default_or_leaves_the_fact_unset_is_flagged()
    {
        Assert.False(StatesTheFact(typeof(InheritingHandler)));
        Assert.False(StatesTheFact(typeof(SilentHandler)));
        Assert.True(StatesTheFact(typeof(StatingHandler)));
        Assert.True(StatesTheFact(typeof(DelegatingHandler)));
    }

    private static bool StatesTheFact(Type handler)
    {
        var map = handler.GetInterfaceMap(typeof(IActivityStructureHandler));
        var target = map.TargetMethods[Array.IndexOf(map.InterfaceMethods, Assess)];
        if (target.DeclaringType != handler)
            return false;

        var visited = new HashSet<MethodBase>();
        var pending = new Stack<MethodBase>([target]);
        while (pending.TryPop(out var method))
        {
            if (!visited.Add(method) || method.GetMethodBody()?.GetILAsByteArray() is not { Length: > 0 } il)
                continue;
            foreach (var instruction in IlInstructionReader.Read(il))
            {
                if (instruction.OpCode.OperandType != OperandType.InlineMethod)
                    continue;
                var called = Resolve(method, instruction.Operand);
                if (called is null)
                    continue;
                if (called.Module == Setter.Module && called.MetadataToken == Setter.MetadataToken)
                    return true;
                if (called.Module.Assembly == handler.Assembly)
                    pending.Push(called);
            }
        }

        return false;
    }

    private static MethodBase? Resolve(MethodBase method, int token)
    {
        try
        {
            return method.Module.ResolveMethod(token,
                method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                method.IsGenericMethod ? method.GetGenericArguments() : null);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private abstract class ProbeHandler : IActivityStructureHandler
    {
        public string Kind => "probe";
        public string SchemaVersion => "1.0.0";
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }

    private sealed class InheritingHandler : ProbeHandler;

    private sealed class SilentHandler : ProbeHandler, IActivityStructureHandler
    {
        public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
            new(ActivityChildResultAvailability.Definite, IsCyclicBackEdge: false);
    }

    private sealed class StatingHandler : ProbeHandler, IActivityStructureHandler
    {
        public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
            new(ActivityChildResultAvailability.Definite) { EarlierProducerRunCanReachConsumer = false };
    }

    private sealed class DelegatingHandler : ProbeHandler, IActivityStructureHandler
    {
        public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
            Analysis.Assess(producerChildNodeId == consumerChildNodeId);

        private static class Analysis
        {
            public static ActivityChildResultFacts Assess(bool same) =>
                new(ActivityChildResultAvailability.Unknown) { EarlierProducerRunCanReachConsumer = same };
        }
    }
}
