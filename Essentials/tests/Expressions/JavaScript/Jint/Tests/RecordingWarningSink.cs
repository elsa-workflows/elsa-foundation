using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>A test sink that keeps every reported warning in order; the runtime's collector lives in Elsa.Workflows.Runtime.</summary>
internal sealed class RecordingWarningSink : IExecutionWarningSink
{
    private readonly List<ExecutionWarning> _warnings = [];

    public IReadOnlyList<ExecutionWarning> Warnings => _warnings;

    public void Report(ExecutionWarning warning) => _warnings.Add(warning);
}
