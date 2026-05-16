using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Activities.Core.Models
{
    internal record SignalHandlerRegistration(Type SignalType, Func<object, SignalContext, ValueTask> Handler);
}
