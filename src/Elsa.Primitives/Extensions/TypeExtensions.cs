using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Elsa.Primitives.Extensions
{
    /// <summary>
    /// Extends <see cref="Type"/> with additional methods.
    /// </summary>
    public static class TypeExtensions
    {
        private static readonly ConcurrentDictionary<Type, string> SimpleAssemblyQualifiedTypeNameCache = new();

        public static string GetSanitizedFullName(this Type type)
        {
            var ns = type.Namespace;
            string typeName;

            if (type.IsGenericType)
            {
                var sanitizedTypeName = type.Name[..type.Name.IndexOf('`', StringComparison.InvariantCulture)];
                var genericArgs = type.GenericTypeArguments.Select(
                    GetSanitizedFullName
                );
                typeName = $"{sanitizedTypeName}<{string.Join(',', genericArgs)}>";
            }
            else
                typeName = type.Name;

            return $"{ns}.{typeName}";
        }


        /// <summary>
        /// Gets the assembly-qualified name of the type, without any version info etc.
        /// E.g. "System.String, System.Private.CoreLib"
        /// </summary>
        public static string GetSimpleAssemblyQualifiedName(this Type type)
        {
            return SimpleAssemblyQualifiedTypeNameCache.GetOrAdd(type, GetSimplifiedTypeName);
        }

        /// <summary>
        /// Tries to find a method that matches the specified names and returns it. If multiple methods are found, an exception is thrown. If no methods are found, an exception is thrown. If a method is found but does not return Task or ValueTask, an exception is thrown.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="names"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static MethodInfo[] GetMethods(this Type source, string[] names)
        {
            var methods = source.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            var invokeMethods = methods
                .Where(method =>
                {
                    foreach (var name in names)
                    {
                        var nameEquals = string.Equals(
                            method.Name,
                            name,
                            StringComparison.Ordinal
                        );

                        if (nameEquals)
                        {
                            return true;
                        }
                    }

                    return false;
                });

            return [.. invokeMethods];
        }

        public static T InvokeAndUnwrap<T>(this MethodBase method, object target, object[] args) where T : Task
        {
            try
            {
                return (T)method.Invoke(target, args)!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // Unreachable, but required for compiler
            }
        }

        /// <summary>
        /// Returns true of the type is generic, false otherwise.
        /// </summary>
        public static bool IsGenericType(this Type type, Type genericType) => type.IsGenericType && type.GetGenericTypeDefinition() == genericType;

        /// <summary>
        /// Returns true of the type is nullable, false otherwise.
        /// </summary>
        public static bool IsNullableType(this Type type) => type.IsGenericType(typeof(Nullable<>));

        /// <summary>
        /// Returns the wrapped type of the specified nullable type.
        /// </summary>
        public static Type GetTypeOfNullable(this Type type) => type.GenericTypeArguments[0];

        /// <summary>
        /// Returns true if the specified type is a collection type, false otherwise.
        /// </summary>
        public static bool IsCollectionType(this Type type)
        {
            if (!type.IsGenericType)
                return false;

            var elementType = type.GenericTypeArguments[0];
            var collectionType = typeof(ICollection<>).MakeGenericType(elementType);
            var listType = typeof(IList<>).MakeGenericType(elementType);
            return collectionType.IsAssignableFrom(type) || listType.IsAssignableFrom(type);
        }

        /// <summary>
        /// Constructs a collection type from the specified type.
        /// </summary>
        public static Type MakeCollectionType(this Type type) => typeof(ICollection<>).MakeGenericType(type);

        /// <summary>
        /// Returns the element type of the specified collection type.
        /// </summary>
        public static Type GetCollectionElementType(this Type type) => type.GenericTypeArguments[0];

        /// <summary>
        /// Determines whether the specified type is a numeric type.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns>True if the specified type is numeric, otherwise false.</returns>
        public static bool IsNumericType(this Type type)
        {
            return type.IsPrimitive || type == typeof(decimal) || type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);
        }

        /// <summary>
        /// Returns the default value for the specified type.
        /// </summary>
        public static object? GetDefaultValue(this Type type) => type.IsClass ? null : Activator.CreateInstance(type);

        private static string GetSimplifiedTypeName(Type type)
        {
            var assemblyName = type.Assembly.GetName().Name;

            if (type.IsGenericType)
            {
                var genericTypeName = type.GetGenericTypeDefinition().FullName!;
                var backtickIndex = genericTypeName.IndexOf('`');
                var typeNameWithoutArity = genericTypeName[..backtickIndex];
                var arity = genericTypeName[backtickIndex..];

                var genericArguments = type.GetGenericArguments();
                var simplifiedGenericArguments = genericArguments.Select(GetSimplifiedTypeName);

                return $"{typeNameWithoutArity}{arity}[[{string.Join("],[", simplifiedGenericArguments)}]], {assemblyName}";
            }

            var typeName = type.FullName;
            return $"{typeName}, {assemblyName}";
        }
    }
}
