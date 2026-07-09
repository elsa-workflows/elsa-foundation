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

    /// <summary>
    /// Resolves a <see cref="Type"/> from its name. First tries <see cref="Type.GetType(string)"/> (which only
    /// searches the calling assembly, <c>Elsa.Primitives</c>, and corelib); when that returns null — the common
    /// case for an unqualified name whose type lives in some other loaded assembly — it falls back to scanning
    /// every assembly currently loaded into the app domain and returns the first match. Throws when the type still
    /// cannot be resolved.
    /// </summary>
    public static Type GetLoadedType(this string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is not null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type is not null)
                return type;
        }

        throw new ArgumentException($"Type with name '{typeName}' cannot be loaded");
    }
}