using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Activities.Core.Models
{
    /// <summary>
    /// The type of a port.
    /// </summary>
    public enum PortType
    {
        /// <summary>
        /// A port that is embedded in the activity.
        /// </summary>
        Embedded,

        /// <summary>
        /// A flow port.
        /// </summary>
        Flow
    }
}
