using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.Workflows.Design.CodeGeneration;

/// <summary>
/// Generates deterministic, authoring-only activity-call facades from compile-time activity metadata.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ActivityCallGenerator : IIncrementalGenerator
{
    public const string GeneratedHintName = "ElsaActivityCalls.g.cs";
    public const string DuplicateFacadeDiagnosticId = "VFAUTHGEN001";
    public const string UnsupportedShapeDiagnosticId = "VFAUTHGEN002";

    private const string ActivityContractName = "Elsa.Activities.Runtime.Core.Contracts.IActivity";
    private const string ActivityResultContractName = "Elsa.Activities.Runtime.Core.Contracts.IActivityResult<TResult>";
    private const string ActivityInputAttributeName = "Elsa.Activities.Runtime.Core.Attributes.ActivityInputAttribute";
    private const string ActivityOutputAttributeName = "Elsa.Activities.Runtime.Core.Attributes.OutputAttribute";
    private const string ActivityOutcomeAttributeName = "Elsa.Activities.Runtime.Core.Attributes.ActivityOutcomeAttribute";
    private const string RequiredAttributeName = "Elsa.Activities.Runtime.Core.Attributes.RequiredAttribute";
    private const string VersionAttributeName = "Elsa.Activities.Runtime.Core.Attributes.VersionAttribute";
    private const string InformationalVersionAttributeName = "System.Reflection.AssemblyInformationalVersionAttribute";

    private static readonly DiagnosticDescriptor DuplicateFacade = new(
        DuplicateFacadeDiagnosticId,
        "Duplicate activity-call method",
        "Activity-call method '{0}' is declared by both '{1}' and '{2}'. Method names must be unique.",
        "Elsa.Authoring",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedShape = new(
        UnsupportedShapeDiagnosticId,
        "Unsupported activity-call metadata",
        "Activity '{0}' has unsupported activity-call metadata: {1}",
        "Elsa.Authoring",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var activities = context.CompilationProvider.Select(
            static (compilation, cancellationToken) => ReadActivities(compilation, cancellationToken));
        context.RegisterSourceOutput(activities, static (output, candidates) => Emit(output, candidates));
    }

    private static ImmutableArray<ActivityMetadata> ReadActivities(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var activities = ImmutableArray.CreateBuilder<ActivityMetadata>();
        var activityContract = compilation.GetTypeByMetadataName(ActivityContractName);
        if (activityContract is null)
            return activities.ToImmutable();

        CollectActivities(compilation.Assembly.GlobalNamespace, activities, cancellationToken);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectActivities(assembly.GlobalNamespace, activities, cancellationToken);
        }

        return activities.ToImmutable();
    }

    private static void CollectActivities(
        INamespaceSymbol namespaceSymbol,
        ImmutableArray<ActivityMetadata>.Builder activities,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var memberNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectActivities(memberNamespace, activities, cancellationToken);
        }

        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectActivity(type, activities, cancellationToken);
        }
    }

    private static void CollectActivity(
        INamedTypeSymbol type,
        ImmutableArray<ActivityMetadata>.Builder activities,
        System.Threading.CancellationToken cancellationToken)
    {
        if (IsPublicActivity(type))
            activities.Add(ReadActivity(type));

        foreach (var nestedType in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectActivity(nestedType, activities, cancellationToken);
        }
    }

    private static bool IsPublicActivity(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Class, IsAbstract: false, DeclaredAccessibility: Accessibility.Public, ContainingType: null } &&
        type.AllInterfaces.Any(candidate => MetadataName(candidate) == ActivityContractName);

    private static ActivityMetadata ReadActivity(INamedTypeSymbol activityType)
    {
        var resultContracts = activityType.AllInterfaces
            .Where(candidate => candidate.IsGenericType &&
                                candidate.OriginalDefinition.ToDisplayString() == ActivityResultContractName)
            .ToArray();
        var resultType = resultContracts.Length == 1 ? resultContracts[0].TypeArguments[0] : null;
        var inputs = EnumeratePublicProperties(activityType)
            .Select(property => new { Property = property, Attribute = FindPropertyAttribute(property, ActivityInputAttributeName) })
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => ReadInput(candidate.Property, candidate.Attribute!))
            .ToImmutableArray();
        var outputs = resultType is INamedTypeSymbol namedResult
            ? EnumeratePublicProperties(namedResult)
                .Select(property => new { Property = property, Attribute = FindPropertyAttribute(property, ActivityOutputAttributeName) })
                .Where(candidate => candidate.Attribute is not null)
                .Select(candidate => ReadOutput(candidate.Property, candidate.Attribute!))
                .ToImmutableArray()
            : ImmutableArray<OutputMetadata>.Empty;
        var outcomes = EnumerateTypeAttributes(activityType, ActivityOutcomeAttributeName)
            .Select(ReadOutcome)
            .ToImmutableArray();
        var location = activityType.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? Location.None;

        return new ActivityMetadata(
            activityType,
            activityType.Name,
            ResolveActivityVersionId(activityType),
            resultType,
            inputs,
            outputs,
            outcomes,
            location);
    }

    private static IEnumerable<IPropertySymbol> EnumeratePublicProperties(INamedTypeSymbol type)
    {
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.IsStatic && property.DeclaredAccessibility == Accessibility.Public)
                {
                    if (!properties.ContainsKey(property.Name))
                        properties.Add(property.Name, property);
                }
            }
        }

        return properties.Values;
    }

    private static AttributeData? FindPropertyAttribute(IPropertySymbol property, string metadataName)
    {
        foreach (var current in EnumeratePropertyChain(property))
        {
            var attribute = current.GetAttributes().FirstOrDefault(candidate => IsAttribute(candidate, metadataName));
            if (attribute is not null)
                return attribute;
        }

        return null;
    }

    private static IEnumerable<IPropertySymbol> EnumeratePropertyChain(IPropertySymbol property)
    {
        yield return property;

        for (var current = property.ContainingType.BaseType; current is not null; current = current.BaseType)
        {
            var baseProperty = current.GetMembers(property.Name)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(candidate =>
                    !candidate.IsStatic &&
                    candidate.DeclaredAccessibility == Accessibility.Public);
            if (baseProperty is not null)
                yield return baseProperty;
        }
    }

    private static IEnumerable<AttributeData> EnumerateTypeAttributes(INamedTypeSymbol type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes().Where(candidate => IsAttribute(candidate, metadataName)))
                yield return attribute;
        }
    }

    private static InputMetadata ReadInput(IPropertySymbol property, AttributeData attribute)
    {
        var key = ReadNamedString(attribute, "Key") ?? property.Name;
        var order = ReadNamedSingle(attribute, "Order");
        var required = EnumeratePropertyChain(property).Any(candidate =>
            candidate.IsRequired ||
            candidate.GetAttributes().Any(attribute => IsAttribute(attribute, RequiredAttributeName)));
        return new InputMetadata(ToParameterName(property.Name), key, property.Type, order, required);
    }

    private static OutputMetadata ReadOutput(IPropertySymbol property, AttributeData attribute)
    {
        var key = ReadNamedString(attribute, "Key") ?? property.Name;
        return new OutputMetadata(property.Name, key, property.Type);
    }

    private static OutcomeMetadata ReadOutcome(AttributeData attribute)
    {
        var key = ReadString(attribute.ConstructorArguments, 0);
        return new OutcomeMetadata(ToMemberName(key), key);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<ActivityMetadata> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
            return;

        var ordered = candidates
            .OrderBy(candidate => candidate.MethodName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.ActivityDisplayName, StringComparer.Ordinal)
            .ToArray();
        var invalid = new HashSet<ActivityMetadata>();

        foreach (var candidate in ordered)
        {
            var error = Validate(candidate);
            if (error is null)
                continue;

            invalid.Add(candidate);
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedShape,
                candidate.Location,
                candidate.ActivityDisplayName,
                error));
        }

        foreach (var group in ordered.GroupBy(candidate => candidate.MethodName, StringComparer.Ordinal))
        {
            var duplicates = group.ToArray();
            if (duplicates.Length < 2)
                continue;

            foreach (var duplicate in duplicates)
                invalid.Add(duplicate);

            context.ReportDiagnostic(Diagnostic.Create(
                DuplicateFacade,
                duplicates[1].Location,
                group.Key,
                duplicates[0].ActivityDisplayName,
                duplicates[1].ActivityDisplayName));
        }

        var valid = ordered.Where(candidate => !invalid.Contains(candidate)).ToArray();
        if (valid.Length == 0)
            return;

        context.AddSource(GeneratedHintName, SourceText.From(Render(valid), Encoding.UTF8));
    }

    private static string? Validate(ActivityMetadata activity)
    {
        if (activity.ActivityType.ContainingType is not null || activity.ActivityType.IsGenericType)
            return "activity types must be top-level and non-generic";
        if (!SyntaxFacts.IsValidIdentifier(activity.MethodName))
            return $"method name '{activity.MethodName}' is not a valid C# identifier";
        if (string.IsNullOrWhiteSpace(activity.VersionId))
            return "activity version id must not be empty";
        if (!IsSupportedType(activity.ResultType))
            return "result type must be a closed, non-pointer CLR type";

        var orderedInputs = OrderInputs(activity.Inputs).ToArray();
        if (orderedInputs.Any(input => input.Order < 0))
            return "input order values must be non-negative";
        if (orderedInputs.Any(input => input.Name == "nodeId"))
            return "input parameter name 'nodeId' is reserved by the generated call facade";

        var inputError = ValidateMembers(
            orderedInputs.Select(input => new MemberShape(input.Name, input.Key, input.Type)),
            "input");
        if (inputError is not null)
            return inputError;

        var outputError = ValidateMembers(
            activity.Outputs.Select(output => new MemberShape(output.Name, output.Key, output.Type)),
            "output");
        if (outputError is not null)
            return outputError;

        return ValidateNamedKeys(
            activity.Outcomes.Select(outcome => new NamedKey(outcome.Name, outcome.Key)),
            "outcome");
    }

    private static string? ValidateMembers(IEnumerable<MemberShape> members, string kind)
    {
        var materialized = members.ToArray();
        var namedKeyError = ValidateNamedKeys(materialized.Select(member => new NamedKey(member.Name, member.Key)), kind);
        if (namedKeyError is not null)
            return namedKeyError;
        return materialized.Any(member => !IsSupportedType(member.Type))
            ? $"{kind} types must be closed, non-pointer CLR types"
            : null;
    }

    private static string? ValidateNamedKeys(IEnumerable<NamedKey> members, string kind)
    {
        var materialized = members.ToArray();
        if (materialized.Any(member => !SyntaxFacts.IsValidIdentifier(member.Name)))
            return $"every {kind} name must be a valid C# identifier";
        if (materialized.Any(member => string.IsNullOrWhiteSpace(member.Key)))
            return $"every {kind} stable key must be non-empty";
        if (materialized.Select(member => member.Name).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            return $"{kind} names must be unique";
        if (materialized.Select(member => member.Key).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            return $"{kind} stable keys must be unique";
        return null;
    }

    private static bool IsSupportedType(ITypeSymbol? type)
    {
        if (type is null || type.TypeKind is TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.TypeParameter)
            return false;
        if (type.SpecialType == SpecialType.System_Void)
            return false;

        return type switch
        {
            IArrayTypeSymbol array => IsSupportedType(array.ElementType),
            INamedTypeSymbol named => !named.IsUnboundGenericType && named.TypeArguments.All(IsSupportedType),
            _ => true
        };
    }

    private static string Render(IReadOnlyCollection<ActivityMetadata> activities)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Elsa.Workflows.Design.CodeGeneration.Generated;");
        source.AppendLine();
        source.AppendLine("public static partial class ActivityCallExtensions");
        source.AppendLine("{");

        var activityIndex = 0;
        foreach (var activity in activities)
        {
            if (activityIndex++ > 0)
                source.AppendLine();
            RenderMethod(source, activity);
        }

        source.AppendLine("}");

        foreach (var activity in activities)
        {
            source.AppendLine();
            RenderCallHandle(source, activity);
            source.AppendLine();
            RenderOutputs(source, activity);
            source.AppendLine();
            RenderOutcomes(source, activity);
        }

        return source.ToString();
    }

    private static void RenderMethod(StringBuilder source, ActivityMetadata activity)
    {
        var inputs = OrderInputs(activity.Inputs).ToArray();
        source.Append("    public static ").Append(activity.CallTypeName).Append(' ').Append(activity.MethodName).AppendLine("(");
        source.Append("        this global::Elsa.Workflows.Design.Core.Authoring.ISequenceBuilder sequence")
            .AppendLine(",");
        foreach (var input in inputs)
        {
            source.Append("        global::Elsa.Workflows.Design.Core.Authoring.ActivityArgument<")
                .Append(DisplayType(input.Type)).Append("> ").Append(input.Name);
            if (!input.IsRequired)
                source.Append(" = default");
            source.AppendLine(",");
        }
        source.AppendLine("        string? nodeId = null)");
        source.AppendLine("    {");
        source.AppendLine("        if (sequence is null)");
        source.AppendLine("            throw new global::System.ArgumentNullException(nameof(sequence));");
        source.AppendLine();
        source.Append("        var call = sequence.Add<").Append(DisplayType(activity.ActivityType)).Append(", ")
            .Append(DisplayType(activity.ResultType)).AppendLine(">(");
        source.Append("            ").Append(SymbolDisplay.FormatLiteral(activity.VersionId, quote: true));
        if (inputs.Length == 0)
        {
            source.AppendLine(", nodeId: nodeId);");
        }
        else
        {
            source.AppendLine(",");
            source.AppendLine("            inputs =>");
            source.AppendLine("            {");
            foreach (var input in inputs)
            {
                source.Append("                inputs.Set(")
                    .Append(SymbolDisplay.FormatLiteral(input.Key, quote: true)).Append(", ").Append(input.Name).AppendLine(");");
            }
            source.AppendLine("            },");
            source.AppendLine("            nodeId);");
        }
        source.Append("        return new ").Append(activity.CallTypeName).AppendLine("(call);");
        source.AppendLine("    }");
    }

    private static void RenderCallHandle(StringBuilder source, ActivityMetadata activity)
    {
        var resultType = DisplayType(activity.ResultType);
        source.Append("public sealed class ").Append(activity.CallTypeName).AppendLine();
        source.AppendLine("{");
        source.Append("    private readonly global::Elsa.Workflows.Design.Core.Authoring.ActivityCall<")
            .Append(resultType).AppendLine("> _call;");
        source.AppendLine();
        source.Append("    internal ").Append(activity.CallTypeName)
            .Append("(global::Elsa.Workflows.Design.Core.Authoring.ActivityCall<").Append(resultType).AppendLine("> call)");
        source.AppendLine("    {");
        source.AppendLine("        _call = call;");
        source.Append("        Outputs = new ").Append(activity.OutputsTypeName).AppendLine("(call);");
        source.Append("        Outcomes = new ").Append(activity.OutcomesTypeName).AppendLine("(call);");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public global::Elsa.Workflows.Design.Core.Authoring.ActivityNodeHandle Node => _call.Node;");
        source.Append("    public global::Elsa.Workflows.Design.Core.Authoring.ActivityResultSource<")
            .Append(resultType).AppendLine("> Result => _call.Result;");
        source.Append("    public ").Append(activity.OutputsTypeName).AppendLine(" Outputs { get; }");
        source.Append("    public ").Append(activity.OutcomesTypeName).AppendLine(" Outcomes { get; }");
        source.AppendLine("}");
    }

    private static void RenderOutputs(StringBuilder source, ActivityMetadata activity)
    {
        var resultType = DisplayType(activity.ResultType);
        source.Append("public sealed class ").Append(activity.OutputsTypeName).AppendLine();
        source.AppendLine("{");
        source.Append("    private readonly global::Elsa.Workflows.Design.Core.Authoring.ActivityCall<")
            .Append(resultType).AppendLine("> _call;");
        source.AppendLine();
        source.Append("    internal ").Append(activity.OutputsTypeName)
            .Append("(global::Elsa.Workflows.Design.Core.Authoring.ActivityCall<").Append(resultType)
            .AppendLine("> call) => _call = call;");

        foreach (var output in activity.Outputs.OrderBy(output => output.Name, StringComparer.Ordinal))
        {
            source.AppendLine();
            source.Append("    public global::Elsa.Workflows.Design.Core.Authoring.ActivityResultSource<")
                .Append(DisplayType(output.Type)).Append("> ").Append(output.Name).Append(" => _call.Output<")
                .Append(DisplayType(output.Type)).Append(">(").Append(SymbolDisplay.FormatLiteral(output.Key, quote: true)).AppendLine(");");
        }

        source.AppendLine("}");
    }

    private static void RenderOutcomes(StringBuilder source, ActivityMetadata activity)
    {
        var resultType = DisplayType(activity.ResultType);
        source.Append("public sealed class ").Append(activity.OutcomesTypeName).AppendLine();
        source.AppendLine("{");
        source.Append("    private readonly global::Elsa.Workflows.Design.Core.Authoring.ActivityCall<")
            .Append(resultType).AppendLine("> _call;");
        source.AppendLine();
        source.Append("    internal ").Append(activity.OutcomesTypeName)
            .Append("(global::Elsa.Workflows.Design.Core.Authoring.ActivityCall<").Append(resultType)
            .AppendLine("> call) => _call = call;");

        foreach (var outcome in activity.Outcomes.OrderBy(outcome => outcome.Name, StringComparer.Ordinal))
        {
            source.AppendLine();
            source.Append("    public global::Elsa.Workflows.Design.Core.Authoring.ActivityOutcomeSource ")
                .Append(outcome.Name).Append(" => _call.Outcome(")
                .Append(SymbolDisplay.FormatLiteral(outcome.Key, quote: true)).AppendLine(");");
        }

        source.AppendLine("}");
    }

    private static string DisplayType(ITypeSymbol? type) => type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static IOrderedEnumerable<InputMetadata> OrderInputs(IEnumerable<InputMetadata> inputs) => inputs
        .OrderByDescending(input => input.IsRequired)
        .ThenBy(input => input.Order)
        .ThenBy(input => input.Name, StringComparer.Ordinal);

    private static string MetadataName(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static bool IsAttribute(AttributeData attribute, string metadataName) =>
        string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal);

    private static string ReadString(ImmutableArray<TypedConstant> arguments, int index) =>
        index < arguments.Length ? arguments[index].Value as string ?? string.Empty : string.Empty;

    private static string? ReadNamedString(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(candidate => candidate.Key == name).Value.Value as string;

    private static float ReadNamedSingle(AttributeData attribute, string name)
    {
        var value = attribute.NamedArguments.FirstOrDefault(candidate => candidate.Key == name).Value.Value;
        return value is float number ? number : 0;
    }

    private static string ToParameterName(string propertyName) => propertyName.Length == 0
        ? propertyName
        : char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);

    private static string ToMemberName(string key)
    {
        var result = new StringBuilder(key.Length);
        var capitalize = true;
        foreach (var character in key)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                capitalize = true;
                continue;
            }

            result.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }

        if (result.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(result[0]))
            result.Insert(0, '_');
        return result.ToString();
    }

    private static string ResolveActivityVersionId(INamedTypeSymbol activityType)
    {
        var version = ResolveActivityVersion(activityType);
        var sortKey = ToSemVerSortKey(version);
        if (sortKey is null)
            return string.Empty;

        var activityTypeKey = activityType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var bytes = Encoding.UTF8.GetBytes(activityTypeKey + '\u001f' + sortKey);
        byte[] hash;
        using (var algorithm = SHA256.Create())
            hash = algorithm.ComputeHash(bytes);
        var encoded = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "actver_" + encoded;
    }

    private static string ResolveActivityVersion(INamedTypeSymbol activityType)
    {
        for (var current = activityType; current is not null; current = current.BaseType)
        {
            var attribute = current.GetAttributes().FirstOrDefault(candidate => IsAttribute(candidate, VersionAttributeName));
            if (attribute is not null)
                return ReadString(attribute.ConstructorArguments, 0);
        }

        var informational = activityType.ContainingAssembly.GetAttributes()
            .FirstOrDefault(candidate => IsAttribute(candidate, InformationalVersionAttributeName));
        if (informational is not null)
        {
            var value = ReadString(informational.ConstructorArguments, 0);
            if (ToSemVerSortKey(value) is not null)
                return value;
        }

        var identityVersion = activityType.ContainingAssembly.Identity.Version;
        return $"{identityVersion.Major}.{identityVersion.Minor}.{Math.Max(identityVersion.Build, 0)}";
    }

    private static string? ToSemVerSortKey(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var buildSeparator = version.IndexOf('+');
        var withoutBuild = buildSeparator < 0 ? version : version.Substring(0, buildSeparator);
        if (buildSeparator >= 0)
        {
            var build = version.Substring(buildSeparator + 1);
            if (build.IndexOf('+') >= 0 || !AreValidIdentifiers(build, allowNumericLeadingZero: true))
                return null;
        }

        var dashIndex = withoutBuild.IndexOf('-');
        var core = dashIndex < 0 ? withoutBuild : withoutBuild.Substring(0, dashIndex);
        var prerelease = dashIndex < 0 ? null : withoutBuild.Substring(dashIndex + 1);
        var parts = core.Split('.');
        if (parts.Length != 3 || !parts.All(IsValidNumericIdentifier))
            return null;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return null;

        var normalizedCore = Pad(major) + "." + Pad(minor) + "." + Pad(patch);
        if (prerelease is null)
            return normalizedCore + ".1";

        if (!AreValidIdentifiers(prerelease, allowNumericLeadingZero: false))
            return null;
        var identifiers = prerelease.Split('.');
        var normalizedPrerelease = string.Join(".", identifiers.Select(identifier => identifier.All(IsAsciiDigit)
            ? "0" + identifier.PadLeft(11, '0')
            : "1" + identifier));
        return normalizedCore + ".0." + normalizedPrerelease;
    }

    private static bool IsValidNumericIdentifier(string value) =>
        value.Length > 0 && value.All(IsAsciiDigit) && (value.Length == 1 || value[0] != '0');

    private static bool AreValidIdentifiers(string value, bool allowNumericLeadingZero) => value
        .Split('.')
        .All(identifier => identifier.Length > 0 &&
                           identifier.All(IsAsciiIdentifierCharacter) &&
                           (allowNumericLeadingZero || !identifier.All(IsAsciiDigit) || IsValidNumericIdentifier(identifier)));

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';

    private static bool IsAsciiIdentifierCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';

    private static string Pad(int value) => value.ToString(CultureInfo.InvariantCulture).PadLeft(11, '0');

    private sealed class ActivityMetadata
    {
        public ActivityMetadata(
            INamedTypeSymbol activityType,
            string methodName,
            string versionId,
            ITypeSymbol? resultType,
            ImmutableArray<InputMetadata> inputs,
            ImmutableArray<OutputMetadata> outputs,
            ImmutableArray<OutcomeMetadata> outcomes,
            Location location)
        {
            ActivityType = activityType;
            MethodName = methodName;
            VersionId = versionId;
            ResultType = resultType;
            Inputs = inputs;
            Outputs = outputs;
            Outcomes = outcomes;
            Location = location;
        }

        public INamedTypeSymbol ActivityType { get; }
        public string MethodName { get; }
        public string VersionId { get; }
        public ITypeSymbol? ResultType { get; }
        public ImmutableArray<InputMetadata> Inputs { get; }
        public ImmutableArray<OutputMetadata> Outputs { get; }
        public ImmutableArray<OutcomeMetadata> Outcomes { get; }
        public Location Location { get; }
        public string ActivityDisplayName => ActivityType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        public string CallTypeName => MethodName + "Call";
        public string OutputsTypeName => MethodName + "OutputSources";
        public string OutcomesTypeName => MethodName + "OutcomeSources";
    }

    private sealed class InputMetadata
    {
        public InputMetadata(string name, string key, ITypeSymbol? type, float order, bool isRequired)
        {
            Name = name;
            Key = key;
            Type = type;
            Order = order;
            IsRequired = isRequired;
        }

        public string Name { get; }
        public string Key { get; }
        public ITypeSymbol? Type { get; }
        public float Order { get; }
        public bool IsRequired { get; }
    }

    private sealed class OutputMetadata
    {
        public OutputMetadata(string name, string key, ITypeSymbol? type)
        {
            Name = name;
            Key = key;
            Type = type;
        }

        public string Name { get; }
        public string Key { get; }
        public ITypeSymbol? Type { get; }
    }

    private sealed class OutcomeMetadata
    {
        public OutcomeMetadata(string name, string key)
        {
            Name = name;
            Key = key;
        }

        public string Name { get; }
        public string Key { get; }
    }

    private sealed class MemberShape
    {
        public MemberShape(string name, string key, ITypeSymbol? type)
        {
            Name = name;
            Key = key;
            Type = type;
        }

        public string Name { get; }
        public string Key { get; }
        public ITypeSymbol? Type { get; }
    }

    private sealed class NamedKey
    {
        public NamedKey(string name, string key)
        {
            Name = name;
            Key = key;
        }

        public string Name { get; }
        public string Key { get; }
    }

}
