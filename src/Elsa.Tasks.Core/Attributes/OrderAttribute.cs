using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Tasks.Core.Attributes
{
    /// <summary>
    /// Configures a task with a priority.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class OrderAttribute(float order) : Attribute
    {
        public float Order { get; } = order;
    }
}
