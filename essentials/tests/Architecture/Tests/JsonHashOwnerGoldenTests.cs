using System.Reflection;
using System.Collections;
using System.Text;
using System.Text.Json;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Calls shipping owners with fixed inputs; expected values come from the independent Python recipe.</summary>
public sealed class JsonHashOwnerGoldenTests : IDisposable
{
    private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
    public void Dispose() => services.Dispose();
    [Fact]
    public void Shipping_hash_owners_pin_their_canonical_migration_inputs()
    {
        var configuration = typeof(JsonHashOwnerGoldenTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        using var inventory = ShippingHashAssemblies.Load(RepoPaths.RepoRoot, configuration);
        Assert.True(inventory.Errors.Count == 0, string.Join('\n', inventory.Errors));
        foreach (var vector in Vectors())
        {
            if (vector.TryGetProperty("platform", out var platform) &&
                (platform.GetString() == "windows") != OperatingSystem.IsWindows()) continue;
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
                method = method.MakeGenericMethod(vector.TryGetProperty("genericObject", out var genericObject) && genericObject.GetBoolean() ? typeof(object) : vector.TryGetProperty("genericType", out var generic)
                    ? inventory.Assemblies.Single(a => a.GetName().Name == vector.GetProperty("genericAssembly").GetString()).GetType(generic.GetString()!, throwOnError: true)!
                    : typeof(JsonElement));
            var parameters = method.GetParameters();
            var values = arguments.Select((argument, index) => Argument(argument, parameters[index].ParameterType, inventory.Assemblies)).ToArray();
            if (vector.TryGetProperty("validateWith", out var validation))
                ResolveType(validation.GetProperty("type").GetString()!, validation.GetProperty("assembly").GetString()!, inventory.Assemblies)
                    .GetMethod(validation.GetProperty("method").GetString()!, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [values[0]]);
            var instance = vector.TryGetProperty("instance", out var instanceArgument) ? Argument(instanceArgument, type, inventory.Assemblies)
                : vector.TryGetProperty("instanceKeyHex", out var key)
                ? Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [Convert.FromHexString(key.GetString()!)], null)
                : null;
            object? returned;
            try { returned = method.Invoke(instance, values); }
            catch (TargetInvocationException error) when (vector.TryGetProperty("expectedException", out _))
            {
                Assert.True(vector.GetProperty("expectedException").GetString() == error.InnerException!.GetType().FullName,
                    vector.GetProperty("name").GetString() + ": First-stable hash freeze: owner refusal type changed; changes require an explicit identity/migration decision. Actual " + error.InnerException.GetType().FullName);
                if (vector.TryGetProperty("exceptionCode", out var exceptionCode))
                    Assert.True(Equals(exceptionCode.GetString(), error.InnerException.GetType().GetProperty("Code")!.GetValue(error.InnerException)),
                        vector.GetProperty("name").GetString() + ": First-stable hash freeze: owner refusal code changed; changes require an explicit identity/migration decision.");
                continue;
            }
            finally { (instance as IDisposable)?.Dispose(); }
            Assert.True(!vector.TryGetProperty("expectedException", out _), vector.GetProperty("name").GetString() + ": First-stable hash freeze: the actual owner must preserve the expected refusal; changes require an explicit identity/migration decision.");
            if (vector.TryGetProperty("expectedBytesHex", out var expectedBytes))
            {
                Assert.True(returned is byte[] bytes && Convert.ToHexStringLower(bytes) == expectedBytes.GetString(),
                    vector.GetProperty("name").GetString() + ": First-stable hash freeze: verified payload bytes remain exact; changes require an explicit identity/migration decision.");
                continue;
            }
            if (vector.TryGetProperty("expectedOrdinalDigest", out var ordinalDigest))
            {
                Assert.True(Assert.IsType<int>(returned) == StringComparer.Ordinal.GetHashCode(ordinalDigest.GetString()!),
                    vector.GetProperty("name").GetString() + ": First-stable hash freeze: the stable digest is pinned independently of process-randomized ordinal hashing.");
                continue;
            }
            if (vector.GetProperty("expected").ValueKind == JsonValueKind.Number)
            {
                Assert.True(Assert.IsType<int>(returned) == vector.GetProperty("expected").GetInt32(),
                    vector.GetProperty("name").GetString() + ": First-stable hash freeze: absent-value hash behavior requires an explicit identity/migration decision to change.");
                continue;
            }
            if (vector.TryGetProperty("returnMembers", out var members))
                foreach (var member in members.EnumerateObject())
                {
                    var actualMember = returned!.GetType().GetProperty(member.Name)!.GetValue(returned);
                    Assert.True(JsonCanonicalForm.Equals(member.Value, JsonSerializer.SerializeToElement(actualMember, JsonBaseline.Options)),
                        vector.GetProperty("name").GetString() + ": First-stable hash freeze: returned " + member.Name + " must preserve the supplied owner identity/payload; changes require an explicit identity/migration decision.");
                }
            if (vector.TryGetProperty("returnFieldsJson", out var fields))
                foreach (var field in fields.EnumerateObject())
                {
                    var actualField = returned!.GetType().GetField(field.Name)!.GetValue(returned);
                    using var expectedField = JsonDocument.Parse(field.Value.GetString()!);
                    Assert.True(JsonCanonicalForm.Equals(expectedField.RootElement, JsonSerializer.SerializeToElement(actualField, JsonBaseline.Options)),
                        vector.GetProperty("name").GetString() + ": First-stable hash freeze: reconstructed " + field.Name + " must preserve normalized owner state; changes require an explicit identity/migration decision.");
                }
            if (vector.TryGetProperty("returnRawPaths", out var rawPaths))
                foreach (var path in rawPaths.EnumerateObject())
                    Assert.True(ReadPath(returned!, path.Name) is JsonElement raw && raw.GetRawText() == path.Value.GetString(),
                        vector.GetProperty("name").GetString() + "/" + path.Name + ": First-stable hash freeze: preserve the exact raw owner token; changes require an explicit identity/migration decision.");
            if (vector.TryGetProperty("returnProperty", out var returnedProperty))
                foreach (var member in returnedProperty.GetString()!.Split('.')) returned = returned!.GetType().GetProperty(member)!.GetValue(returned);
            if (vector.TryGetProperty("returnField", out var returnedField))
                returned = returned!.GetType().GetField(returnedField.GetString()!)!.GetValue(returned);
            if (vector.GetProperty("expected").ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                Assert.True(Assert.IsType<bool>(returned) == vector.GetProperty("expected").GetBoolean(),
                    vector.GetProperty("name").GetString() + ": First-stable hash freeze: verifier changes require an explicit identity/migration decision.");
                continue;
            }
            if (vector.GetProperty("expected").ValueKind == JsonValueKind.Null)
            {
                Assert.True(returned is null, vector.GetProperty("name").GetString() + ": First-stable hash freeze: absent owner input must remain distinct from explicit JSON null; changes require an identity/migration decision.");
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

    private static object? ReadPath(object value, string path)
    {
        object? current = value;
        foreach (var segment in path.Split('.'))
        {
            Assert.NotNull(current);
            var opening = segment.IndexOf('[');
            var name = opening < 0 ? segment : segment[..opening];
            var type = current.GetType();
            current = type.GetField(name) is { } field ? field.GetValue(current) : type.GetProperty(name)!.GetValue(current);
            if (opening >= 0) current = ((IEnumerable)current!).Cast<object?>().ElementAt(int.Parse(segment[(opening + 1)..^1], System.Globalization.CultureInfo.InvariantCulture));
        }
        return current;
    }

    private object? Argument(JsonElement argument, Type type, IReadOnlyList<Assembly> assemblies)
    {
        if (argument.TryGetProperty("systemTimeProvider", out _)) return TimeProvider.System;
        if (argument.TryGetProperty("emptyScopeFactory", out _)) return services.GetRequiredService<IServiceScopeFactory>();
        if (argument.TryGetProperty("baselineOptions", out _)) return JsonBaseline.Options;
        if (argument.TryGetProperty("clrNull", out _)) return null;
        if (argument.TryGetProperty("optionsValue", out var options))
        {
            var valueType = type.GetGenericArguments().Single();
            return Activator.CreateInstance(type.Assembly.GetType("Microsoft.Extensions.Options.OptionsWrapper`1", throwOnError: true)!.MakeGenericType(valueType),
                [Argument(options, valueType, assemblies)]);
        }
        if (argument.TryGetProperty("arrayArguments", out var elements))
        {
            var itemType = type.IsArray ? type.GetElementType()! : type.GetGenericArguments().Single();
            var result = Array.CreateInstance(itemType, elements.GetArrayLength());
            var index = 0;
            foreach (var element in elements.EnumerateArray()) result.SetValue(Argument(element, itemType, assemblies), index++);
            return result;
        }
        if (argument.TryGetProperty("clrType", out var clrType))
            type = ResolveType(clrType.GetString()!, argument.GetProperty("clrAssembly").GetString()!, assemblies);
        if (argument.TryGetProperty("propertyArguments", out var properties))
        {
            var instance = Argument(argument.GetProperty("value"), type, assemblies)!;
            foreach (var property in properties.EnumerateObject())
            {
                var member = type.GetProperty(property.Name)!;
                member.SetValue(instance, Argument(property.Value, member.PropertyType, assemblies));
            }
            return instance;
        }
        if (argument.TryGetProperty("constructorArguments", out var constructorFields))
        {
            var constructor = Assert.Single(type.GetConstructors(), candidate => candidate.GetParameters().All(parameter => constructorFields.TryGetProperty(parameter.Name!, out _)));
            return constructor.Invoke(constructor.GetParameters().Select(parameter => Argument(constructorFields.GetProperty(parameter.Name!), parameter.ParameterType, assemblies)).ToArray());
        }
        if (argument.TryGetProperty("dictionaryEntries", out var entries))
        {
            var valueType = type.GetGenericArguments()[1];
            var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;
            foreach (var entry in entries.EnumerateObject()) dictionary.Add(entry.Name, Argument(entry.Value, valueType, assemblies));
            return dictionary;
        }
        if (argument.TryGetProperty("typedJson", out var typed)) return JsonSerializer.Deserialize(typed.GetString()!, type, JsonBaseline.Options);
        if (argument.TryGetProperty("objectArray", out var array)) return array.EnumerateArray().Select(value => value.ValueKind switch
        {
            JsonValueKind.String => (object?)value.GetString(), JsonValueKind.Null => null,
            JsonValueKind.True => true, JsonValueKind.False => false,
            JsonValueKind.Number => value.GetInt64(), _ => value.Clone()
        }).ToArray();
        if (argument.TryGetProperty("text", out var text)) return text.GetString();
        if (argument.TryGetProperty("utf16", out var units)) return new string(units.EnumerateArray().Select(unit => checked((char)unit.GetInt32())).ToArray());
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
