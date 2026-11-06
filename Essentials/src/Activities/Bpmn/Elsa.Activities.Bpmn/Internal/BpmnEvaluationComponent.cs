using Bpmn.Semantics;
using Elsa.Activities.Bpmn.Exceptions;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Shared evaluation boundary; the released library owns all native and direct BPMN token semantics.</summary>
internal static class BpmnEvaluationComponent
{
    public static BpmnInterpreter Interpreter { get; } = BpmnInterpreter.CreateDefault();
    public static BpmnEvaluation Evaluate(Func<BpmnEvaluation> action)
    {
        try { return action(); }
        catch (BpmnExecutionException exception) { throw new BpmnHostException(exception.Message, exception); }
    }
}
