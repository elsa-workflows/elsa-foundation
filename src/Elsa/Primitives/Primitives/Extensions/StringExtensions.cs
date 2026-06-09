namespace Elsa.Primitives.Extensions;

public static class StringExtensions
{
    public static string Camelize(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (value.Length == 1)
            return value.ToLower();

        return char.ToLower(value[0]) + value[1..];
    }

    public static string Pascalize(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.Length == 1)
            return value.ToUpper();

        return char.ToUpper(value[0]) + value[1..];
    }

    public static Type GetLoadedType(this string typeName)
    {
        return Type.GetType(typeName) ?? throw new ArgumentException($"Type with name '{typeName}' cannot be loaded");
    }
}