using Elsa.Workflows.Testing.Core.Models;

namespace Elsa.Workflows.Testing.Core.Contracts;

/// <summary>One replacement provider evaluates deterministic checks over already authorized observations.</summary>
public interface IWorkflowTestAssertionEvaluator
{
    TestCheckResult Evaluate(TestAssertion assertion, TestSubjectObservation observation);
}

/// <summary>One replacement provider projects authoritative case results into the shared CI gate policy.</summary>
public interface IWorkflowTestReportPolicy
{
    int GetExitCode(TestRunReport report);
}

/// <summary>One replacement provider renders the standard CI projection without changing detailed JSON verdicts.</summary>
public interface IWorkflowTestReportWriter
{
    void WriteJunit(TestRunReport report, TextWriter output);
}
