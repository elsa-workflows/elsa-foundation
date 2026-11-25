using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Architecture.Tests;

/// <summary>Resolved call, producer, DTO and converter closure of one reviewed hash owner.</summary>
internal static class JsonHashRecipeReceipt
{
    internal sealed record Receipt(string Owner, string Signature, int Definitions, int Contracts, int Sources,
        IReadOnlyList<string> DefinitionIdentities, IReadOnlyList<string> ContractIdentities, IReadOnlyList<string> SourcePaths);
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    internal static Receipt Create(string owner, JsonHashReferenceGraph graph, IReadOnlyList<Assembly> assemblies,
        IReadOnlyDictionary<Guid, JsonHashSourceDocuments> sourceMaps, IReadOnlyDictionary<string, string>? externalSourceIdentities = null)
        => CreateMany([owner], graph, assemblies, sourceMaps, externalSourceIdentities);

    /// <summary>One conservative union receipt avoids repeatedly walking shared dependencies for every owner.</summary>
    internal static Receipt CreateMany(IEnumerable<string> owners, JsonHashReferenceGraph graph, IReadOnlyList<Assembly> assemblies,
        IReadOnlyDictionary<Guid, JsonHashSourceDocuments> sourceMaps, IReadOnlyDictionary<string, string>? externalSourceIdentities = null)
    {
        var entries = owners.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var owner = entries.Length == 1 ? entries[0] : "reviewed-recipe-union";
        if (entries.Length == 0 || graph.Errors.Count != 0 || entries.Any(entry => !graph.Definitions.ContainsKey(entry)))
            throw new InvalidOperationException("Cannot receipt unresolved hash owners/graph: " + string.Join(';', entries) + "; " + string.Join(';', graph.Errors));
        var modules = assemblies.SelectMany(a => a.Modules).ToDictionary(m => m.ModuleVersionId);
        foreach (var module in modules.Values)
        {
            if (!sourceMaps.TryGetValue(module.ModuleVersionId, out var map) || map.ImageMvid != module.ModuleVersionId || map.Errors.Count != 0)
                throw new InvalidOperationException("Hash receipt requires authenticated source for " + module.Name + ": " +
                    (map is null ? "missing source map" : string.Join(';', map.Errors)));
        }
        var kickoffDocuments = sourceMaps.ToDictionary(pair => pair.Key, pair => pair.Value.Methods.Values
            .Where(method => method.KickoffToken is not null).GroupBy(method => method.KickoffToken!.Value)
            .ToDictionary(group => group.Key, group => group.SelectMany(method => method.Documents).Distinct(StringComparer.Ordinal).ToArray()));
        var shipping = assemblies.ToHashSet();
        var authority = typeof(JsonHashInput);
        var authorityModules = assemblies.Where(JsonHashReferenceGraph.IsHashAuthorityAssembly).Select(assembly => assembly.ManifestModule.ModuleVersionId).ToHashSet();
        var stop = graph.Definitions.Values.Where(d => authorityModules.Contains(d.ModuleMvid) &&
                (d.RuntimeType == authority.FullName || d.RuntimeType?.StartsWith(authority.FullName + "+", StringComparison.Ordinal) == true))
            .Select(d => d.Identity).ToHashSet(StringComparer.Ordinal);
        // The authority itself is reviewed as an authority, rather than being its own stopped dependency.
        var authorityOwner = entries.All(stop.Contains);
        if (!authorityOwner && entries.Any(stop.Contains)) throw new InvalidOperationException("Review shared hash authority separately from its consumers.");
        if (authorityOwner) stop.Clear();
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var producers = new HashSet<string>(StringComparer.Ordinal);
        var incoming = new HashSet<JsonHashReferenceGraph.Reference>();
        var incomingBindings = new HashSet<JsonHashReferenceGraph.Binding>();
        var pending = new Queue<string>(entries);
        while (pending.TryDequeue(out var producer))
        {
            if (!producers.Add(producer)) continue;
            var observed = graph.IncomingReferences(producer);
            foreach (var reference in observed.References)
            {
                incoming.Add(reference);
                if (graph.Definitions.ContainsKey(reference.Owner)) pending.Enqueue(reference.Owner);
            }
            foreach (var binding in observed.Bindings) incomingBindings.Add(binding);
        }
        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        var types = new Queue<Type>();
        var processedTypes = new HashSet<Type>();
        var expandedSchemas = new HashSet<Type>();
        var outgoing = new HashSet<JsonHashReferenceGraph.Reference>();
        var outgoingBindings = new HashSet<JsonHashReferenceGraph.Binding>();
        var boundaries = new HashSet<string>(StringComparer.Ordinal);
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var methodShapes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var producer in producers) Include(producer);
        while (types.TryDequeue(out var type))
        {
            if (!processedTypes.Add(type)) continue;
            if (type.HasElementType) { types.Enqueue(type.GetElementType()!); continue; }
            foreach (var argument in type.GetGenericArguments()) types.Enqueue(argument);
            var identity = JsonHashReferenceGraph.ContractIdentity(type);
            if (!authorityOwner && JsonHashReferenceGraph.IsHashAuthorityType(type, includeNested: true))
            { contracts[identity] = "shared-json-hash-authority"; continue; }
            if (type.IsGenericParameter)
            {
                var scope = type.DeclaringMethod is { } declaring ? JsonHashReferenceGraph.CallableIdentity(declaring)
                    : JsonHashReferenceGraph.ContractIdentity(type.DeclaringType!);
                contracts[scope + "|" + identity] = "generic-parameter";
                foreach (var constraint in type.GetGenericParameterConstraints()) types.Enqueue(constraint);
                continue;
            }
            if (!shipping.Contains(type.Assembly)) { contracts[identity] = "external:" + type.Assembly.GetName().FullName; continue; }
            if (type.IsConstructedGenericType)
            {
                var schema = type.GetGenericTypeDefinition();
                contracts[identity] = Digest(new { Definition = JsonHashReferenceGraph.ContractIdentity(schema),
                    Arguments = type.GetGenericArguments().Select(JsonHashReferenceGraph.ContractIdentity).ToArray() });
                types.Enqueue(schema);
                continue;
            }
            if (!expandedSchemas.Add(type)) continue;
            if (type.BaseType is { } parent) types.Enqueue(parent);
            foreach (var contract in type.GetInterfaces()) types.Enqueue(contract);
            foreach (var constructor in type.GetConstructors(Members)) Include(JsonHashReferenceGraph.CallableIdentity(constructor));
            if (type.TypeInitializer is { } initializer) Include(JsonHashReferenceGraph.CallableIdentity(initializer));
            var properties = type.GetProperties(Members).OrderBy(p => p.Name, StringComparer.Ordinal).Select(p =>
            {
                if (p.GetMethod?.IsPublic == true || p.SetMethod?.IsPublic == true || p.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Text.Json.Serialization.JsonIncludeAttribute"))
                {
                    types.Enqueue(p.PropertyType);
                    if (p.GetMethod is { } getter) Include(JsonHashReferenceGraph.CallableIdentity(getter));
                    if (p.SetMethod is { } setter) Include(JsonHashReferenceGraph.CallableIdentity(setter));
                }
                return new { p.Name, Type = JsonHashReferenceGraph.ContractIdentity(p.PropertyType), Get = p.GetMethod?.Attributes.ToString(),
                    Set = p.SetMethod?.Attributes.ToString(), Attributes = Attributes(p.CustomAttributes) };
            }).ToArray();
            var fields = type.GetFields(Members).OrderBy(f => f.Name, StringComparer.Ordinal).Select(f =>
            {
                if (f.IsPublic || f.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Text.Json.Serialization.JsonIncludeAttribute")) types.Enqueue(f.FieldType);
                return new { f.Name, Type = JsonHashReferenceGraph.ContractIdentity(f.FieldType), Flags = f.Attributes.ToString(),
                    Constant = f.IsLiteral ? f.GetRawConstantValue() is { } constant
                        ? "value:" + JsonHashInput.OpaqueString(Convert.ToString(constant, System.Globalization.CultureInfo.InvariantCulture)!) : "null:" : null,
                    Attributes = Attributes(f.CustomAttributes) };
            }).ToArray();
            contracts[identity] = Digest(new { Type = identity, Flags = type.Attributes.ToString(), type.IsByRefLike,
                Base = type.BaseType is { } baseType ? JsonHashReferenceGraph.ContractIdentity(baseType) : null,
                Interfaces = type.GetInterfaces().Select(JsonHashReferenceGraph.ContractIdentity).Order(StringComparer.Ordinal).ToArray(),
                GenericConstraints = type.GetGenericArguments().Where(a => a.IsGenericParameter).Select(a => new
                { a.GenericParameterPosition, Flags = a.GenericParameterAttributes.ToString(), Constraints = a.GetGenericParameterConstraints().Select(JsonHashReferenceGraph.ContractIdentity).Order(StringComparer.Ordinal).ToArray() }).ToArray(),
                Attributes = Attributes(type.CustomAttributes), Properties = properties, Fields = fields });
            if (IsConverter(type) || IsConverterAttribute(type))
                foreach (var method in type.GetMethods(Members).Cast<MethodBase>().Concat(type.GetConstructors(Members))) Include(JsonHashReferenceGraph.CallableIdentity(method));
            // An external base constructor can call a first-party override or interface implementation.
            // Keep those callback bodies even though its external implementation is a versioned leaf.
            if (ExternalBase(type))
            {
                foreach (var method in type.GetMethods(Members).Where(method => method.IsVirtual)) Include(JsonHashReferenceGraph.CallableIdentity(method));
                if (!type.IsInterface && !type.IsGenericParameter)
                    foreach (var contract in type.GetInterfaces())
                        foreach (var target in type.GetInterfaceMap(contract).TargetMethods.Where(method => method is not null && shipping.Contains(method.Module.Assembly)))
                            Include(JsonHashReferenceGraph.CallableIdentity(target));
            }
        }
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var identity in definitions)
        {
            var definition = graph.Definitions[identity];
            if (definition.Kind != "method") continue;
            var map = sourceMaps[definition.ModuleMvid];
            if (!map.Methods.TryGetValue(definition.MetadataToken, out var method))
                throw new InvalidOperationException("Missing authenticated method source map: " + identity);
            var callable = modules[definition.ModuleMvid].ResolveMethod(definition.MetadataToken)!;
            var documents = method.Documents.Concat(kickoffDocuments[definition.ModuleMvid].GetValueOrDefault(definition.MetadataToken) ?? [])
                .Distinct(StringComparer.Ordinal).ToArray();
            if (callable.DeclaringType is { } declaring)
                documents = documents.Concat(map.TypeDocuments.GetValueOrDefault(declaring.MetadataToken) ?? [])
                    .Distinct(StringComparer.Ordinal).ToArray();
            if (documents.Length == 0 && callable.GetMethodBody() is not null && !CanWitnessWithoutDocument(callable))
                throw new InvalidOperationException("Authored hash dependency has no authenticated source document: " + identity);
            foreach (var path in documents)
            {
                var document = map.Documents[path];
                var portable = path;
                if (document.Origin == "external-compile")
                {
                    // Authentication already happened against the exact evaluated Compile item and PDB.
                    // This exact package-file mapping only removes machine-specific cache locations.
                    if (externalSourceIdentities is null || !externalSourceIdentities.TryGetValue(path, out portable))
                        throw new InvalidOperationException("External hash source needs a portable, exact owned identity: " + path);
                }
                var key = definition.Assembly + "|" + document.Origin + ":" + portable;
                var digest = document.PortableSha256 ?? throw new InvalidOperationException("Missing authenticated portable source digest: " + key);
                if (sources.TryGetValue(key, out var previous) && previous != digest)
                    throw new InvalidOperationException("Conflicting authenticated source contents for " + key);
                sources[key] = digest;
            }
        }
        var signature = Digest(new
        {
            Owners = entries, Incoming = OrderedReferences(incoming), IncomingBindings = OrderedBindings(incomingBindings),
            Producers = producers.Order(StringComparer.Ordinal), References = OrderedReferences(outgoing), Bindings = OrderedBindings(outgoingBindings),
            Boundaries = boundaries.Order(StringComparer.Ordinal),
            Bodies = bodies.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(), Methods = methodShapes.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
            Contracts = contracts.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(), Sources = sources.ToArray()
        });
        return new(owner, signature, definitions.Count, contracts.Count, sources.Count,
            definitions.Order(StringComparer.Ordinal).ToArray(), contracts.Keys.Order(StringComparer.Ordinal).ToArray(), sources.Keys.ToArray());

        bool ExternalBase(Type type)
        {
            for (var parent = type.BaseType; parent is not null && parent != typeof(object); parent = parent.BaseType)
                if (!shipping.Contains(parent.Assembly)) return true;
            return false;
        }

        void Include(string entry)
        {
            if (definitions.Contains(entry)) return;
            if (!graph.Definitions.ContainsKey(entry)) return;
            var closure = graph.InspectClosure(entry, stop, expanded);
            expanded.UnionWith(closure.InternalDefinitions);
            expanded.UnionWith(closure.ExternalLeaves);
            foreach (var reference in closure.References) outgoing.Add(reference);
            foreach (var binding in closure.Bindings) outgoingBindings.Add(binding);
            foreach (var boundary in closure.ExternalLeaves.Concat(closure.Boundaries)) boundaries.Add(boundary);
            foreach (var identity in closure.InternalDefinitions)
            {
                if (!definitions.Add(identity)) continue;
                var definition = graph.Definitions[identity];
                var module = modules[definition.ModuleMvid];
                if (definition.RuntimeType is { } runtimeType) types.Enqueue(module.Assembly.GetType(runtimeType, throwOnError: true)!);
                if (definition.Kind != "method") continue;
                var method = module.ResolveMethod(definition.MetadataToken)!;
                methodShapes[identity] = Digest(new { Flags = method.Attributes.ToString(), Implementation = method.GetMethodImplementationFlags().ToString(),
                    Attributes = Attributes(method.CustomAttributes), Parameters = method.GetParameters().Select(parameter => new
                    {
                        parameter.Name, Type = JsonHashReferenceGraph.ContractIdentity(parameter.ParameterType), Flags = parameter.Attributes.ToString(),
                        Default = parameter.HasDefaultValue ? parameter.RawDefaultValue is { } value
                            ? value.GetType().FullName + ":" + JsonHashInput.OpaqueString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!) : "null:" : null,
                        Attributes = Attributes(parameter.CustomAttributes)
                    }).ToArray() });
                foreach (var parameter in method.GetParameters()) types.Enqueue(parameter.ParameterType);
                if (method is MethodInfo info) types.Enqueue(info.ReturnType);
                if (method.GetMethodBody() is not { } body) continue;
                foreach (var local in body.LocalVariables) types.Enqueue(local.LocalType);
                var il = body.GetILAsByteArray()!;
                bodies[identity] = SemanticBody(method);
                foreach (var instruction in IlInstructionReader.Read(il))
                {
                    if (instruction.OpCode.OperandType is not (OperandType.InlineType or OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok)) continue;
                    var member = module.ResolveMember(instruction.Operand, method.DeclaringType?.GetGenericArguments(),
                        method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null);
                    if (member?.DeclaringType is { } declaring) types.Enqueue(declaring);
                    switch (member)
                    {
                        case Type referenced: types.Enqueue(referenced); break;
                        case FieldInfo field: types.Enqueue(field.FieldType); break;
                        case MethodInfo called:
                            foreach (var argument in called.GetGenericArguments()) types.Enqueue(argument);
                            foreach (var parameter in called.GetParameters()) types.Enqueue(parameter.ParameterType);
                            types.Enqueue(called.ReturnType);
                            break;
                    }
                }
            }
        }

        string[] Attributes(IEnumerable<CustomAttributeData> attributes) => attributes.Select(attribute =>
        {
            types.Enqueue(attribute.AttributeType);
            var values = attribute.ConstructorArguments.Select(Value)
                .Concat(attribute.NamedArguments.Select(a => a.MemberName + ":" + a.IsField + ":" + Value(a.TypedValue))).ToArray();
            return JsonHashReferenceGraph.MethodIdentity(attribute.Constructor) + ":" + string.Join('|', values);
        }).Order(StringComparer.Ordinal).ToArray();
        string Value(CustomAttributeTypedArgument argument)
        {
            var prefix = JsonHashReferenceGraph.ContractIdentity(argument.ArgumentType) + ":";
            if (argument.Value is Type value) { types.Enqueue(value); return prefix + JsonHashReferenceGraph.ContractIdentity(value); }
            if (argument.Value is IEnumerable<CustomAttributeTypedArgument> array) return prefix + "[" + string.Join(',', array.Select(Value)) + "]";
            return prefix + (argument.Value is null ? "null:" : "value:" + JsonHashInput.OpaqueString(Convert.ToString(argument.Value, System.Globalization.CultureInfo.InvariantCulture)!));
        }
    }

    private static bool IsConverter(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.FullName == "System.Text.Json.Serialization.JsonConverter") return true;
        return false;
    }
    internal static bool CanWitnessWithoutDocument(MethodBase method) =>
        CompilerGenerated(method) || TrivialBaseConstructor(method) || ValuePackingConstructor(method);

    private static bool CompilerGenerated(MethodBase method)
    {
        if (method.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)) return true;
        for (var type = method.DeclaringType; type is not null; type = type.DeclaringType)
            if (type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)) return true;
        return false;
    }
    private static bool TrivialBaseConstructor(MethodBase method)
    {
        if (method is not ConstructorInfo { IsStatic: false, DeclaringType: { IsClass: true } declaring } constructor ||
            constructor.GetParameters().Length != 0 || method.GetMethodBody() is not { } body ||
            body.LocalVariables.Count != 0 || body.ExceptionHandlingClauses.Count != 0) return false;
        // Converter closure includes every declared callback. Do not infer arbitrary external-base behavior
        // from a forwarding ctor alone; System.Object has no such callbacks.
        if (declaring.BaseType != typeof(object) && !IsConverter(declaring)) return false;
        var implementation = method.GetMethodImplementationFlags();
        if ((implementation & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL ||
            (implementation & MethodImplAttributes.ManagedMask) != MethodImplAttributes.Managed ||
            (implementation & (MethodImplAttributes.InternalCall | MethodImplAttributes.Synchronized)) != 0 ||
            (method.Attributes & MethodAttributes.PinvokeImpl) != 0) return false;
        var instructions = IlInstructionReader.Read(body.GetILAsByteArray()!).Where(instruction => instruction.OpCode != OpCodes.Nop).ToArray();
        return instructions.Length == 3 && instructions[0].OpCode == OpCodes.Ldarg_0 && instructions[1].OpCode == OpCodes.Call &&
            instructions[2].OpCode == OpCodes.Ret && method.Module.ResolveMethod(instructions[1].Operand, declaring.GetGenericArguments(), null)
                is ConstructorInfo { IsStatic: false } called && called.DeclaringType == declaring.BaseType && called.GetParameters().Length == 0;
    }
    private static bool ValuePackingConstructor(MethodBase method)
    {
        if (method is not ConstructorInfo { IsStatic: false, DeclaringType: { IsValueType: true } declaring } constructor ||
            constructor.GetParameters().Length == 0 || method.GetMethodBody() is not { } body ||
            body.LocalVariables.Count != 0 || body.ExceptionHandlingClauses.Count != 0) return false;
        var implementation = method.GetMethodImplementationFlags();
        if ((implementation & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL ||
            (implementation & MethodImplAttributes.ManagedMask) != MethodImplAttributes.Managed ||
            (implementation & (MethodImplAttributes.InternalCall | MethodImplAttributes.Synchronized)) != 0 ||
            (method.Attributes & MethodAttributes.PinvokeImpl) != 0) return false;
        var instructions = IlInstructionReader.Read(body.GetILAsByteArray()!).Where(instruction => instruction.OpCode != OpCodes.Nop).ToArray();
        var parameters = constructor.GetParameters();
        if (instructions.Length != parameters.Length * 3 + 1 || instructions[^1].OpCode != OpCodes.Ret) return false;
        var fields = new HashSet<FieldInfo>();
        for (var index = 0; index < parameters.Length; index++)
        {
            var target = instructions[index * 3 + 2];
            if (instructions[index * 3].OpCode != OpCodes.Ldarg_0 || ArgumentIndex(instructions[index * 3 + 1]) != index + 1 ||
                target.OpCode != OpCodes.Stfld || method.Module.ResolveField(target.Operand, declaring.GetGenericArguments(), null) is not { } field ||
                field.DeclaringType != declaring || field.FieldType != parameters[index].ParameterType ||
                !field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false) || !fields.Add(field)) return false;
        }
        return true;

        static int ArgumentIndex(IlInstruction instruction) => instruction.OpCode == OpCodes.Ldarg_1 ? 1 : instruction.OpCode == OpCodes.Ldarg_2 ? 2
            : instruction.OpCode == OpCodes.Ldarg_3 ? 3 : instruction.OpCode == OpCodes.Ldarg || instruction.OpCode == OpCodes.Ldarg_S ? instruction.Operand : -1;
    }
    private static bool IsConverterAttribute(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.FullName == "System.Text.Json.Serialization.JsonConverterAttribute") return true;
        return false;
    }
    internal static string SemanticBody(MethodBase method)
    {
        var body = method.GetMethodBody() ?? throw new InvalidOperationException("No method body: " + method);
        var il = body.GetILAsByteArray()!;
        var instructions = IlInstructionReader.Read(il);
        var indices = instructions.Select((instruction, index) => (instruction.Offset, index)).ToDictionary(p => p.Offset, p => p.index);
        indices[il.Length] = instructions.Count;
        var typeArguments = method.DeclaringType?.GetGenericArguments();
        var methodArguments = method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null;
        var code = instructions.Select((instruction, index) =>
        {
            string operand;
            if (instruction.Targets.Length != 0) operand = "targets:" + string.Join(',', instruction.Targets.Select(offset => indices[offset]));
            else if (instruction.OpCode.OperandType == OperandType.InlineString)
                operand = "string:" + JsonHashInput.OpaqueString(method.Module.ResolveString(instruction.Operand));
            else if (instruction.OpCode.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
                operand = method.Module.ResolveMember(instruction.Operand, typeArguments, methodArguments) switch
                {
                    Type type => JsonHashReferenceGraph.ContractIdentity(type),
                    MethodBase called => JsonHashReferenceGraph.MethodIdentity(called),
                    FieldInfo field => JsonHashReferenceGraph.ContractIdentity(field.DeclaringType!) + "::" + field.Name + ":" + JsonHashReferenceGraph.ContractIdentity(field.FieldType),
                    _ => throw new InvalidOperationException("Unresolved body operand in " + method)
                };
            else if (instruction.OpCode.OperandType == OperandType.InlineSig)
                throw new InvalidOperationException("An indirect-call signature cannot borrow a reviewed hash receipt: " + method);
            else
            {
                var start = instruction.Offset + instruction.OpCode.Size;
                var end = index + 1 == instructions.Count ? il.Length : instructions[index + 1].Offset;
                operand = Convert.ToHexStringLower(il.AsSpan(start, end - start));
            }
            return new { Opcode = instruction.OpCode.Name, Operand = operand };
        }).ToArray();
        var regions = body.ExceptionHandlingClauses.Select(region => new
        {
            Flags = region.Flags.ToString(), Try = indices[region.TryOffset], TryEnd = indices[region.TryOffset + region.TryLength],
            Handler = indices[region.HandlerOffset], HandlerEnd = indices[region.HandlerOffset + region.HandlerLength],
            Filter = region.Flags == ExceptionHandlingClauseOptions.Filter ? indices[region.FilterOffset] : -1,
            Catch = region.Flags == ExceptionHandlingClauseOptions.Clause && region.CatchType is { } type ? JsonHashReferenceGraph.ContractIdentity(type) : null
        }).ToArray();
        return Digest(new { body.InitLocals, body.MaxStackSize, Code = code, Regions = regions,
            Locals = body.LocalVariables.Select(local => new { Type = JsonHashReferenceGraph.ContractIdentity(local.LocalType), local.IsPinned }).ToArray() });
    }
    private static IEnumerable<JsonHashReferenceGraph.Reference> OrderedReferences(IEnumerable<JsonHashReferenceGraph.Reference> references) =>
        references.OrderBy(r => r.Owner, StringComparer.Ordinal).ThenBy(r => r.Offset).ThenBy(r => r.Target, StringComparer.Ordinal)
            .ThenBy(r => r.Operation, StringComparer.Ordinal).ThenBy(r => r.Occurrence).ThenBy(r => r.Role, StringComparer.Ordinal)
            .ThenBy(r => r.DeclaringTypeContext, StringComparer.Ordinal).ThenBy(r => string.Join(',', r.GenericArguments), StringComparer.Ordinal);
    private static IEnumerable<JsonHashReferenceGraph.Binding> OrderedBindings(IEnumerable<JsonHashReferenceGraph.Binding> bindings) =>
        bindings.OrderBy(b => b.Kind, StringComparer.Ordinal).ThenBy(b => b.Source, StringComparer.Ordinal).ThenBy(b => b.Target, StringComparer.Ordinal);

    internal static string Digest<T>(T value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new HashingWriteStream(hash);
        JsonSerializer.Serialize(stream, value);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed class HashingWriteStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
