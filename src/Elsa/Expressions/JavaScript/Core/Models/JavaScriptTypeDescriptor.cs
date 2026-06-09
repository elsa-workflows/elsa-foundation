namespace Elsa.Expressions.JavaScript.Core.Models;

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
        loadedType = type;
    }

    public string TypeFullName { get; set; } = string.Empty;

    public Type GetDescriptorType()
    {
        loadedType ??= Type.GetType(TypeFullName) ?? throw new InvalidOperationException($"Type '{TypeFullName}' cannot be loaded");
        return loadedType;
    }
}