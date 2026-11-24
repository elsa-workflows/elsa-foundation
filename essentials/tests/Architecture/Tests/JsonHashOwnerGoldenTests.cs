using System.Reflection;
using System.Text;
using System.Text.Json;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Calls shipping owners with fixed inputs; expected values come from the independent Python recipe.</summary>
public sealed class JsonHashOwnerGoldenTests
{
    [Fact]
    public void Shipping_hash_owners_pin_their_canonical_migration_inputs()
    {
        var configuration = typeof(JsonHashOwnerGoldenTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        using var inventory = ShippingHashAssemblies.Load(RepoPaths.RepoRoot, configuration);
        Assert.True(inventory.Errors.Count == 0, string.Join('\n', inventory.Errors));
        foreach (var vector in Vectors())
        {
            var type = inventory.Assemblies.Single(a => a.GetName().Name == vector.GetProperty("assembly").GetString())
                .GetType(vector.GetProperty("type").GetString()!, throwOnError: true)!;
            if (type.IsGenericTypeDefinition)
                type = type.MakeGenericType(ResolveType(vector.GetProperty("declaringType").GetString()!, vector.GetProperty("declaringAssembly").GetString()!, inventory.Assemblies));
            var arguments = vector.GetProperty("arguments").EnumerateArray().ToArray();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == vector.GetProperty("method").GetString() && m.GetParameters().Length == arguments.Length);
            if (vector.TryGetProperty("parameterType", out var selected))
                methods = methods.Where(m => m.GetParameters()[0].ParameterType.FullName == selected.GetString());
            var method = Assert.Single(methods);
            if (method.IsGenericMethodDefinition)
                method = method.MakeGenericMethod(vector.TryGetProperty("genericType", out var generic)
                    ? inventory.Assemblies.Single(a => a.GetName().Name == vector.GetProperty("genericAssembly").GetString()).GetType(generic.GetString()!, throwOnError: true)!
                    : typeof(JsonElement));
            var parameters = method.GetParameters();
            var values = arguments.Select((argument, index) => Argument(argument, parameters[index].ParameterType, inventory.Assemblies)).ToArray();
            if (vector.TryGetProperty("validateWith", out var validation))
                ResolveType(validation.GetProperty("type").GetString()!, validation.GetProperty("assembly").GetString()!, inventory.Assemblies)
                    .GetMethod(validation.GetProperty("method").GetString()!, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [values[0]]);
            var instance = vector.TryGetProperty("instanceKeyHex", out var key)
                ? Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [Convert.FromHexString(key.GetString()!)], null)
                : null;
            var returned = method.Invoke(instance, values);
            if (vector.TryGetProperty("returnMembers", out var members))
                foreach (var member in members.EnumerateObject())
                {
                    var actualMember = returned!.GetType().GetProperty(member.Name)!.GetValue(returned);
                    Assert.True(JsonCanonicalForm.Equals(member.Value, JsonSerializer.SerializeToElement(actualMember, JsonBaseline.Options)),
                        vector.GetProperty("name").GetString() + ": returned " + member.Name + " must preserve the supplied owner identity/payload.");
                }
            if (vector.TryGetProperty("returnProperty", out var returnedProperty))
                returned = returned!.GetType().GetProperty(returnedProperty.GetString()!)!.GetValue(returned);
            if (vector.GetProperty("expected").ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                Assert.True(Assert.IsType<bool>(returned) == vector.GetProperty("expected").GetBoolean(),
                    vector.GetProperty("name").GetString() + ": First-stable hash freeze: verifier changes require an explicit identity/migration decision.");
                continue;
            }
            if (vector.GetProperty("expected").ValueKind == JsonValueKind.Null)
            {
                Assert.True(returned is null, vector.GetProperty("name").GetString() + ": absent owner input must remain distinct from an explicit JSON null.");
                continue;
            }
            var actual = Assert.IsType<string>(returned);
            var expected = vector.GetProperty("expected").GetString();
            Assert.True(actual == expected, vector.GetProperty("name").GetString() +
                ": First-stable hash freeze: changes require an explicit identity/migration decision. Expected " + expected + "; actual " + actual);
        }
    }

    private static IEnumerable<JsonElement> Vectors()
    {
        foreach (var name in new[] { "owner-hash-vectors.json", "additional-owner-hash-vectors.json" })
        {
            using var stream = File.OpenRead(Path.Join(RepoPaths.RepoRoot, "specs/1207-json-data-integrity-baseline/evidence", name));
            using var document = JsonDocument.Parse(stream);
            foreach (var vector in document.RootElement.EnumerateArray()) yield return vector.Clone();
        }
    }

    private static Type ResolveType(string name, string assembly, IReadOnlyList<Assembly> assemblies) =>
        assemblies.Single(candidate => candidate.GetName().Name == assembly).GetType(name, throwOnError: true)!;

    private static object? Argument(JsonElement argument, Type type, IReadOnlyList<Assembly> assemblies)
    {
        if (argument.TryGetProperty("clrType", out var clrType))
            type = ResolveType(clrType.GetString()!, argument.GetProperty("clrAssembly").GetString()!, assemblies);
        if (argument.TryGetProperty("typedJson", out var typed)) return JsonSerializer.Deserialize(typed.GetString()!, type, JsonBaseline.Options);
        if (argument.TryGetProperty("objectArray", out var array)) return array.EnumerateArray().Select(value => value.ValueKind switch
        {
            JsonValueKind.String => (object?)value.GetString(), JsonValueKind.Null => null,
            JsonValueKind.True => true, JsonValueKind.False => false,
            JsonValueKind.Number => value.GetInt64(), _ => value.Clone()
        }).ToArray();
        if (argument.TryGetProperty("text", out var text)) return text.GetString();
        if (argument.TryGetProperty("keyHex", out var key)) return Convert.FromHexString(key.GetString()!);
        if (argument.TryGetProperty("utf8", out var utf8))
        {
            var bytes = Encoding.UTF8.GetBytes(utf8.GetString()!);
            if (type == typeof(byte[])) return bytes;
            return new ReadOnlyMemory<byte>(bytes);
        }
        if (argument.TryGetProperty("rawJson", out var raw))
        {
            using var document = JsonDocument.Parse(raw.GetString()!);
            return document.RootElement.Clone();
        }
        if (argument.TryGetProperty("ctor", out var fields))
        {
            var constructor = Assert.Single(type.GetConstructors().Where(c => c.GetParameters().All(p => fields.TryGetProperty(p.Name!, out _))));
            return constructor.Invoke(constructor.GetParameters().Select(p =>
                p.ParameterType == typeof(IReadOnlySet<string>)
                    ? fields.GetProperty(p.Name!).Deserialize<string[]>()!.ToHashSet(StringComparer.Ordinal)
                    : fields.GetProperty(p.Name!).Deserialize(p.ParameterType, JsonBaseline.Options)).ToArray());
        }
        var json = argument.GetProperty("json");
        if (type == typeof(object) || type == typeof(JsonElement)) return json.Clone();
        return json.Deserialize(type, JsonBaseline.Options);
    }
}
