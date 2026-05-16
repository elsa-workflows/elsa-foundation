using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Services
{
    /// <inheritdoc />
    internal sealed class JavaScriptTypeAliasRegistry : IJavaScriptTypeAliasRegistry
    {
        private readonly Lazy<Dictionary<Type, string>> typeAliasDictionary;
        private readonly IEnumerable<IJavaScriptTypeDescriptorProvider> descriptorProviders;

        public JavaScriptTypeAliasRegistry(IEnumerable<IJavaScriptTypeDescriptorProvider> descriptorProviders)
        {
            typeAliasDictionary = new(InitializeRegistry);
            this.descriptorProviders = descriptorProviders;
        }

        /// <inheritdoc />
        public bool TryGetAlias(Type type, out string alias) => typeAliasDictionary.Value.TryGetValue(type, out alias!);      

        private Dictionary<Type, string> InitializeRegistry()
        {
            var result = new Dictionary<Type, string>();
            foreach (var provider in descriptorProviders)
            {
                foreach (var descriptor in provider.GetDescriptors().Where(x => x.RegisterAlias))
                {
                    var type = Type.GetType(descriptor.TypeFullName)
                        ?? throw new InvalidOperationException($"Could not load type '{descriptor.TypeFullName}'");
                    var alias = !string.IsNullOrWhiteSpace(descriptor.Alias)
                        ? descriptor.Alias
                        : type.Name;

                    result[type] = alias;
                }
            }
            return result;
        }
    }
}
