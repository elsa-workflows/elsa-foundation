using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Workflows.Design.Core
{
    public interface IWorkflowDesignContext
    {
        IWorkflowGraph Graph { get; }
    }
}
