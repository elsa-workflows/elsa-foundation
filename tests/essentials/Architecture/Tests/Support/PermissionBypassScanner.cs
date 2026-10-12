using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Architecture.Tests;

/// <summary>A compiled-code site that bypasses Foundation Identity's permission authorization.</summary>
/// <param name="Code">Which rule the site breaks; see <see cref="PermissionBypassScanner"/>.</param>
/// <param name="Assembly">The assembly the site compiles into.</param>
/// <param name="Type">The source-level type that owns the site, with compiler-generated closures and state machines folded into it.</param>
/// <param name="Method">The source-level method that owns the site, with lambdas, local functions and async bodies folded into it.</param>
/// <param name="Detail">The compiled method that holds the instruction, and the instruction's IL offset.</param>
internal sealed record PermissionBypassFinding(string Code, string Assembly, string Type, string Method, string Detail, string Message)
{
    public override string ToString() => $"{Code}: [{Assembly}] {Type}.{Method} ({Detail}): {Message}";
}

/// <summary>
/// A reviewed exception to the direct permission-claim rule: compiled code that legitimately reads permission claims,
/// because it is the shared evaluator itself or projects the normalized grants into a session or token. A
/// <see langword="null"/> <paramref name="Method"/> exempts the whole type.
/// </summary>
internal sealed record ReviewedPermissionClaimReader(string Assembly, string Type, string? Method, string Reason)
{
    internal bool Covers(PermissionBypassFinding finding) =>
        StringComparer.Ordinal.Equals(Assembly, finding.Assembly) &&
        StringComparer.Ordinal.Equals(Type, finding.Type) &&
        (Method is null || StringComparer.Ordinal.Equals(Method, finding.Method));
}

/// <summary>What one scan reported, what it exempted, and how much compiled code it looked at.</summary>
internal sealed record PermissionBypassScan(
    IReadOnlyList<PermissionBypassFinding> Findings,
    IReadOnlyList<(ReviewedPermissionClaimReader Reader, PermissionBypassFinding Finding)> Exempted,
    int Assemblies,
    int Types,
    int MethodBodies,
    int EndpointTypes,
    IReadOnlyDictionary<string, int> ClaimLookupsByType)
{
    /// <summary>The key <see cref="ClaimLookupsByType"/> uses for a type.</summary>
    internal static string TypeKey(string assembly, string type) => $"{assembly}|{type}";
}

/// <summary>
/// Inspects the IL of compiled assemblies for the two ways first-party code can bypass Foundation Identity's
/// permission authorization (<c>IPermissionAuthorizationService</c> and the policy metadata it evaluates):
/// <list type="bullet">
/// <item><description>
/// <c>AUTHZ-FASTENDPOINTS-PERMISSION</c>: calling, or taking a delegate to, a FastEndpoints <c>Permissions</c> or
/// <c>PermissionsAll</c> helper, meaning a method of that name declared on a type in the <c>FastEndpoints</c> namespace,
/// in an assembly whose name contains <c>FastEndpoints</c>, or on a type deriving from one. No exception applies.
/// </description></item>
/// <item><description>
/// <c>AUTHZ-DIRECT-PERMISSION-CLAIM</c>: reading permission claims directly, either by passing a permission value to
/// a claims lookup (<c>FindFirst</c>, <c>FindFirstValue</c>, <c>FindAll</c> or <c>HasClaim</c> declared on, or taking
/// as its first parameter, a <c>ClaimsPrincipal</c>, a <c>ClaimsIdentity</c> or another <c>System.Security.Claims.Claims*</c>
/// type), or by handing a principal's or identity's <c>Claims</c> collection, or a <c>FindAll</c> result, to a call
/// together with a permission value. Only <see cref="ReviewedPermissionClaimReader"/> sites are exempt.
/// </description></item>
/// </list>
/// A permission value is the <see cref="IdentityClaimTypes.Permission"/> claim type, a string equal to a constant whose
/// name or declaring type's name contains "Permission", a non-constant field so named, a local, argument, field or
/// property assigned a permission value anywhere in the scanned code, or a delegate to a method that loads one.
/// Constants are folded into their use sites at compile time, which is why they are matched by value. The scan
/// simulates the evaluation stack in forward passes over each method (ECMA-335 III.1.7.5), so it sees which value
/// reaches which argument, and repeats until no alias is learned, so an alias assigned in one method is seen where
/// another reads it.
/// </summary>
/// <remarks>
/// Values carried through arrays, collections or method return values (other than property getters), and a claim
/// taken out of the collection by a loop, are not tracked; the source scan this replaces did not track them either.
/// </remarks>
internal static class PermissionBypassScanner
{
    internal const string FastEndpointsPermission = "AUTHZ-FASTENDPOINTS-PERMISSION";
    internal const string DirectPermissionClaim = "AUTHZ-DIRECT-PERMISSION-CLAIM";

    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly HashSet<string> ClaimLookupNames = new(StringComparer.Ordinal) { "FindFirst", "FindFirstValue", "FindAll", "HasClaim" };
    private static readonly HashSet<string> FastEndpointsPermissionNames = new(StringComparer.Ordinal) { "Permissions", "PermissionsAll" };

    internal static PermissionBypassScan Scan(
        IReadOnlyCollection<Assembly> assemblies,
        IReadOnlyCollection<ReviewedPermissionClaimReader> reviewedReaders)
    {
        var types = assemblies.SelectMany(assembly => assembly.GetTypes()).ToArray();
        var methods = types
            .SelectMany(type => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)).Select(method => (Type: type, Method: method)))
            .Where(entry => entry.Method.GetMethodBody()?.GetILAsByteArray() is { Length: > 0 })
            .ToArray();
        var state = new ScanState(PermissionConstantValues(types));

        // Aliases found late in one sweep can change what an earlier method passes, so sweep until nothing new is
        // learned; the findings of the last sweep, which saw every alias, are the result.
        List<PermissionBypassFinding> findings;
        Dictionary<string, int> lookups;
        do
        {
            state.Changed = false;
            findings = [];
            lookups = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (type, method) in methods)
                new MethodSimulation(state, type, method, findings, lookups).Run();
        } while (state.Changed);

        var exempted = new List<(ReviewedPermissionClaimReader, PermissionBypassFinding)>();
        var reported = new List<PermissionBypassFinding>();
        foreach (var finding in findings)
        {
            var reader = finding.Code == DirectPermissionClaim ? reviewedReaders.FirstOrDefault(candidate => candidate.Covers(finding)) : null;
            if (reader is null)
                reported.Add(finding);
            else
                exempted.Add((reader, finding));
        }

        return new PermissionBypassScan(
            reported,
            exempted,
            assemblies.Count,
            types.Length,
            methods.Length,
            types.Count(IsEndpointType),
            lookups);
    }

    private static HashSet<string> PermissionConstantValues(IEnumerable<Type> types)
    {
        var values = new HashSet<string>(StringComparer.Ordinal) { IdentityClaimTypes.Permission };
        foreach (var field in types.SelectMany(type => type.GetFields(Declared)))
        {
            if (field is { IsLiteral: true } && field.FieldType == typeof(string) && IsPermissionNamed(field) &&
                field.GetRawConstantValue() is string { Length: > 0 } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static bool IsPermissionNamed(FieldInfo field) =>
        field.Name.Contains("Permission", StringComparison.OrdinalIgnoreCase) ||
        field.DeclaringType?.Name.Contains("Permission", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>A first-party endpoint class, or any type in an <c>Endpoints</c> namespace: the surface the deleted scan covered by folder.</summary>
    private static bool IsEndpointType(Type type) =>
        !IsCompilerGenerated(type) &&
        (type.Namespace?.Split('.').Contains("Endpoints", StringComparer.Ordinal) == true ||
         BaseTypes(type).Any(baseType => baseType.Namespace is "NativeEndpoints" || IsFastEndpointsType(baseType)));

    private static IEnumerable<Type> BaseTypes(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            yield return current;
    }

    private static bool IsFastEndpointsType(Type type) =>
        type.Namespace?.StartsWith("FastEndpoints", StringComparison.Ordinal) == true ||
        type.Assembly.GetName().Name?.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsFastEndpointsPermissionHelper(MethodBase method) =>
        FastEndpointsPermissionNames.Contains(method.Name) &&
        method.DeclaringType is { } declaringType &&
        (IsFastEndpointsType(declaringType) || BaseTypes(declaringType).Any(IsFastEndpointsType));

    private static bool IsClaimsType(Type? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name is "ClaimsPrincipal" or "ClaimsIdentity" ||
                current.FullName?.StartsWith("System.Security.Claims.Claims", StringComparison.Ordinal) == true)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="method"/> looks claims up by type; the receiver is always the first value it pops.</summary>
    private static bool IsClaimLookup(MethodBase method) =>
        ClaimLookupNames.Contains(method.Name) &&
        ((!method.IsStatic && IsClaimsType(method.DeclaringType)) ||
         (method.IsStatic && method.GetParameters() is [var first, ..] && IsClaimsType(first.ParameterType)));

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.StartsWith('<') || type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    /// <summary>The source-level name inside a compiler-generated name such as <c>&lt;HandleAsync&gt;b__4_0</c> or <c>&lt;&lt;Map&gt;b__0&gt;d</c>.</summary>
    private static string? SourceName(string generatedName)
    {
        if (!generatedName.StartsWith('<'))
            return null;

        var depth = 0;
        for (var index = 0; index < generatedName.Length; index++)
        {
            depth += generatedName[index] switch { '<' => 1, '>' => -1, _ => 0 };
            if (depth != 0)
                continue;

            var inner = generatedName[1..index];
            return inner.StartsWith('<') ? SourceName(inner) : inner.Length == 0 ? null : inner;
        }

        return null;
    }

    [Flags]
    private enum Taint : byte
    {
        None = 0,
        Permission = 1,
        ClaimsCollection = 2
    }

    /// <summary>What every sweep learns about members, shared across methods and assemblies.</summary>
    private sealed class ScanState(HashSet<string> permissionConstants)
    {
        private readonly Dictionary<(Module, int), Taint> _fields = [];
        private readonly Dictionary<(Module, int, string), Taint> _properties = [];
        private readonly HashSet<(Module, int)> _permissionLoadingMethods = [];

        public bool Changed { get; set; }

        public Taint String(string value) => permissionConstants.Contains(value) ? Taint.Permission : Taint.None;

        public Taint Field(FieldInfo field) =>
            (IsPermissionNamed(field) ? Taint.Permission : Taint.None) |
            _fields.GetValueOrDefault((field.Module, field.MetadataToken));

        public void AssignField(FieldInfo field, Taint value) =>
            Merge(_fields, (field.Module, field.MetadataToken), value);

        /// <summary>The taint a property getter returns: whatever its setter or its auto-property backing field was assigned.</summary>
        public Taint Property(MethodBase getter)
        {
            if (getter.DeclaringType is not { } type)
                return Taint.None;

            var name = getter.Name["get_".Length..];
            var taint = _properties.GetValueOrDefault((type.Module, type.MetadataToken, name));
            return type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic) is { } backingField
                ? taint | Field(backingField)
                : taint;
        }

        public void AssignProperty(MethodBase setter, Taint value)
        {
            if (setter.DeclaringType is { } type)
                Merge(_properties, (type.Module, type.MetadataToken, setter.Name["set_".Length..]), value);
        }

        public Taint DelegateTarget(MethodBase method) =>
            _permissionLoadingMethods.Contains((method.Module, method.MetadataToken)) ? Taint.Permission : Taint.None;

        public void MethodLoadsPermission(MethodBase method)
        {
            if (_permissionLoadingMethods.Add((method.Module, method.MetadataToken)))
                Changed = true;
        }

        private void Merge<TKey>(Dictionary<TKey, Taint> map, TKey key, Taint value) where TKey : notnull
        {
            var current = map.GetValueOrDefault(key);
            if ((current | value) == current)
                return;

            map[key] = current | value;
            Changed = true;
        }
    }

    /// <summary>One forward pass over a method body with a taint-carrying model of the evaluation stack.</summary>
    private sealed class MethodSimulation(
        ScanState state,
        Type type,
        MethodBase method,
        List<PermissionBypassFinding> findings,
        Dictionary<string, int> lookups)
    {
        private readonly Taint[] _locals = new Taint[method.GetMethodBody()!.LocalVariables.Count];
        private readonly Taint[] _arguments = new Taint[method.GetParameters().Length + (method.IsStatic ? 0 : 1)];
        private readonly Type[]? _typeArguments = type.IsGenericType ? type.GetGenericArguments() : null;
        private readonly Type[]? _methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        private readonly (string Assembly, string Type, string Method) _owner = Owner(type, method);
        private List<PermissionBypassFinding> _pending = [];
        private Dictionary<string, int> _pendingLookups = [];
        private bool _localsChanged;
        private bool _loadsPermission;

        public void Run()
        {
            var body = method.GetMethodBody()!;
            var instructions = IlInstructionReader.Read(body.GetILAsByteArray()!);

            // Locals and arguments are aliases for the whole body, so a value stored after a backward branch reaches
            // the loads before it: repeat the pass until no local or argument learns anything new.
            do
            {
                _localsChanged = false;
                _pending = [];
                _pendingLookups = [];
                Pass(body, instructions);
            } while (_localsChanged);

            findings.AddRange(_pending);
            foreach (var (key, count) in _pendingLookups)
                lookups[key] = lookups.GetValueOrDefault(key) + count;
            if (_loadsPermission)
                state.MethodLoadsPermission(method);
        }

        private void Pass(MethodBody body, IReadOnlyList<IlInstruction> instructions)
        {
            var entries = new Dictionary<int, List<Taint>>();
            foreach (var clause in body.ExceptionHandlingClauses)
            {
                if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                    entries[clause.FilterOffset] = [Taint.None];
                entries[clause.HandlerOffset] = clause.Flags is ExceptionHandlingClauseOptions.Clause or ExceptionHandlingClauseOptions.Filter
                    ? [Taint.None]
                    : [];
            }

            List<Taint>? stack = [];
            foreach (var instruction in instructions)
            {
                if (entries.TryGetValue(instruction.Offset, out var recorded))
                    stack = stack is null ? [.. recorded] : Join(stack, recorded, instruction);

                // ECMA-335 III.1.7.5: code that follows an unconditional transfer and is not the target of an earlier
                // forward branch starts with an empty stack.
                stack ??= [];
                Execute(instruction, stack);

                foreach (var target in instruction.Targets)
                    entries[target] = entries.TryGetValue(target, out var existing) ? Join(existing, stack, instruction) : [.. stack];

                if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Return or FlowControl.Throw)
                    stack = null;
            }
        }

        private List<Taint> Join(List<Taint> left, List<Taint> right, IlInstruction instruction)
        {
            if (left.Count != right.Count)
                throw Inconsistent(instruction, $"stack depths {left.Count} and {right.Count} meet");

            return [.. left.Zip(right, (first, second) => first | second)];
        }

        private void Execute(IlInstruction instruction, List<Taint> stack)
        {
            var code = instruction.OpCode;
            var name = code.Name!;
            switch (name)
            {
                case "ldstr":
                    Push(stack, state.String(method.Module.ResolveString(instruction.Operand)));
                    return;
                case "ldsfld" or "ldsflda":
                    Push(stack, state.Field(ResolveField(instruction)));
                    return;
                case "ldfld" or "ldflda":
                    Pop(stack, instruction);
                    Push(stack, state.Field(ResolveField(instruction)));
                    return;
                case "stsfld":
                    state.AssignField(ResolveField(instruction), Pop(stack, instruction));
                    return;
                case "stfld":
                    var assigned = Pop(stack, instruction);
                    Pop(stack, instruction);
                    state.AssignField(ResolveField(instruction), assigned);
                    return;
                case "dup":
                    var top = Pop(stack, instruction);
                    Push(stack, top);
                    Push(stack, top);
                    return;
                case "castclass" or "isinst" or "box" or "unbox" or "unbox.any":
                    Push(stack, Pop(stack, instruction));
                    return;
                case "call" or "callvirt" or "newobj":
                    Call(instruction, stack, ResolveMethod(instruction));
                    return;
                case "ldftn" or "ldvirtftn":
                    if (name == "ldvirtftn")
                        Pop(stack, instruction);
                    var target = ResolveMethod(instruction);
                    ReportFastEndpointsHelper(target, instruction);
                    Push(stack, state.DelegateTarget(target));
                    return;
                case "calli":
                    CallIndirect(instruction, stack);
                    return;
                case "ret":
                    if (method is MethodInfo { ReturnType: var returnType } && returnType != typeof(void))
                        Pop(stack, instruction);
                    return;
                case "leave" or "leave.s" or "endfinally":
                    stack.Clear();
                    return;
            }

            if (name.StartsWith("ldloc", StringComparison.Ordinal))
            {
                Push(stack, _locals[instruction.Operand]);
                return;
            }

            if (name.StartsWith("stloc", StringComparison.Ordinal))
            {
                Store(_locals, instruction.Operand, Pop(stack, instruction));
                return;
            }

            if (name.StartsWith("ldarg", StringComparison.Ordinal))
            {
                Push(stack, _arguments[instruction.Operand]);
                return;
            }

            if (name.StartsWith("starg", StringComparison.Ordinal))
            {
                Store(_arguments, instruction.Operand, Pop(stack, instruction));
                return;
            }

            for (var index = PopCount(code.StackBehaviourPop, instruction); index > 0; index--)
                Pop(stack, instruction);
            for (var index = PushCount(code.StackBehaviourPush, instruction); index > 0; index--)
                Push(stack, Taint.None);
        }

        private void Call(IlInstruction instruction, List<Taint> stack, MethodBase callee)
        {
            var isNewObject = instruction.OpCode == OpCodes.Newobj;
            var parameters = callee.GetParameters();
            var count = parameters.Length + (callee.IsStatic || isNewObject ? 0 : 1);
            var arguments = new Taint[count];
            for (var index = count - 1; index >= 0; index--)
                arguments[index] = Pop(stack, instruction);

            if (!ReportFastEndpointsHelper(callee, instruction))
            {
                if (IsClaimLookup(callee))
                {
                    var key = PermissionBypassScan.TypeKey(_owner.Assembly, _owner.Type);
                    _pendingLookups[key] = _pendingLookups.GetValueOrDefault(key) + 1;
                    if (arguments.Skip(1).Any(argument => argument.HasFlag(Taint.Permission)))
                    {
                        Report(DirectPermissionClaim, instruction,
                            $"{callee.DeclaringType?.Name}.{callee.Name} is passed a permission value; decide through IPermissionAuthorizationService instead.");
                    }
                }
                else if (arguments.Any(argument => argument.HasFlag(Taint.ClaimsCollection)) &&
                         arguments.Any(argument => argument.HasFlag(Taint.Permission)))
                {
                    Report(DirectPermissionClaim, instruction,
                        $"{callee.DeclaringType?.Name}.{callee.Name} inspects a principal's claims for a permission value; decide through IPermissionAuthorizationService instead.");
                }
            }

            if (callee.IsSpecialName && callee.Name.StartsWith("set_", StringComparison.Ordinal) && count > 0)
                state.AssignProperty(callee, arguments[^1]);

            if (isNewObject)
            {
                // A delegate carries the permission value its target method loads.
                var isDelegate = typeof(Delegate).IsAssignableFrom(callee.DeclaringType);
                Push(stack, isDelegate ? arguments.Aggregate(Taint.None, (all, argument) => all | argument) & Taint.Permission : Taint.None);
                return;
            }

            if (callee is MethodInfo { ReturnType: var returnType } && returnType != typeof(void))
                Push(stack, ResultTaint(callee));
        }

        private Taint ResultTaint(MethodBase callee)
        {
            if (IsClaimsType(callee.DeclaringType) && callee.Name is ("get_Claims" or "FindAll"))
                return Taint.ClaimsCollection;

            return callee.IsSpecialName && callee.Name.StartsWith("get_", StringComparison.Ordinal)
                ? state.Property(callee)
                : Taint.None;
        }

        private void CallIndirect(IlInstruction instruction, List<Taint> stack)
        {
            // StandAloneSig blob: calling convention, compressed parameter count, then the return type, which may be
            // preceded by custom modifiers (each a modifier byte and a compressed type token).
            var signature = method.Module.ResolveSignature(instruction.Operand);
            var position = 1;
            var parameterCount = ReadCompressed(signature, ref position);
            while (signature[position] is 0x1F or 0x20)
            {
                position++;
                ReadCompressed(signature, ref position);
            }

            var hasThis = (signature[0] & 0x20) != 0;
            for (var index = parameterCount + (hasThis ? 1 : 0) + 1; index > 0; index--)
                Pop(stack, instruction);
            if (signature[position] != 0x01)
                Push(stack, Taint.None);
        }

        private bool ReportFastEndpointsHelper(MethodBase callee, IlInstruction instruction)
        {
            if (!IsFastEndpointsPermissionHelper(callee))
                return false;

            Report(FastEndpointsPermission, instruction,
                $"{callee.DeclaringType?.Name}.{callee.Name} is a FastEndpoints permission helper; declare Foundation Identity policy metadata instead.");
            return true;
        }

        private void Report(string code, IlInstruction instruction, string message) =>
            _pending.Add(new PermissionBypassFinding(
                code,
                _owner.Assembly,
                _owner.Type,
                _owner.Method,
                $"{type.FullName}::{method.Name} IL_{instruction.Offset:x4}",
                message));

        private void Push(List<Taint> stack, Taint taint)
        {
            if (taint.HasFlag(Taint.Permission))
                _loadsPermission = true;
            stack.Add(taint);
        }

        private Taint Pop(List<Taint> stack, IlInstruction instruction)
        {
            if (stack.Count == 0)
                throw Inconsistent(instruction, "the evaluation stack underflows");

            var taint = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return taint;
        }

        private void Store(Taint[] slots, int index, Taint taint)
        {
            if ((slots[index] | taint) == slots[index])
                return;

            slots[index] |= taint;
            _localsChanged = true;
        }

        private int PopCount(StackBehaviour behaviour, IlInstruction instruction) => behaviour switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Varpop => throw Inconsistent(instruction, "an unmodelled variable-pop instruction"),
            _ => behaviour.ToString().Split('_').Length
        };

        private int PushCount(StackBehaviour behaviour, IlInstruction instruction) => behaviour switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Varpush => throw Inconsistent(instruction, "an unmodelled variable-push instruction"),
            _ => 1
        };

        private FieldInfo ResolveField(IlInstruction instruction) =>
            method.Module.ResolveField(instruction.Operand, _typeArguments, _methodArguments)
            ?? throw Inconsistent(instruction, "a field token does not resolve");

        private MethodBase ResolveMethod(IlInstruction instruction) =>
            method.Module.ResolveMethod(instruction.Operand, _typeArguments, _methodArguments)
            ?? throw Inconsistent(instruction, "a method token does not resolve");

        private InvalidOperationException Inconsistent(IlInstruction instruction, string problem) =>
            new($"The permission-bypass scan cannot model {type.FullName}::{method.Name} at IL_{instruction.Offset:x4} " +
                $"({instruction.OpCode.Name}): {problem}. Extend the scanner rather than skipping the method.");

        private static int ReadCompressed(byte[] blob, ref int position)
        {
            var first = blob[position];
            if ((first & 0x80) == 0)
            {
                position += 1;
                return first;
            }

            if ((first & 0xC0) == 0x80)
            {
                position += 2;
                return ((first & 0x3F) << 8) | blob[position - 1];
            }

            position += 4;
            return ((first & 0x1F) << 24) | (blob[position - 3] << 16) | (blob[position - 2] << 8) | blob[position - 1];
        }

        private static (string Assembly, string Type, string Method) Owner(Type type, MethodBase method)
        {
            var owner = type;
            var sourceMethod = SourceName(method.Name);
            while (owner.IsNested && IsCompilerGenerated(owner))
            {
                sourceMethod ??= SourceName(owner.Name);
                owner = owner.DeclaringType!;
            }

            return (type.Assembly.GetName().Name!, owner.FullName ?? owner.Name, sourceMethod ?? method.Name);
        }
    }
}
