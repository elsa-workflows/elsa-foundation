using System.Reflection;
using System.Text.Json.Serialization;

namespace Elsa.Primitives.Models;

public sealed record TypeInformation
{
    [JsonConstructor]
    public TypeInformation(
        string typeName,
        string @namespace,
        string assemblyName,
        string assemblyVersion)
    {
        TypeName = typeName.TrimStart('.');
        Namespace = @namespace.TrimEnd('.');
        AssemblyName = assemblyName;
        AssemblyVersion = assemblyVersion;
    }

    public string TypeName { get; init; }
    public string Namespace { get; init; }
    public string AssemblyName { get; init; }
    public string AssemblyVersion { get; init; }

    public static TypeInformation FromType(Type type)
    {
        return new TypeInformation(
            type.Name,
            type.Namespace ?? string.Empty,
            type.Assembly.GetName().Name ?? string.Empty,
            type.Assembly.GetName().Version?.ToString() ?? string.Empty
        );
    }

    public static TypeInformation FromType<TValue>() => FromType(typeof(TValue));

    public static readonly TypeInformation String = FromType<string>();
    public static readonly TypeInformation Object = FromType<object>();
    public static readonly TypeInformation Integer = FromType<int>();
    public static readonly TypeInformation Double = FromType<double>();
    public static readonly TypeInformation Float = FromType<float>();
    public static readonly TypeInformation Decimal = FromType<decimal>();
    public static readonly TypeInformation Bool = FromType<bool>();
    public static readonly TypeInformation DateTimeOffset = FromType<DateTimeOffset>();
    public static readonly TypeInformation DateTime = FromType<DateTime>();
    public static readonly TypeInformation TimeSpan = FromType<TimeSpan>();

    public Type LoadType()
    {
        var assemblyName = new AssemblyName(AssemblyName)
        {
            Version = new Version(AssemblyVersion)
        };
        var assembly = Assembly.Load(assemblyName);

        var typeName = GetTypeFullName();
        var type = assembly.GetType(typeName, throwOnError: true, ignoreCase: true);

        return type!;
    }

    public string GetTypeFullName()
    {
        return string.Join('.', Namespace, TypeName);
    }
}