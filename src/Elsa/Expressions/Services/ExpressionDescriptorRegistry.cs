using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Services
{
    /// <inheritdoc />
    public sealed class ExpressionDescriptorRegistry : IExpressionDescriptorRegistry
    {
        private readonly IDictionary<string, IExpressionDescriptor> _expressionSyntaxDescriptors = new Dictionary<string, IExpressionDescriptor>();

        /// <summary>
        /// Represents a registry of expression descriptors.
        /// </summary>
        public ExpressionDescriptorRegistry(IEnumerable<IExpressionDescriptorProvider> providers)
        {
            foreach (var provider in providers)
            {
                var descriptors = provider.GetDescriptors();
                AddRange(descriptors);
            }
        }

        /// <inheritdoc />
        public void Add(IExpressionDescriptor descriptor)
        {
            _expressionSyntaxDescriptors[descriptor.TypeName] = descriptor;
        }

        /// <inheritdoc />
        public void AddRange(IEnumerable<IExpressionDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
                Add(descriptor);
        }

        /// <inheritdoc />
        public IEnumerable<IExpressionDescriptor> ListAll() => _expressionSyntaxDescriptors.Values;

        /// <inheritdoc />
        public IExpressionDescriptor? Find(Func<IExpressionDescriptor, bool> predicate) => _expressionSyntaxDescriptors.Values.FirstOrDefault(predicate);

        /// <inheritdoc />
        public IExpressionDescriptor? Find(string type) => _expressionSyntaxDescriptors.TryGetValue(type, out var descriptor) ? descriptor : default;
    }
}
