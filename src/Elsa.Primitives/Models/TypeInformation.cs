using Elsa.Primitives.Contracts;
using System.Text.Json.Serialization;

namespace Elsa.Primitives.Models
{
    [method: JsonConstructor]
    public sealed class TypeInformation(string typeName, string @namespace, string assemblyName, string assemblyVersion) 
        : ITypeInformation
    {
        public string TypeName => typeName;
        
        public string Namespace => @namespace!;

        public string AssemblyName => assemblyName;

        public string? AssemblyVersion => assemblyVersion;

        public static ITypeInformation FromType(Type type)
        {
            return new TypeInformation(
                type.Name,
                type.Namespace ?? string.Empty,
                type.AssemblyQualifiedName ?? string.Empty,
                type.Assembly.GetName().Version?.ToString() ?? string.Empty
            );
        }

        public static readonly ITypeInformation String = new TypeInformation<string>();
        public static readonly ITypeInformation Object = new TypeInformation<object>();
        public static readonly ITypeInformation Integer = new TypeInformation<int>();
        public static readonly ITypeInformation Double = new TypeInformation<double>();
        public static readonly ITypeInformation Float = new TypeInformation<float>();
        public static readonly ITypeInformation Decimal = new TypeInformation<decimal>();
        public static readonly ITypeInformation Bool = new TypeInformation<bool>();
        public static readonly ITypeInformation DateTimeOffset = new TypeInformation<DateTimeOffset>();
        public static readonly ITypeInformation DateTime = new TypeInformation<DateTime>();
        public static readonly ITypeInformation TimeSpan = new TypeInformation<TimeSpan>();
    }

    public sealed class TypeInformation<TValue> : ITypeInformation
    {        
        public string TypeName => typeof(TValue).Name;

        public string Namespace => typeof(TValue).Namespace ?? string.Empty;

        public string AssemblyName => typeof(TValue).AssemblyQualifiedName ?? string.Empty;

        public string? AssemblyVersion => typeof(TValue).Assembly.GetName().Version?.ToString() ?? string.Empty;       
    }
}
