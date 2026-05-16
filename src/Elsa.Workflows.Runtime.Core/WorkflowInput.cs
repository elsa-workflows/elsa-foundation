using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Workflows.Runtime.Core
{
    /// <summary>
    /// Represents a workflow input name and value.
    /// </summary>
    public record WorkflowInput(string Name, object? Value);
}
