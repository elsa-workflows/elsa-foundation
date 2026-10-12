using CShells.Features;
using Elsa.Persistence.EntityFramework;
using System.Reflection;
using System.Reflection.Emit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>One feature class that depends on an EF module without a <c>[UsesEfModule]</c> naming it.</summary>
internal sealed record EfFeatureModuleViolation(Type Feature, string Module, string Reason)
{
    public override string ToString() =>
        $"{Feature.FullName} depends on module '{Module}' ({Reason}) and carries no [UsesEfModule(\"{Module}\")].";
}

/// <summary>
/// The guard behind spec 171 FR-066: a shell feature that depends on an EF module must say so with
/// <see cref="UsesEfModuleAttribute"/>, so the research.md inventory cannot silently grow a feature the
/// provider-agreement check and the activation guard never see.
/// </summary>
/// <remarks>
/// <para>
/// Two shapes count as depending on a module, because both exist today. The first is a call to
/// <c>AddEfModuleMigrations&lt;TContext&gt;</c>; it is not enough to look at the feature's own
/// <c>ConfigureServices</c> body for it, because <c>SecretsEntityFrameworkCoreFeature</c> reaches that call
/// through its registration class instead — so the search follows calls transitively, within the feature's
/// own assembly. The second is a reference to a module's <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// type anywhere in that same reachable code, which is how <c>WorkflowsDashboardEntityFrameworkCoreFeature</c>
/// depends on two modules while registering migrations for neither.
/// </para>
/// <para>
/// The search reads IL rather than source, so it sees a dependency however it is spelled and cannot be
/// defeated by moving the call one method further away. It stops at the assembly boundary: following calls
/// into every referenced assembly would eventually reach a module context from almost anywhere, and the
/// dependency this guard is about is the one a feature's own package expresses.
/// </para>
/// </remarks>
internal static class EfFeatureModuleAudit
{
    private const string MigrationRegistration = "AddEfModuleMigrations";

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(opCode => opCode.Value)
        .ToDictionary(group => group.Key, group => group.First());

    /// <summary>
    /// Every feature in <paramref name="assemblies"/> that depends on one of
    /// <paramref name="modules"/>' modules without declaring it, ordinal-ordered so a failure reads the
    /// same way twice.
    /// </summary>
    public static IReadOnlyList<EfFeatureModuleViolation> Audit(IEnumerable<Assembly> assemblies, IEnumerable<Assembly> modules)
    {
        var contexts = ModulesByContext(modules);
        return
        [
            .. assemblies
                .Distinct()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IShellFeature).IsAssignableFrom(type))
                .SelectMany(feature => Violations(feature, contexts))
                .OrderBy(violation => violation.Feature.FullName, StringComparer.Ordinal)
                .ThenBy(violation => violation.Module, StringComparer.Ordinal)
        ];
    }

    /// <summary>Every module context type — base and provider-derived — mapped to its module's canonical name.</summary>
    private static Dictionary<Type, string> ModulesByContext(IEnumerable<Assembly> modules)
    {
        var map = new Dictionary<Type, string>();
        foreach (var descriptor in EfModuleCatalog.Discover(modules))
            foreach (var context in new[] { descriptor.ContextType }.Concat(ModuleContextCatalog.Providers.Select(descriptor.ProviderContext)).OfType<Type>())
                map[context] = descriptor.Name;
        return map;
    }

    private static IEnumerable<EfFeatureModuleViolation> Violations(Type feature, Dictionary<Type, string> contexts)
    {
        var declared = feature
            .GetCustomAttributes<UsesEfModuleAttribute>(inherit: false)
            .Select(attribute => attribute.Module)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Required(feature, contexts)
            .Where(required => !declared.Contains(required.Key))
            .Select(required => new EfFeatureModuleViolation(feature, required.Key, required.Value))
            .OrderBy(violation => violation.Module, StringComparer.Ordinal);
    }

    /// <summary>The modules reachable from <paramref name="feature"/>, each with the shape that established it.</summary>
    private static Dictionary<string, string> Required(Type feature, Dictionary<Type, string> contexts)
    {
        var required = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<MethodBase>();
        var pending = new Queue<MethodBase>(Methods(feature));

        while (pending.TryDequeue(out var method))
        {
            if (!visited.Add(method))
                continue;

            foreach (var member in Members(method))
            {
                switch (member)
                {
                    case MethodBase called:
                        var arguments = GenericArguments(called);
                        if (called.Name == MigrationRegistration && arguments is [{ } context] && contexts.TryGetValue(context, out var registered))
                            required[registered] = $"it registers that module's migrations with {MigrationRegistration}<{context.Name}>";

                        foreach (var type in arguments)
                            Note(required, contexts, type);
                        if (called.DeclaringType?.Assembly == feature.Assembly)
                            pending.Enqueue(called);
                        Note(required, contexts, called.DeclaringType);
                        break;

                    case Type type:
                        Note(required, contexts, type);
                        break;

                    case FieldInfo field:
                        Note(required, contexts, field.FieldType);
                        break;
                }
            }
        }

        return required;
    }

    /// <summary>
    /// Records a context-type dependency, unless a migration registration already established the same
    /// module: that reason is the more specific of the two and reads better in a failure.
    /// </summary>
    private static void Note(Dictionary<string, string> required, Dictionary<Type, string> contexts, Type? type)
    {
        foreach (var candidate in Candidates(type))
            if (contexts.TryGetValue(candidate, out var module) && !required.ContainsKey(module))
                required[module] = $"it depends on that module's {candidate.Name}";
    }

    /// <summary>A constructor has no generic arguments of its own and throws when asked for them.</summary>
    private static Type[] GenericArguments(MethodBase method) => method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : [];

    private static IEnumerable<Type> Candidates(Type? type)
    {
        if (type is null)
            yield break;
        yield return type;
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments())
                yield return argument;
    }

    /// <summary>Every method body a feature owns, including the display classes its lambdas compile into.</summary>
    private static IEnumerable<MethodBase> Methods(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetMethods(all).Cast<MethodBase>()
            .Concat(type.GetConstructors(all))
            .Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Methods));
    }

    /// <summary>Every metadata member an IL body references, resolved where it can be.</summary>
    private static IEnumerable<MemberInfo> Members(MethodBase method)
    {
        byte[] il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException or BadImageFormatException)
        {
            yield break;
        }

        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethodDefinition || method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (var position = 0; position < il.Length;)
        {
            short value = il[position++];
            if (value == 0xFE && position < il.Length)
                value = (short)((value << 8) | il[position++]);
            if (!OpCodesByValue.TryGetValue(value, out var opCode))
                yield break;

            var operand = position;
            position += OperandSize(opCode, il, position);
            if (position > il.Length)
                yield break;
            if (opCode.OperandType is not (OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok))
                continue;

            var member = Resolve(method.Module, BitConverter.ToInt32(il, operand), typeArguments, methodArguments);
            if (member is not null)
                yield return member;
        }
    }

    private static MemberInfo? Resolve(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
    {
        try
        {
            return module.ResolveMember(token, typeArguments, methodArguments);
        }
        // A token this process cannot resolve — a reference into an assembly the test closure does not load,
        // most often — is not a dependency this guard can judge, and is skipped rather than failing the audit.
        catch (Exception failure) when (failure is ArgumentException or BadImageFormatException or FileNotFoundException or TypeLoadException or MissingMemberException)
        {
            return null;
        }
    }

    private static int OperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => position + 4 <= il.Length ? 4 + (4 * BitConverter.ToInt32(il, position)) : il.Length,
        _ => 4
    };
}
