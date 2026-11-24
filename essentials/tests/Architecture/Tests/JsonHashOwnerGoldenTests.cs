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
        using var stream = File.OpenRead(Path.Join(RepoPaths.RepoRoot,
            "specs/1207-json-data-integrity-baseline/evidence/owner-hash-vectors.json"));
        using var vectors = JsonDocument.Parse(stream);
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            var type = inventory.Assemblies.Single(a => a.GetName().Name == vector.GetProperty("assembly").GetString())
                .GetType(vector.GetProperty("type").GetString()!, throwOnError: true)!;
            var arguments = vector.GetProperty("arguments").EnumerateArray().ToArray();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == vector.GetProperty("method").GetString() && m.GetParameters().Length == arguments.Length);
            if (vector.TryGetProperty("parameterType", out var selected))
                methods = methods.Where(m => m.GetParameters()[0].ParameterType.FullName == selected.GetString());
            var method = Assert.Single(methods);
            if (method.IsGenericMethodDefinition) method = method.MakeGenericMethod(typeof(JsonElement));
            var parameters = method.GetParameters();
            var values = arguments.Select((argument, index) => Argument(argument, parameters[index].ParameterType)).ToArray();
            var instance = vector.TryGetProperty("instanceKeyHex", out var key)
                ? Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [Convert.FromHexString(key.GetString()!)], null)
                : null;
            var actual = Assert.IsType<string>(method.Invoke(instance, values));
            var expected = vector.GetProperty("expected").GetString();
            Assert.True(actual == expected, vector.GetProperty("name").GetString() +
                ": First-stable hash freeze: changes require an explicit identity/migration decision. Expected " + expected + "; actual " + actual);
        }
    }

    private static object? Argument(JsonElement argument, Type type)
    {
        if (argument.TryGetProperty("text", out var text)) return text.GetString();
        if (argument.TryGetProperty("keyHex", out var key)) return Convert.FromHexString(key.GetString()!);
        if (argument.TryGetProperty("utf8", out var utf8)) return new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(utf8.GetString()!));
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
