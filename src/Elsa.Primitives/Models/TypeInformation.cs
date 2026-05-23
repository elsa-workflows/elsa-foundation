using System.Reflection;
using System.Text.Json.Serialization;

namespace Elsa.Primitives.Models
{
    public sealed class TypeInformation<TValue> : TypeInformation
    {
        public TypeInformation()
            : base(typeof(TValue).Name, $"{typeof(TValue).Namespace}", $"{typeof(TValue).Assembly.FullName}", $"{typeof(TValue).Assembly.GetName().Version}")
        {
        }
    }

    [method: JsonConstructor]
    public class TypeInformation(string typeName, string @namespace, string assemblyName, string assemblyVersion)
    {
        public TypeInformation(Type type) 
            : this(type.Name, $"{type.Namespace}", $"{type.Assembly.FullName}", $"{type.Assembly.GetName().Version}")
        {            
        }

        public string TypeName => typeName.TrimStart('.');

        public string Namespace => @namespace.TrimEnd('.');

        public string AssemblyName => assemblyName;

        public string AssemblyVersion => assemblyVersion;

        public static TypeInformation FromType(Type type)
        {
            return new TypeInformation(
                type.Name,
                type.Namespace ?? string.Empty,
                type.AssemblyQualifiedName ?? string.Empty,
                type.Assembly.GetName().Version?.ToString() ?? string.Empty
            );
        }

        public static readonly TypeInformation String = new TypeInformation<string>();
        public static readonly TypeInformation Object = new TypeInformation<object>();
        public static readonly TypeInformation Integer = new TypeInformation<int>();
        public static readonly TypeInformation Double = new TypeInformation<double>();
        public static readonly TypeInformation Float = new TypeInformation<float>();
        public static readonly TypeInformation Decimal = new TypeInformation<decimal>();
        public static readonly TypeInformation Bool = new TypeInformation<bool>();
        public static readonly TypeInformation DateTimeOffset = new TypeInformation<DateTimeOffset>();
        public static readonly TypeInformation DateTime = new TypeInformation<DateTime>();
        public static readonly TypeInformation TimeSpan = new TypeInformation<TimeSpan>();

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
}
