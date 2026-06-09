using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid.Models;

namespace Elsa.Expressions.Liquid.Services
{
    /// <summary>
    /// Provides Liquid expression descriptors.
    /// </summary>
    public sealed class LiquidExpressionDescriptorProvider : IExpressionDescriptorProvider
    {
        /// <inheritdoc />
        public IEnumerable<IExpressionDescriptor> GetDescriptors()
        {
            yield return new LiquidExpressionDescriptor();
        }
    }
}
