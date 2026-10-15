using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Claims;
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
/// A reviewed exception to the direct permission-claim rule: one source-level method that legitimately reads
/// permission claims, because it is the shared evaluator itself, strips incoming internal claims, or projects the
/// normalized grants into a session, a token or an identity.
/// </summary>
internal sealed record ReviewedPermissionClaimReader(string Assembly, string Type, string Method, string Reason)
{
    internal bool Covers(PermissionBypassFinding finding) =>
        StringComparer.Ordinal.Equals(Assembly, finding.Assembly) &&
        StringComparer.Ordinal.Equals(Type, finding.Type) &&
        StringComparer.Ordinal.Equals(Method, finding.Method);
}

/// <summary>What one scan reported, what it exempted, and how much compiled code it looked at.</summary>
internal sealed record PermissionBypassScan(
    IReadOnlyList<PermissionBypassFinding> Findings,
    IReadOnlyList<(ReviewedPermissionClaimReader Reader, PermissionBypassFinding Finding)> Exempted,
    int Assemblies,
    int MethodBodies,
    int EndpointTypes,
    IReadOnlyDictionary<string, int> ClaimLookupsByType,
    IReadOnlyCollection<string> ContributedPermissionKeys)
{
    /// <summary>The key <see cref="ClaimLookupsByType"/> uses for a type.</summary>
    internal static string TypeKey(string assembly, string type) => $"{assembly}|{type}";
}

/// <summary>
/// Inspects the IL of compiled assemblies for the ways first-party code can bypass Foundation Identity's permission
/// authorization (<c>IPermissionAuthorizationService</c> and the policy metadata it evaluates):
/// <list type="bullet">
/// <item><description>
/// <c>AUTHZ-FASTENDPOINTS-PERMISSION</c>: calling, or taking a delegate to, a FastEndpoints <c>Permissions</c> or
/// <c>PermissionsAll</c> helper, meaning a method of that name declared on a type in the <c>FastEndpoints</c> namespace,
/// in an assembly whose name contains <c>FastEndpoints</c>, or on a type deriving from one. No exception applies.
/// </description></item>
/// <item><description>
/// <c>AUTHZ-DIRECT-PERMISSION-CLAIM</c>, three ways. A claims lookup (<c>FindFirst</c>, <c>FindFirstValue</c>,
/// <c>FindAll</c> or <c>HasClaim</c>, as an instance method of a <c>ClaimsPrincipal</c>, a <c>ClaimsIdentity</c> or
/// another <c>System.Security.Claims.Claims*</c> type, or as a static method whose first parameter can take a principal
/// or an identity: <c>IPrincipal</c>, <c>IIdentity</c>, <c>object</c> or a claims type) is passed a permission value
/// in any argument but the principal. A claims collection (see below) is handed to a call together with a permission
/// value. Or a scanned method is passed a permission value in a parameter that the method, or a method it calls, uses
/// in one of those two ways. Only <see cref="ReviewedPermissionClaimReader"/> methods are exempt.
/// </description></item>
/// </list>
/// <para>
/// A permission value is the <see cref="IdentityClaimTypes.Permission"/> claim type, a key that a permission contributor
/// in the scanned assemblies adds to the catalog (each <see cref="IPermissionContributor"/> is constructed and asked for
/// its <see cref="Permission"/> entries, so the keys are exactly the catalog's), or the value of a constant whose
/// source-level name contains "Permission". Constants are folded into their use sites at compile time, which is why
/// they are matched by value, and only values that read like a namespaced key (letters and a <c>.</c>, <c>:</c> or
/// <c>-</c>) count, so the bare wildcard <c>*</c> and plain words, which also appear in unrelated code, do not. A field
/// holds permission values when its source-level name contains "Permission" (the property name for an auto-property
/// backing field, the variable or parameter name for a closure, iterator or async state-machine field) and its type can
/// hold one, or when it is a static string-collection or delegate field of a type whose name contains "Permission".
/// Other constants and fields of such a type, an owner id or a category among them, are not permission values.
/// </para>
/// <para>
/// A permission value flows through locals, arguments (including <c>in</c> and <c>ref</c> reads), fields and properties
/// it is assigned to anywhere in the scanned code; into the result of a call or object creation that takes it as an
/// input, receiver included, when the result's type can hold a permission value (a string, a string collection, a
/// delegate, <see cref="object"/> or a generic parameter); and out of a scanned method that returns it. A delegate
/// carries a permission value when its target method loads one, or decides on one: compares a claim's own type, value
/// or issuer with a permission value (equality, <c>StartsWith</c>, <c>Contains</c> and the like), passes a claim member
/// to a scanned method that compares that parameter with one, or calls a scanned method that returns such a decision
/// or branches on one. A result computed from a permission value by any other call,
/// such as an <c>IPermissionAuthorizationService</c> decision, is not one.
/// </para>
/// <para>
/// A claims collection is a principal's <c>Claims</c> or <c>Identities</c>, a <c>FindAll</c> result, anything of a type
/// that can hold claims or identities computed from one (<c>IEnumerable&lt;Claim&gt;</c> and its implementations,
/// arrays, lists, lookups and dictionaries of them), and a string collection projected out of one by a selector that
/// reads a claim member (<c>Select(claim =&gt; claim.Value)</c>). So <c>ToList()</c>, <c>AsEnumerable()</c>,
/// <c>SelectMany</c> over identities, helpers that return claims and <c>Select(...).Contains(key)</c> all keep the mark.
/// </para>
/// <para>
/// Each method's parameters are summarized: a parameter is a sink when its value reaches a claims read inside the
/// method, directly, through a closure, iterator or async state machine, or through another summarized method it calls.
/// A call that passes a permission value into a sink parameter is reported at the call site. The scan simulates the
/// evaluation stack in forward passes over each method, which IL's own rules make exact without a control-flow graph,
/// and repeats every method until no alias, return or summary is learned.
/// </para>
/// </summary>
/// <remarks>
/// Not followed: values carried in array, collection or tuple elements; a claim taken out of the collection by a loop;
/// a lookup or grouping built with an element selector, such as <c>ToLookup(claim =&gt; claim.Type, claim =&gt;
/// claim.Value)</c>, whose element type is no longer a claim;
/// a claims collection that a helper receives as a parameter rather than reading it from a principal itself; values
/// that pass through a type that cannot hold them by the rules above, such as a <c>StringBuilder</c>, a tuple or the
/// <c>Task</c> an async method returns; values written back through <c>ref</c> or <c>out</c> parameters; calls made
/// through an interface, a virtual or abstract method, or a delegate, which are not resolved to their implementations;
/// and code outside the scanned assemblies. A permission contributor the scan cannot construct fails it.
/// </remarks>
internal static class PermissionBypassScanner
{
    internal const string FastEndpointsPermission = "AUTHZ-FASTENDPOINTS-PERMISSION";
    internal const string DirectPermissionClaim = "AUTHZ-DIRECT-PERMISSION-CLAIM";

    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly HashSet<string> ClaimLookupNames = new(StringComparer.Ordinal) { "FindFirst", "FindFirstValue", "FindAll", "HasClaim" };
    private static readonly HashSet<string> FastEndpointsPermissionNames = new(StringComparer.Ordinal) { "Permissions", "PermissionsAll" };

    /// <summary>Calls that compare one string with another, or look one up in a collection.</summary>
    private static readonly HashSet<string> ComparisonNames = new(StringComparer.Ordinal)
    {
        "op_Equality", "op_Inequality", "Equals", "Compare", "CompareOrdinal", "CompareTo", "StartsWith", "EndsWith", "Contains", "IndexOf"
    };

    internal static PermissionBypassScan Scan(
        IReadOnlyCollection<Assembly> assemblies,
        IReadOnlyCollection<ReviewedPermissionClaimReader> reviewedReaders)
    {
        var types = assemblies.SelectMany(assembly => assembly.GetTypes()).ToArray();
        var methods = types
            .SelectMany(type => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)).Select(method => (Type: type, Method: method)))
            .Where(entry => entry.Method.GetMethodBody()?.GetILAsByteArray() is { Length: > 0 })
            .ToArray();
        var contributedKeys = ContributedPermissionKeys(types);
        var state = new ScanState([.. NamedPermissionConstants(types), .. contributedKeys]);

        // What one method learns (an alias, a returned value, a sink parameter) can change what an earlier
        // method passes, so sweep until nothing new is learned; the findings of the last sweep, which saw it all, are
        // the result.
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
            methods.Length,
            types.Count(IsEndpointType),
            lookups,
            contributedKeys);
    }

    /// <summary>The claim type and the namespaced values of constants whose source-level name contains "Permission".</summary>
    private static HashSet<string> NamedPermissionConstants(IEnumerable<Type> types)
    {
        var values = new HashSet<string>(StringComparer.Ordinal) { IdentityClaimTypes.Permission };
        foreach (var field in types.SelectMany(type => type.GetFields(Declared)))
        {
            if (field is { IsLiteral: true } && field.FieldType == typeof(string) && HasPermissionName(field) &&
                field.GetRawConstantValue() is string value && IsNamespacedKey(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// The namespaced keys every <see cref="IPermissionContributor"/> in the scanned assemblies contributes to the
    /// permission catalog. Each contributor is constructed and asked, so the keys are exactly what the catalog will
    /// hold, however the contributor builds them; one that cannot be constructed fails the scan rather than being
    /// skipped.
    /// </summary>
    private static HashSet<string> ContributedPermissionKeys(IEnumerable<Type> types)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types.Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IPermissionContributor).IsAssignableFrom(type)))
        {
            IPermissionContributor contributor;
            try
            {
                contributor = (IPermissionContributor)Activator.CreateInstance(type, nonPublic: true)!;
            }
            catch (Exception exception) when (exception is MissingMethodException or ArgumentException or NotSupportedException or TargetInvocationException or MemberAccessException)
            {
                throw new InvalidOperationException(
                    $"The permission-bypass scan cannot construct the permission contributor {type.FullName} to read the keys it " +
                    "declares. Give it a parameterless constructor or teach the scan how to build it.", exception);
            }

            keys.UnionWith(contributor.Contribute().Select(permission => permission.Key).Where(IsNamespacedKey));
        }

        return keys;
    }

    /// <summary>Letters and a <c>.</c>, <c>:</c> or <c>-</c>, the shape of a permission key such as <c>workflows/design:read</c>.</summary>
    private static bool IsNamespacedKey(string value) =>
        value.Any(char.IsLetter) && value.IndexOfAny(['.', ':', '-']) >= 0;

    /// <summary>Whether <paramref name="field"/> holds permission values, by its own name or as a collection or delegate on a permission-named type.</summary>
    private static bool IsPermissionField(FieldInfo field) =>
        (HasPermissionName(field) && CanHoldPermissionValue(field.FieldType)) ||
        (field.IsStatic &&
         field.DeclaringType is { } declaringType &&
         !IsCompilerGenerated(declaringType) &&
         !field.Name.StartsWith('<') &&
         declaringType.Name.Contains("Permission", StringComparison.OrdinalIgnoreCase) &&
         field.FieldType != typeof(string) &&
         (typeof(IEnumerable<string>).IsAssignableFrom(field.FieldType) || typeof(Delegate).IsAssignableFrom(field.FieldType)));

    private static bool HasPermissionName(FieldInfo field) =>
        FieldSourceName(field).Contains("Permission", StringComparison.OrdinalIgnoreCase);

    /// <summary>A type a permission value can be stored in or returned as: a string, a string collection, a delegate, <see cref="object"/> or a generic parameter.</summary>
    private static bool CanHoldPermissionValue(Type type) =>
        type == typeof(string) || type == typeof(object) || type.IsGenericParameter ||
        IsStringCollection(type) || typeof(Delegate).IsAssignableFrom(type);

    /// <summary>
    /// A collection of strings, a span or memory of them included: C# binds <c>array.Contains(value)</c> through a
    /// <c>ReadOnlySpan&lt;string&gt;</c>.
    /// </summary>
    private static bool IsStringCollection(Type type) =>
        typeof(IEnumerable<string>).IsAssignableFrom(type) ||
        (type.IsGenericType &&
         type.GetGenericArguments() is [var element] && element == typeof(string) &&
         type.GetGenericTypeDefinition() is var definition &&
         (definition == typeof(ReadOnlySpan<>) || definition == typeof(Span<>) ||
          definition == typeof(ReadOnlyMemory<>) || definition == typeof(Memory<>)));

    /// <summary>
    /// A type a claims collection can be stored in or returned as: one whose element or type arguments are claims,
    /// identities or principals, at any depth (arrays, lists, lookups, dictionaries), or <see cref="object"/> or a generic
    /// parameter. A single principal, identity or claim is not a collection, and a predicate over claims holds none.
    /// </summary>
    private static bool CanHoldClaims(Type type) =>
        type == typeof(object) || type.IsGenericParameter || (!typeof(Delegate).IsAssignableFrom(type) && ContainsClaims(type, depth: 0));

    private static bool ContainsClaims(Type type, int depth) =>
        depth < 4 &&
        (type.IsArray
            ? ContainsClaims(type.GetElementType()!, depth + 1)
            : type.IsGenericType
                ? type.GetGenericArguments().Any(argument => ContainsClaims(argument, depth + 1))
                : depth > 0 && (type == typeof(Claim) || IsClaimsType(type)));

    /// <summary>
    /// The source-level name of a field: the property behind a backing field, the variable behind a hoisted or captured
    /// one, the parameter behind an iterator's copy of it.
    /// </summary>
    private static string FieldSourceName(FieldInfo field) =>
        field.Name.StartsWith(IteratorParameterPrefix, StringComparison.Ordinal)
            ? field.Name[IteratorParameterPrefix.Length..]
            : SourceName(field.Name) ?? field.Name;

    /// <summary>The prefix of the field an iterator keeps its parameter in until <c>GetEnumerator</c> copies it.</summary>
    private const string IteratorParameterPrefix = "<>3__";

    /// <summary>A field of a closure or state machine that carries a captured variable or parameter, not the compiler's own bookkeeping.</summary>
    private static bool IsCapturedVariable(FieldInfo field) =>
        field.DeclaringType is { } declaringType && IsCompilerGenerated(declaringType) &&
        (!field.Name.Contains("<>", StringComparison.Ordinal) || field.Name.StartsWith(IteratorParameterPrefix, StringComparison.Ordinal));

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

    /// <summary>Whether <paramref name="method"/> looks claims up by type; the principal is always the first value it pops.</summary>
    private static bool IsClaimLookup(MethodBase method) =>
        ClaimLookupNames.Contains(method.Name) &&
        (method.IsStatic
            ? method.GetParameters() is [var first, ..] && CanTakeAPrincipal(first.ParameterType)
            : IsClaimsType(method.DeclaringType));

    /// <summary>A parameter a principal or identity can be passed to: a claims type, or a type either one converts to.</summary>
    private static bool CanTakeAPrincipal(Type type) =>
        IsClaimsType(type) || type.IsAssignableFrom(typeof(ClaimsPrincipal)) || type.IsAssignableFrom(typeof(ClaimsIdentity));

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

        /// <summary>The value is, or carries, a permission value.</summary>
        Permission = 1,

        /// <summary>The value is a collection of claims, or of identities that hold them.</summary>
        ClaimsCollection = 2,

        /// <summary>
        /// The value is a decision on a permission value: the result of comparing a claim's own type, value or issuer
        /// with one, or of a scanned method that returns such a result or branches on one. It marks a delegate whose
        /// target decides on a permission value, and nothing else.
        /// </summary>
        Derived = 4,

        /// <summary>The value is a claim's own type, value or issuer, or a string computed from one.</summary>
        ClaimMember = 8,

        /// <summary>The value is a string collection projected out of claims by a selector that reads a claim member.</summary>
        ClaimValues = 16
    }

    private readonly record struct MemberKey(Module Module, int Token)
    {
        public static MemberKey Of(MemberInfo member) => new(member.Module, member.MetadataToken);
    }

    /// <summary>
    /// A value on the simulated stack: what it carries, and where in the current method it came from, as the
    /// parameters and captured-variable fields it was computed from.
    /// </summary>
    private readonly record struct Flow(Taint Taint, ulong Parameters = 0, ImmutableHashSet<MemberKey>? Fields = null)
    {
        public static readonly Flow None = new(Taint.None);

        public bool HasPermission => Taint.HasFlag(Taint.Permission);

        /// <summary>Whether the value holds claims: a claims collection, or values projected out of claims.</summary>
        public bool IsClaimsCollection => (Taint & (Taint.ClaimsCollection | Taint.ClaimValues)) != 0;

        public bool HasClaimMember => Taint.HasFlag(Taint.ClaimMember);

        public bool IsDecision => Taint.HasFlag(Taint.Derived);

        /// <summary>Whether a method that pushes this value uses a permission value: loads one, or decides on one.</summary>
        public bool InvolvesPermission => (Taint & (Taint.Permission | Taint.Derived)) != 0;

        public static Flow operator |(Flow left, Flow right) =>
            new(left.Taint | right.Taint, left.Parameters | right.Parameters, Union(left.Fields, right.Fields));

        /// <summary>
        /// The flow of a value of <paramref name="type"/> computed from this one, keeping only what the type can hold: the
        /// permission value and its sources, claims, a claim member (strings, and delegates that return one), or values
        /// projected out of claims (string collections). A decision is never carried into a call's result.
        /// </summary>
        public Flow Into(Type type)
        {
            var taint = Taint.None;
            if (Taint.HasFlag(Taint.ClaimsCollection) && CanHoldClaims(type))
                taint |= Taint.ClaimsCollection;
            if (HasClaimMember && (type == typeof(string) || type == typeof(object) || typeof(Delegate).IsAssignableFrom(type)))
                taint |= Taint.ClaimMember;
            if (Taint.HasFlag(Taint.ClaimValues) && (type == typeof(object) || IsStringCollection(type)))
                taint |= Taint.ClaimValues;
            return CanHoldPermissionValue(type)
                ? new Flow(taint | (Taint & Taint.Permission), Parameters, Fields)
                : new Flow(taint);
        }

        public bool Covers(Flow other) =>
            (Taint | other.Taint) == Taint &&
            (Parameters | other.Parameters) == Parameters &&
            (other.Fields is null || (Fields is not null && other.Fields.IsSubsetOf(Fields)));

        private static ImmutableHashSet<MemberKey>? Union(ImmutableHashSet<MemberKey>? left, ImmutableHashSet<MemberKey>? right) =>
            left is null ? right : right is null ? left : left.Union(right);
    }

    /// <summary>What every sweep learns about members, shared across methods and assemblies.</summary>
    private sealed class ScanState(HashSet<string> permissionValues)
    {
        private readonly Dictionary<MemberKey, Taint> _fields = [];
        private readonly Dictionary<(MemberKey Type, string Property), Taint> _properties = [];
        private readonly HashSet<MemberKey> _permissionLoadingMethods = [];
        private readonly Dictionary<MemberKey, Taint> _returned = [];
        private readonly Dictionary<MemberKey, ulong> _sinkParameters = [];
        private readonly Dictionary<MemberKey, ulong> _comparedParameters = [];
        private readonly HashSet<MemberKey> _sinkFields = [];
        private readonly Dictionary<MemberKey, ImmutableHashSet<MemberKey>> _capturedFieldsRead = [];

        public bool Changed { get; set; }

        public Taint String(string value) => permissionValues.Contains(value) ? Taint.Permission : Taint.None;

        public Taint Field(FieldInfo field) =>
            (IsPermissionField(field) ? Taint.Permission : Taint.None) | _fields.GetValueOrDefault(MemberKey.Of(field));

        public void AssignField(FieldInfo field, Taint value) => Merge(_fields, MemberKey.Of(field), value);

        /// <summary>The taint a property getter returns: whatever its setter or its auto-property backing field was assigned.</summary>
        public Taint Property(MethodBase getter)
        {
            if (getter.DeclaringType is not { } type)
                return Taint.None;

            var name = getter.Name["get_".Length..];
            var taint = _properties.GetValueOrDefault((MemberKey.Of(type), name));
            return type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic) is { } backingField
                ? taint | Field(backingField)
                : taint;
        }

        public void AssignProperty(MethodBase setter, Taint value)
        {
            if (setter.DeclaringType is { } type)
                Merge(_properties, (MemberKey.Of(type), setter.Name["set_".Length..]), value);
        }

        /// <summary>
        /// What a delegate to <paramref name="method"/> carries: a permission value when the method uses one, and a claim
        /// member when it returns one, as a selector such as <c>claim =&gt; claim.Value</c> does.
        /// </summary>
        public Taint DelegateTarget(MethodBase method) =>
            (_permissionLoadingMethods.Contains(MemberKey.Of(method)) ? Taint.Permission : Taint.None) |
            (Returned(method) & Taint.ClaimMember);

        public void MethodLoadsPermission(MethodBase method) => Add(_permissionLoadingMethods, MemberKey.Of(method));

        public Taint Returned(MethodBase method) => _returned.GetValueOrDefault(MemberKey.Of(method));

        public void MethodReturns(MethodBase method, Taint taint) => Merge(_returned, MemberKey.Of(method), taint);

        public ulong SinkParameters(MethodBase method) => _sinkParameters.GetValueOrDefault(MemberKey.Of(method));

        public bool IsSinkField(FieldInfo field) => _sinkFields.Contains(MemberKey.Of(field));

        /// <summary>Records that <paramref name="flow"/>'s sources, in <paramref name="method"/>, reach a claims read.</summary>
        public void Sink(MethodBase method, Flow flow)
        {
            AddParameters(_sinkParameters, method, flow.Parameters);
            foreach (var field in flow.Fields ?? [])
                Add(_sinkFields, field);
        }

        /// <summary>The parameters of <paramref name="method"/> it compares with a permission value, directly or through a scanned method.</summary>
        public ulong ComparedParameters(MethodBase method) => _comparedParameters.GetValueOrDefault(MemberKey.Of(method));

        public void Compared(MethodBase method, Flow flow) => AddParameters(_comparedParameters, method, flow.Parameters);

        private void AddParameters(Dictionary<MemberKey, ulong> map, MethodBase method, ulong parameters)
        {
            var key = MemberKey.Of(method);
            var current = map.GetValueOrDefault(key);
            if ((current | parameters) == current)
                return;

            map[key] = current | parameters;
            Changed = true;
        }

        public ImmutableHashSet<MemberKey>? CapturedFieldsRead(MethodBase method) => _capturedFieldsRead.GetValueOrDefault(MemberKey.Of(method));

        public void RecordCapturedFieldsRead(MethodBase method, ImmutableHashSet<MemberKey> fields)
        {
            var key = MemberKey.Of(method);
            if (fields.IsEmpty || (_capturedFieldsRead.TryGetValue(key, out var current) && fields.IsSubsetOf(current)))
                return;

            _capturedFieldsRead[key] = current is null ? fields : current.Union(fields);
            Changed = true;
        }

        private void Add(HashSet<MemberKey> set, MemberKey key)
        {
            if (set.Add(key))
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

    /// <summary>Forward passes over one method body with a model of the evaluation stack that carries <see cref="Flow"/>s.</summary>
    private sealed class MethodSimulation(
        ScanState state,
        Type type,
        MethodBase method,
        List<PermissionBypassFinding> findings,
        Dictionary<string, int> lookups)
    {
        private readonly Flow[] _locals = new Flow[method.GetMethodBody()!.LocalVariables.Count];
        private readonly Flow[] _arguments = new Flow[method.GetParameters().Length + (method.IsStatic ? 0 : 1)];
        private readonly Type[]? _typeArguments = type.IsGenericType ? type.GetGenericArguments() : null;
        private readonly Type[]? _methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        private readonly (string Assembly, string Type, string Method) _owner = Owner(type, method);
        private List<PermissionBypassFinding> _pending = [];
        private Dictionary<string, int> _pendingLookups = [];
        private ImmutableHashSet<MemberKey> _capturedFieldsRead = [];
        private bool _localsChanged;
        private bool _loadsPermission;
        private bool _branchesOnDecision;
        private Taint _returned;

        public void Run()
        {
            var body = method.GetMethodBody()!;
            var instructions = IlInstructionReader.Read(body.GetILAsByteArray()!);
            if (_arguments.Length > 64)
                throw new InvalidOperationException($"The permission-bypass scan cannot model {type.FullName}::{method.Name}: it summarizes at most 64 parameters.");

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
            // A method that branches on a decision returns a decision too, as `claim.Type == x || other` does.
            if (_branchesOnDecision && method is MethodInfo { ReturnType: var returnType } && returnType != typeof(void))
                _returned |= Taint.Derived;
            if (_returned != Taint.None)
                state.MethodReturns(method, _returned);
            state.RecordCapturedFieldsRead(method, _capturedFieldsRead);
        }

        private void Pass(MethodBody body, IReadOnlyList<IlInstruction> instructions)
        {
            var entries = new Dictionary<int, List<Flow>>();
            foreach (var clause in body.ExceptionHandlingClauses)
            {
                if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                    entries[clause.FilterOffset] = [Flow.None];
                entries[clause.HandlerOffset] = clause.Flags is ExceptionHandlingClauseOptions.Clause or ExceptionHandlingClauseOptions.Filter
                    ? [Flow.None]
                    : [];
            }

            List<Flow>? stack = [];
            foreach (var instruction in instructions)
            {
                if (entries.TryGetValue(instruction.Offset, out var recorded))
                    stack = stack is null ? [.. recorded] : Join(stack, recorded, instruction);

                // IL requires that code following an unconditional transfer, and not the target of an earlier forward
                // branch, starts with an empty stack, so one forward pass knows the stack everywhere.
                stack ??= [];
                Execute(instruction, stack);

                foreach (var target in instruction.Targets)
                    entries[target] = entries.TryGetValue(target, out var existing) ? Join(existing, stack, instruction) : [.. stack];

                if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Return or FlowControl.Throw)
                    stack = null;
            }
        }

        private List<Flow> Join(List<Flow> left, List<Flow> right, IlInstruction instruction)
        {
            if (left.Count != right.Count)
                throw Inconsistent(instruction, $"stack depths {left.Count} and {right.Count} meet");

            return [.. left.Zip(right, (first, second) => first | second)];
        }

        private void Execute(IlInstruction instruction, List<Flow> stack)
        {
            var code = instruction.OpCode;
            var name = code.Name!;
            switch (name)
            {
                case "ldstr":
                    var literal = method.Module.ResolveString(instruction.Operand);
                    Push(stack, new Flow(state.String(literal)));
                    return;
                case "ldsfld" or "ldsflda":
                    Push(stack, Load(ResolveField(instruction)));
                    return;
                case "ldfld" or "ldflda":
                    Pop(stack, instruction);
                    Push(stack, Load(ResolveField(instruction)));
                    return;
                case "stsfld":
                    Store(ResolveField(instruction), Pop(stack, instruction));
                    return;
                case "stfld":
                    var assigned = Pop(stack, instruction);
                    Pop(stack, instruction);
                    Store(ResolveField(instruction), assigned);
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
                    Push(stack, new Flow(state.DelegateTarget(target), Fields: state.CapturedFieldsRead(target)));
                    return;
                case "calli":
                    CallIndirect(instruction, stack);
                    return;
                case "ret":
                    if (method is MethodInfo { ReturnType: var returnType } && returnType != typeof(void))
                        _returned |= Pop(stack, instruction).Taint;
                    return;
                case "ldobj":
                    // An in, ref or out parameter, or a ref local, read through its address carries the value stored there.
                    Push(stack, Pop(stack, instruction));
                    return;
                case "leave" or "leave.s" or "endfinally":
                    stack.Clear();
                    return;
            }

            if (name.StartsWith("ldind.", StringComparison.Ordinal))
            {
                Push(stack, Pop(stack, instruction));
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
                Push(stack, _arguments[instruction.Operand] | new Flow(Taint.None, 1UL << instruction.Operand));
                return;
            }

            if (name.StartsWith("starg", StringComparison.Ordinal))
            {
                Store(_arguments, instruction.Operand, Pop(stack, instruction));
                return;
            }

            // Any other instruction keeps only a decision: negating, combining or comparing booleans decides the same way.
            var decision = false;
            for (var index = PopCount(code.StackBehaviourPop, instruction); index > 0; index--)
                decision |= Pop(stack, instruction).IsDecision;
            if (decision && code.FlowControl == FlowControl.Cond_Branch)
                _branchesOnDecision = true;
            for (var index = PushCount(code.StackBehaviourPush, instruction); index > 0; index--)
                Push(stack, decision ? new Flow(Taint.Derived) : Flow.None);
        }

        private Flow Load(FieldInfo field)
        {
            var taint = state.Field(field);
            if (!IsCapturedVariable(field))
                return new Flow(taint);

            var key = MemberKey.Of(field);
            _capturedFieldsRead = _capturedFieldsRead.Add(key);
            return new Flow(taint, Fields: [key]);
        }

        private void Store(FieldInfo field, Flow value)
        {
            state.AssignField(field, value.Taint);

            // A closure or state machine that reads this field into a claims read makes whatever is stored here a sink.
            if (IsCapturedVariable(field) && state.IsSinkField(field))
                state.Sink(method, value);
        }

        private void Call(IlInstruction instruction, List<Flow> stack, MethodBase callee)
        {
            var isNewObject = instruction.OpCode == OpCodes.Newobj;
            var parameters = callee.GetParameters();
            var hasThis = !callee.IsStatic && !isNewObject;
            var count = parameters.Length + (hasThis ? 1 : 0);
            var arguments = new Flow[count];
            for (var index = count - 1; index >= 0; index--)
                arguments[index] = Pop(stack, instruction);

            var reported = ReportFastEndpointsHelper(callee, instruction);
            if (IsClaimLookup(callee))
            {
                var key = PermissionBypassScan.TypeKey(_owner.Assembly, _owner.Type);
                _pendingLookups[key] = _pendingLookups.GetValueOrDefault(key) + 1;

                // Every argument but the principal names the claim type or value being looked up.
                foreach (var argument in arguments.Skip(1))
                    state.Sink(method, argument);
                if (!reported && arguments.Skip(1).Any(argument => argument.HasPermission))
                {
                    reported = true;
                    Report(instruction,
                        $"{callee.DeclaringType?.Name}.{callee.Name} is passed a permission value; decide through IPermissionAuthorizationService instead.");
                }
            }
            else if (arguments.Any(argument => argument.IsClaimsCollection))
            {
                // The collection itself may carry the permission value it was looked up with; that read is reported
                // where it happened, so only the other arguments count here.
                var others = arguments.Where(argument => !argument.IsClaimsCollection).ToArray();
                foreach (var argument in others)
                    state.Sink(method, argument);
                if (!reported && others.Any(argument => argument.HasPermission))
                {
                    reported = true;
                    Report(instruction,
                        $"{callee.DeclaringType?.Name}.{callee.Name} inspects a principal's claims for a permission value; decide through IPermissionAuthorizationService instead.");
                }
            }

            // A scanned method whose parameter reaches a claims read: a permission value passed to it is a read here.
            var sinks = state.SinkParameters(callee);
            for (var index = 0; sinks != 0 && index < count; index++)
            {
                var parameter = index + (isNewObject ? 1 : 0);
                if ((sinks & (1UL << parameter)) == 0)
                    continue;

                state.Sink(method, arguments[index]);
                if (!reported && arguments[index].HasPermission)
                {
                    reported = true;
                    Report(instruction,
                        $"{callee.DeclaringType?.Name}.{callee.Name} is passed a permission value that it reads claims with; decide through IPermissionAuthorizationService instead.");
                }
            }

            if (callee.IsSpecialName && callee.Name.StartsWith("set_", StringComparison.Ordinal) && count > 0)
                state.AssignProperty(callee, arguments[^1].Taint);

            var resultType = isNewObject ? callee.DeclaringType : (callee as MethodInfo)?.ReturnType;
            if (resultType is null || resultType == typeof(void))
                return;

            // A result is computed from every input, the receiver included, but keeps only what its type can hold.
            var own = isNewObject ? Taint.None : ResultTaint(callee);
            var result = arguments.Aggregate(new Flow(own), (all, argument) => all | argument).Into(resultType);

            // A decision comes only from comparing a claim member with a permission value, from passing a claim member to
            // a scanned method that compares that parameter with one, or from a scanned method that returns a decision;
            // an authorization result computed from a permission value is not a decision on a claim.
            var comparison = ComparisonNames.Contains(callee.Name) && arguments.Any(argument => argument.HasPermission);
            var decides = (own & Taint.Derived) != 0 || (comparison && arguments.Any(argument => argument.HasClaimMember));
            var compared = state.ComparedParameters(callee);
            for (var index = 0; index < count; index++)
            {
                if (comparison || (compared & (1UL << (index + (isNewObject ? 1 : 0)))) != 0)
                {
                    state.Compared(method, arguments[index]);
                    decides |= !comparison && arguments[index].HasClaimMember;
                }
            }

            if (decides)
                result |= new Flow(Taint.Derived);

            // Projecting claims onto their members, claims.Select(claim => claim.Value), yields values that still hold claims.
            if (arguments.Any(argument => argument.Taint.HasFlag(Taint.ClaimsCollection)) &&
                arguments.Any(argument => argument.HasClaimMember) &&
                IsStringCollection(resultType))
            {
                result |= new Flow(Taint.ClaimValues);
            }

            Push(stack, result);
        }

        private Taint ResultTaint(MethodBase callee)
        {
            if (IsClaimsType(callee.DeclaringType) && callee.Name is ("get_Claims" or "get_Identities" or "FindAll"))
                return Taint.ClaimsCollection;

            if (callee.DeclaringType == typeof(Claim) && callee.Name.StartsWith("get_", StringComparison.Ordinal))
                return Taint.ClaimMember;

            var property = callee.IsSpecialName && callee.Name.StartsWith("get_", StringComparison.Ordinal)
                ? state.Property(callee)
                : Taint.None;
            return property | state.Returned(callee);
        }

        private void CallIndirect(IlInstruction instruction, List<Flow> stack)
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
                Push(stack, Flow.None);
        }

        private bool ReportFastEndpointsHelper(MethodBase callee, IlInstruction instruction)
        {
            if (!IsFastEndpointsPermissionHelper(callee))
                return false;

            _pending.Add(Finding(FastEndpointsPermission, instruction,
                $"{callee.DeclaringType?.Name}.{callee.Name} is a FastEndpoints permission helper; declare Foundation Identity policy metadata instead."));
            return true;
        }

        private void Report(IlInstruction instruction, string message) =>
            _pending.Add(Finding(DirectPermissionClaim, instruction, message));

        private PermissionBypassFinding Finding(string code, IlInstruction instruction, string message) =>
            new(code, _owner.Assembly, _owner.Type, _owner.Method, $"{type.FullName}::{method.Name} IL_{instruction.Offset:x4}", message);

        private void Push(List<Flow> stack, Flow flow)
        {
            if (flow.InvolvesPermission)
                _loadsPermission = true;
            stack.Add(flow);
        }

        private Flow Pop(List<Flow> stack, IlInstruction instruction)
        {
            if (stack.Count == 0)
                throw Inconsistent(instruction, "the evaluation stack underflows");

            var flow = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return flow;
        }

        private void Store(Flow[] slots, int index, Flow flow)
        {
            if (slots[index].Covers(flow))
                return;

            slots[index] |= flow;
            _localsChanged = true;
        }

        private int PopCount(StackBehaviour behavior, IlInstruction instruction) => behavior switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Varpop => throw Inconsistent(instruction, "an unmodeled variable-pop instruction"),
            _ => behavior.ToString().Split('_').Length
        };

        private int PushCount(StackBehaviour behavior, IlInstruction instruction) => behavior switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Varpush => throw Inconsistent(instruction, "an unmodeled variable-push instruction"),
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
