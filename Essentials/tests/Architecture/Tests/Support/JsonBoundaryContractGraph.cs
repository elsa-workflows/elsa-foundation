using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elsa.Architecture.Tests;

/// <summary>Inspects actual wire metadata. A shape pin requests review; it never classifies a member as metadata.</summary>
internal static class JsonBoundaryContractGraph
{
    internal sealed record Snapshot(SortedDictionary<string, string> Pins, SortedDictionary<string, string> Shapes,
        SortedSet<string> LooseMembers, SortedSet<string> OpaqueConverters, IReadOnlyList<string> Errors);
    internal sealed record Contracts(string Owner, IEnumerable<Type> Roots, JsonSerializerOptions Options);

    internal static IReadOnlyList<JsonSerializerContext> Contexts(IEnumerable<Assembly> assemblies) => assemblies
        .SelectMany(assembly => assembly.GetTypes()).Where(type => !type.IsAbstract && typeof(JsonSerializerContext).IsAssignableFrom(type))
        .Select(type => (JsonSerializerContext?)(type.GetProperty("WireContext", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? type.GetProperty("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null))
            ?? throw new InvalidDataException("No constructed serializer context: " + type.FullName))
        .OrderBy(context => context.GetType().FullName, StringComparer.Ordinal).ToArray();

    internal static Snapshot Inspect(IEnumerable<JsonSerializerContext> contexts) => Inspect(contexts.Select(context => new Contracts(
        context.GetType().FullName!, CustomAttributeData.GetCustomAttributes(context.GetType())
            .Where(attribute => attribute.AttributeType == typeof(JsonSerializableAttribute))
            .Select(attribute => (Type)attribute.ConstructorArguments[0].Value!), context.Options)));

    internal static Snapshot Inspect(IEnumerable<Contracts> contexts)
    {
        var pins = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var shapes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var loose = new SortedSet<string>(StringComparer.Ordinal);
        var opaque = new SortedSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var context in contexts)
        {
            var owner = context.Owner;
            foreach (var root in context.Roots)
            {
                pins.Add("root|" + owner + "|" + Name(root), "declared");
                Visit(root, context.Options, owner);
            }
        }
        return new(pins, shapes, loose, opaque, errors);

        void Visit(Type type, JsonSerializerOptions options, string owner)
        {
            var identity = "shape|" + owner + "|" + Name(type);
            if (shapes.ContainsKey(identity)) return;
            if (Nullable.GetUnderlyingType(type) is { } nullable) { Visit(nullable, options, owner); return; }
            shapes.Add(identity, "pending");
            JsonTypeInfo info;
            try { info = options.GetTypeInfo(type); }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException)
            { errors.Add(identity + ": " + error.Message); return; }

            var properties = info.Properties.OrderBy(property => property.Name, StringComparer.Ordinal).Select(property => new
            {
                property.Name, Type = Name(property.PropertyType), property.Order, property.IsRequired, property.IsGetNullable, property.IsSetNullable,
                property.IsExtensionData, property.NumberHandling, property.ObjectCreationHandling,
                Converter = property.CustomConverter is { } converter ? Name(converter.GetType()) : null,
                HasGet = property.Get is not null, HasSet = property.Set is not null, HasPredicate = property.ShouldSerialize is not null
            }).ToArray();
            var shape = JsonSerializer.Serialize(new
            {
                Kind = info.Kind.ToString(), Converter = Name(info.Converter.GetType()), Properties = properties,
                Discriminator = info.PolymorphismOptions?.TypeDiscriminatorPropertyName,
                UnknownDerived = info.PolymorphismOptions?.UnknownDerivedTypeHandling,
                IgnoreUnknownDiscriminator = info.PolymorphismOptions?.IgnoreUnrecognizedTypeDiscriminators,
                Derived = info.PolymorphismOptions?.DerivedTypes.Select(derived => new { Type = Name(derived.DerivedType), Discriminator = derived.TypeDiscriminator })
                    .OrderBy(derived => derived.Type, StringComparer.Ordinal).ToArray(),
                Enum = type.IsEnum ? Enum.GetNames(type).Select(name => name + "=" + Enum.Format(type, Enum.Parse(type, name), "D")).ToArray() : null
            });
            shapes[identity] = shape;
            pins.Add(identity, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(shape))));
            if (info.Kind == JsonTypeInfoKind.None && !IsLeaf(type) && type != typeof(object))
                opaque.Add(owner + "|" + Name(type) + "|" + Name(info.Converter.GetType()));
            foreach (var property in info.Properties.Where(property => property.Get is not null || property.Set is not null))
            {
                if (ContainsObject(property.PropertyType)) loose.Add(owner + "|" + Name(type) + "." + property.Name);
                Visit(property.PropertyType, options, owner);
            }
            if (type.IsArray) Visit(type.GetElementType()!, options, owner);
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) Visit(argument, options, owner);
            if (info.PolymorphismOptions is { } polymorphism)
                foreach (var derived in polymorphism.DerivedTypes) Visit(derived.DerivedType, options, owner);
        }
    }

    internal static bool IsJsonData(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(type);

    internal static bool ContainsObject(Type type) => type == typeof(object) ||
        (type.IsArray && ContainsObject(type.GetElementType()!)) ||
        (type.IsGenericType && type.GetGenericArguments().Any(ContainsObject));

    private static bool IsLeaf(Type type) => IsJsonData(type) || type == typeof(string) || type.IsPrimitive || type.IsEnum ||
        type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
        type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(Uri) || type == typeof(byte[]);

    internal static string Name(Type type) => type.IsArray ? Name(type.GetElementType()!) + "[]" :
        type.IsGenericType ? type.GetGenericTypeDefinition().FullName!.Split('`')[0] + "<" + string.Join(',', type.GetGenericArguments().Select(Name)) + ">" : type.FullName!;

    internal static IReadOnlySet<string> DeclaredContexts(IEnumerable<string> sources)
    {
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()).ToArray();
        var globalAliases = trees.SelectMany(tree => tree.Usings.Where(usingDirective => usingDirective.GlobalKeyword.RawKind != 0 && usingDirective.Alias is not null))
            .ToDictionary(usingDirective => usingDirective.Alias!.Name.Identifier.ValueText, usingDirective => usingDirective.Name!.ToString(), StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            var aliases = new Dictionary<string, string>(globalAliases, StringComparer.Ordinal);
            foreach (var directive in tree.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(directive => directive.Alias is not null))
                aliases[directive.Alias!.Name.Identifier.ValueText] = directive.Name!.ToString();
            foreach (var declaration in tree.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (!declaration.AttributeLists.SelectMany(list => list.Attributes).Any(attribute => IsSerializable(attribute.Name.ToString(), aliases))) continue;
                var spaces = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(space => space.Name.ToString());
                var owners = declaration.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Append(declaration)
                    .Select(owner => owner.Identifier.ValueText + (owner.TypeParameterList is { Parameters.Count: > 0 } parameters ? "`" + parameters.Parameters.Count : ""));
                var prefix = string.Join('.', spaces);
                result.Add((prefix.Length == 0 ? "" : prefix + ".") + string.Join('+', owners));
            }
        }
        return result;
    }

    private static bool IsSerializable(string name, IReadOnlyDictionary<string, string> aliases)
    {
        name = name.Replace("global::", "", StringComparison.Ordinal);
        if (aliases.TryGetValue(name, out var alias)) name = alias.Replace("global::", "", StringComparison.Ordinal);
        var first = name.Split('.')[0];
        if (aliases.TryGetValue(first, out var space)) name = space + name[first.Length..];
        return name is "JsonSerializable" or "JsonSerializableAttribute" or "System.Text.Json.Serialization.JsonSerializable" or "System.Text.Json.Serialization.JsonSerializableAttribute";
    }
}
