using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>Variable sessions that are not a step's own.</summary>
public static class StepVariables
{
    /// <summary>
    /// The session of a context that is not a step body (spec 1231 FR-002): every call throws. A background worker gets
    /// this one: ADR 0093 C9 refuses a background placement of an activity that reads or writes variables (spec 1231 T030),
    /// so a background body has no variables to touch.
    /// </summary>
    public static IStepVariables Unavailable { get; } = new UnavailableStepVariables();

    private sealed class UnavailableStepVariables : IStepVariables
    {
        private const string Message =
            "No variable session is available here: only a step body (a leaf activity's execute or resume, run inline) reads and writes " +
            "workflow variables through context.Variables. Input bindings, structural evaluations and background workers have no session.";

        public T Get<T>(VariableReference<T> reference) => throw new InvalidOperationException(Message);
        public T Get<T>(ReadOnlyVariableReference<T> reference) => throw new InvalidOperationException(Message);
        public void Set<T>(VariableReference<T> reference, T value) => throw new InvalidOperationException(Message);
#pragma warning disable CS0618, CS0809
        [Obsolete("A ReadOnlyVariableReference cannot be written; this call always throws.")]
        public void Set<T>(ReadOnlyVariableReference<T> reference, T value) => throw new InvalidOperationException(Message);
#pragma warning restore CS0618, CS0809
        public T Get<T>(string name) => throw new InvalidOperationException(Message);
        public void Set<T>(string name, T value) => throw new InvalidOperationException(Message);
    }
}
