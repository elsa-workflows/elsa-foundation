using Elsa.Activities.Design.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Workflows.Design.Core
{
    public interface IActivityConnection
    {
        IActivityDefinition Incoming { get; }
        IActivityDefinition Outgoing { get; }
    }
}
