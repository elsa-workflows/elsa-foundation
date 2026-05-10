using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.Core
{
    /// <summary>
    /// Provides descriptors for expression syntaxes.
    /// </summary>
    public interface IExpressionDescriptorProvider
    {
        /// <summary>
        /// Gets the descriptors for the expression syntaxes supported by this provider.
        /// </summary>
        IEnumerable<IExpressionDescriptor> GetDescriptors();
    }
}
