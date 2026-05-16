namespace Elsa.Expressions.JavaScript.Core.Models
{
    public sealed class JavaScriptTypeDescriptor
    {
        private Type? loadedType;

        public JavaScriptTypeDescriptor()
        {
        }

        public JavaScriptTypeDescriptor(string typeFullName)
        {
            TypeFullName = typeFullName;
        }

        public JavaScriptTypeDescriptor(Type type)
        {
            TypeFullName = type.FullName!;
            Alias = type.Name;
            loadedType = type;
        }

        public bool RegisterTypeDeclaration { get; set; } = true;

        public bool RegisterType { get; set; } = true;

        public bool RegisterAlias { get; set; } = true;

        public string? Alias { get; set; }

        public string TypeFullName { get; set; } = string.Empty;

        public Type GetDescriptorType()
        {            
            loadedType ??= Type.GetType(TypeFullName) ?? throw new InvalidOperationException($"Type '{TypeFullName}' cannot be loaded");
            return loadedType;
        }
    }
}
