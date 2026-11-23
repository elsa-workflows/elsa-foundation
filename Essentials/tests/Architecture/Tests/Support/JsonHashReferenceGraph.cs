using System.Reflection;
using System.Reflection.Emit;

namespace Elsa.Architecture.Tests;

/// <summary>Resolved compiled references for a reviewed hash recipe; syntax spelling never grants an exception.</summary>
internal sealed class JsonHashReferenceGraph
{
    private static readonly Lazy<Type?> NonCryptographicAlgorithm = new(() =>
        System.Type.GetType("System.IO.Hashing.NonCryptographicHashAlgorithm, System.IO.Hashing", throwOnError: false));
    internal sealed record Reference(string Owner, string Target, string Operation, int Occurrence, int Offset,
        string Role, IReadOnlyList<string> GenericArguments, string DeclaringTypeContext);

    internal sealed record Definition(string Identity, string Assembly, string Type, string Name, string Kind, bool HasDelegateParameter = false);

    internal IReadOnlyList<Reference> References { get; }
    internal IReadOnlyDictionary<string, Definition> Definitions { get; }
    internal IReadOnlyList<string> Errors { get; }
    private readonly IReadOnlyDictionary<string, string[]> dispatch;
    private readonly IReadOnlyDictionary<string, string[]> fieldWriters;
    private readonly IReadOnlyDictionary<string, Reference[]> outgoing;
    private readonly IReadOnlyDictionary<string, string[]> callers;

    private JsonHashReferenceGraph(List<Reference> references, Dictionary<string, Definition> definitions,
        List<string> errors, Dictionary<string, string[]> dispatch)
    {
        References = references.OrderBy(item => item.Owner, StringComparer.Ordinal).ThenBy(item => item.Offset).ToArray();
        Definitions = definitions;
        Errors = errors;
        this.dispatch = dispatch;
        outgoing = references.GroupBy(item => item.Owner, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        callers = references.GroupBy(item => item.Target, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Owner).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        fieldWriters = references.Where(item => item.Operation is "stfld" or "stsfld" or "ldflda" or "ldsflda")
            .GroupBy(item => item.Target, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Owner).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }

    /// <summary>Closes methods, dispatch and delegate-field initializers without claiming to infer arbitrary JSON dataflow.</summary>
    internal IReadOnlySet<string> ReachableDefinitions(string entry)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(entry);
        while (pending.TryPop(out var identity))
        {
            if (!found.Add(identity)) continue;
            if (dispatch.TryGetValue(identity, out var implementations))
                foreach (var implementation in implementations) pending.Push(implementation);
            if (fieldWriters.TryGetValue(identity, out var writers))
                foreach (var writer in writers)
                {
                    pending.Push(writer);
                    // An injected delegate is assigned by the constructor/setter's caller. Retain all
                    // observed callers so a new producer cannot borrow an existing helper's receipt.
                    if (Definitions.TryGetValue(identity, out var field) && field.Kind == "delegate-field" &&
                        Definitions.TryGetValue(writer, out var writerDefinition) && writerDefinition.HasDelegateParameter && callers.TryGetValue(writer, out var producers))
                    {
                        var producerQueue = new Stack<string>(producers);
                        var seenProducers = new HashSet<string>(StringComparer.Ordinal);
                        while (producerQueue.TryPop(out var producer))
                        {
                            if (!seenProducers.Add(producer)) continue;
                            pending.Push(producer);
                            if (Definitions.TryGetValue(producer, out var definition) && definition.HasDelegateParameter && callers.TryGetValue(producer, out var upstream))
                                foreach (var parent in upstream) producerQueue.Push(parent);
                        }
                    }
                }
            if (outgoing.TryGetValue(identity, out var references))
                foreach (var reference in references) pending.Push(reference.Target);
        }
        return found;
    }

    internal IReadOnlySet<string> ObservedCallers(string target) => References.Where(reference => reference.Target == target)
        .Select(reference => reference.Owner).ToHashSet(StringComparer.Ordinal);

    internal static JsonHashReferenceGraph Scan(IEnumerable<Assembly> input)
    {
        var assemblies = input.Distinct().ToArray();
        var errors = new List<string>();
        var references = new List<Reference>();
        var definitions = new Dictionary<string, Definition>(StringComparer.Ordinal);
        var methods = new Dictionary<string, MethodBase>(StringComparer.Ordinal);
        var runtimeMethods = new Dictionary<(Module, int), string>();
        var runtimeFields = new Dictionary<(Module, int), string>();
        var types = new List<Type>();
        foreach (var assembly in assemblies)
        {
            try { types.AddRange(assembly.GetTypes()); }
            catch (ReflectionTypeLoadException error)
            {
                errors.Add($"Cannot inspect all types in {assembly.GetName().Name}: {string.Join("; ", error.LoaderExceptions.Select(item => item?.Message))}");
            }
        }
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(flags))
            {
                var identity = FieldIdentity(field);
                Add(new(identity, type.Assembly.GetName().Name!, TypeName(type), field.Name,
                    typeof(Delegate).IsAssignableFrom(field.FieldType) ? "delegate-field" : "field"));
                runtimeFields[(field.Module, field.MetadataToken)] = identity;
            }
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags))
                         .Concat(type.TypeInitializer is { } initializer ? [initializer] : []).Distinct())
            {
                var identity = MethodIdentity(method);
                Add(new(identity, type.Assembly.GetName().Name!, TypeName(type), method.Name, "method",
                    method.GetParameters().Any(parameter => CarriesDelegate(parameter.ParameterType))));
                methods[identity] = method;
                runtimeMethods[(method.Module, method.MetadataToken)] = identity;
            }
        }
        foreach (var (owner, method) in methods)
        {
            var body = method.GetMethodBody();
            if (body is null) continue;
            var occurrences = new Dictionary<(string Target, string Operation), int>();
            foreach (var instruction in IlInstructionReader.Read(body.GetILAsByteArray()!))
            {
                if (instruction.OpCode == OpCodes.Calli)
                {
                    errors.Add($"Unresolved indirect function-pointer call: {owner} IL_{instruction.Offset:x4}.");
                    continue;
                }
                if (instruction.OpCode.OperandType is not (OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)) continue;
                try
                {
                    var member = method.Module.ResolveMember(instruction.Operand, method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null)
                        ?? throw new InvalidOperationException("The reference has no resolved member.");
                    var operation = instruction.OpCode.Name!;
                    var target = member switch
                    {
                        MethodBase called => runtimeMethods.GetValueOrDefault((called.Module, called.MetadataToken)) ?? DefinitionIdentity(called),
                        FieldInfo field => runtimeFields.GetValueOrDefault((field.Module, field.MetadataToken)) ?? FieldIdentity(field),
                        Type referenced => TypeIdentity(referenced),
                        _ => throw new InvalidOperationException($"Unsupported metadata member {member.MemberType}.")
                    };
                    var key = (target, operation);
                    var occurrence = occurrences.GetValueOrDefault(key) + 1;
                    occurrences[key] = occurrence;
                    var arguments = member is MethodInfo { IsGenericMethod: true } generic
                        ? generic.GetGenericArguments().Select(TypeName).ToArray() : [];
                    references.Add(new(owner, target, operation, occurrence, instruction.Offset, Role(member), arguments,
                        TypeName(member as Type ?? member.DeclaringType!)));
                }
                catch (Exception error) when (error is ArgumentException or BadImageFormatException or InvalidOperationException or TypeLoadException or FileNotFoundException)
                {
                    errors.Add($"Unresolved compiled reference: {owner} IL_{instruction.Offset:x4}: {error.GetType().Name}: {error.Message}");
                }
            }
        }
        var dispatch = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (type.IsInterface) continue;
            foreach (var contract in type.GetInterfaces())
            {
                InterfaceMapping map;
                try { map = type.GetInterfaceMap(contract); }
                catch (ArgumentException error) { errors.Add($"Unresolved interface dispatch: {TypeName(type)} / {TypeName(contract)}: {error.Message}"); continue; }
                for (var index = 0; index < map.InterfaceMethods.Length; index++)
                {
                    var from = map.InterfaceMethods[index];
                    var to = map.TargetMethods[index];
                    if (runtimeMethods.TryGetValue((to.Module, to.MetadataToken), out var target))
                        Edge(runtimeMethods.GetValueOrDefault((from.Module, from.MetadataToken)) ?? DefinitionIdentity(from), target);
                }
            }
            foreach (var method in type.GetMethods(flags).Where(method => method.IsVirtual))
            {
                var root = method.GetBaseDefinition();
                Edge(runtimeMethods.GetValueOrDefault((root.Module, root.MetadataToken)) ?? DefinitionIdentity(root), MethodIdentity(method));
            }
        }
        return new(references, definitions, errors,
            dispatch.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal));

        void Add(Definition definition)
        {
            if (!definitions.TryAdd(definition.Identity, definition)) errors.Add("Duplicate compiled identity: " + definition.Identity);
        }
        void Edge(string source, string target)
        {
            if (!dispatch.TryGetValue(source, out var values)) dispatch[source] = values = new(StringComparer.Ordinal);
            values.Add(target);
        }
    }

    private static string Role(MemberInfo member)
    {
        var type = member as Type ?? member.DeclaringType;
        if (type == typeof(Elsa.Serialization.Core.JsonHashInput))
            return "json-hash-input";
        if (type is not null && IsDigestType(type)) return member is Type ? "digest-type" : "digest-primitive";
        return "ordinary";
    }

    private static bool IsDigestType(Type type)
    {
        if (typeof(System.Security.Cryptography.HashAlgorithm).IsAssignableFrom(type) || type == typeof(System.Security.Cryptography.IncrementalHash)) return true;
        return NonCryptographicAlgorithm.Value?.IsAssignableFrom(type) == true;
    }

    private static bool CarriesDelegate(Type type)
    {
        if (type.IsByRef || type.IsArray) return CarriesDelegate(type.GetElementType()!);
        return type == typeof(object) || type.IsGenericParameter || typeof(Delegate).IsAssignableFrom(type) ||
            type.IsGenericType && type.GetGenericArguments().Any(CarriesDelegate);
    }

    internal static string MethodIdentity(MethodBase method) =>
        $"{method.DeclaringType!.Assembly.GetName().Name}|{TypeName(method.DeclaringType)}::{method.Name}" +
        (method.IsGenericMethod ? "<" + string.Join(',', method.GetGenericArguments().Select(TypeName)) + ">" : "") +
        "(" + string.Join(',', method.GetParameters().Select(parameter => TypeName(parameter.ParameterType))) + ")->" +
        (method is MethodInfo info ? TypeName(info.ReturnType) : "System.Void");

    private static string DefinitionIdentity(MethodBase method)
    {
        if (method.DeclaringType is { IsConstructedGenericType: true } owner)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var declaration = owner.GetGenericTypeDefinition();
            method = declaration.GetMethods(flags).Cast<MethodBase>().Concat(declaration.GetConstructors(flags))
                .Concat(declaration.TypeInitializer is { } initializer ? [initializer] : [])
                .First(candidate => candidate.Module == method.Module && candidate.MetadataToken == method.MetadataToken);
        }
        if (method is MethodInfo { IsGenericMethod: true } generic) method = generic.GetGenericMethodDefinition();
        return MethodIdentity(method);
    }

    private static string FieldIdentity(FieldInfo field) => $"{field.DeclaringType!.Assembly.GetName().Name}|{TypeName(field.DeclaringType)}::{field.Name}:{TypeName(field.FieldType)}";
    private static string TypeIdentity(Type type) => $"{type.Assembly.GetName().Name}|{TypeName(type)}";

    private static string TypeName(Type type)
    {
        if (type.IsGenericParameter) return (type.DeclaringMethod is null ? "!" : "!!") + type.GenericParameterPosition;
        if (type.IsByRef) return TypeName(type.GetElementType()!) + "&";
        if (type.IsPointer) return TypeName(type.GetElementType()!) + "*";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        return type.IsGenericType ? (type.GetGenericTypeDefinition().FullName ?? type.Name) + "[" + string.Join(',', type.GetGenericArguments().Select(TypeName)) + "]" : type.FullName ?? type.Name;
    }
}
