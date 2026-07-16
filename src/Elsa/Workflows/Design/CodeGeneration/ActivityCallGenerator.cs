using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.Workflows.Design.CodeGeneration;

/// <summary>
/// Generates deterministic, authoring-only activity-call facades from compile-time activity metadata.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ActivityCallGenerator : IIncrementalGenerator
{
    public const string GeneratedHintName = "ElsaActivityCalls.g.cs";
    public const string MetadataHintName = "ElsaActivityCallMetadata.g.cs";
    public const string DuplicateFacadeDiagnosticId = "VFAUTHGEN001";
    public const string UnsupportedShapeDiagnosticId = "VFAUTHGEN002";

    private const string ActivityCallAttributeName = "Elsa.Workflows.Design.CodeGeneration.ActivityCallAttribute";
    private const string ActivityInputAttributeName = "Elsa.Workflows.Design.CodeGeneration.ActivityInputAttribute";
    private const string ActivityOutputAttributeName = "Elsa.Workflows.Design.CodeGeneration.ActivityOutputAttribute";
    private const string ActivityOutcomeAttributeName = "Elsa.Workflows.Design.CodeGeneration.ActivityOutcomeAttribute";

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
        context.RegisterPostInitializationOutput(static output => output.AddSource(
            MetadataHintName,
            SourceText.From(MetadataSource, Encoding.UTF8)));

        var activities = context.SyntaxProvider.ForAttributeWithMetadataName(
                ActivityCallAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => ReadActivity(attributeContext))
            .Collect();

        var referencedActivities = context.CompilationProvider.Select(
            static (compilation, cancellationToken) => ReadReferencedActivities(compilation, cancellationToken));

        context.RegisterSourceOutput(
            activities.Combine(referencedActivities),
            static (output, candidates) => Emit(output, candidates.Left.AddRange(candidates.Right)));
    }

    private static ActivityMetadata ReadActivity(GeneratorAttributeSyntaxContext context)
    {
        var activityType = (INamedTypeSymbol)context.TargetSymbol;
        var activityAttribute = context.Attributes[0];
        return ReadActivity(activityType, activityAttribute, context.TargetNode.GetLocation());
    }

    private static ActivityMetadata ReadActivity(
        INamedTypeSymbol activityType,
        AttributeData activityAttribute,
        Location location)
    {
        var arguments = activityAttribute.ConstructorArguments;
        var methodName = ReadString(arguments, 0);
        var versionId = ReadString(arguments, 1);
        var resultType = ReadType(arguments, 2);
        var inputs = activityType.GetAttributes()
            .Where(attribute => IsAttribute(attribute, ActivityInputAttributeName))
            .Select(ReadInput)
            .ToImmutableArray();
        var outputs = activityType.GetAttributes()
            .Where(attribute => IsAttribute(attribute, ActivityOutputAttributeName))
            .Select(ReadOutput)
            .ToImmutableArray();
        var outcomes = activityType.GetAttributes()
            .Where(attribute => IsAttribute(attribute, ActivityOutcomeAttributeName))
            .Select(ReadOutcome)
            .ToImmutableArray();

        return new ActivityMetadata(
            activityType,
            methodName,
            versionId,
            resultType,
            inputs,
            outputs,
            outcomes,
            location);
    }

    private static ImmutableArray<ActivityMetadata> ReadReferencedActivities(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var activities = ImmutableArray.CreateBuilder<ActivityMetadata>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assembly.GetTypeByMetadataName(ActivityCallAttributeName) is null)
                continue;
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
        var attribute = type.GetAttributes().FirstOrDefault(candidate => IsAttribute(candidate, ActivityCallAttributeName));
        if (attribute is not null)
            activities.Add(ReadActivity(type, attribute, Location.None));

        foreach (var nestedType in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectActivity(nestedType, activities, cancellationToken);
        }
    }

    private static InputMetadata ReadInput(AttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments;
        return new InputMetadata(
            ReadString(arguments, 0),
            ReadString(arguments, 1),
            ReadType(arguments, 2),
            ReadInt32(arguments, 3),
            ReadBoolean(arguments, 4, defaultValue: true));
    }

    private static OutputMetadata ReadOutput(AttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments;
        return new OutputMetadata(ReadString(arguments, 0), ReadString(arguments, 1), ReadType(arguments, 2));
    }

    private static OutcomeMetadata ReadOutcome(AttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments;
        return new OutcomeMetadata(ReadString(arguments, 0), ReadString(arguments, 1));
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

        var orderedInputs = activity.Inputs.OrderBy(input => input.Order).ToArray();
        if (orderedInputs.Select(input => input.Order).Distinct().Count() != orderedInputs.Length)
            return "input order values must be unique";
        if (orderedInputs.Any(input => input.Order < 0))
            return "input order values must be non-negative";
        if (orderedInputs.SkipWhile(input => input.IsRequired).Any(input => input.IsRequired))
            return "required inputs cannot follow optional inputs";

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
        var inputs = activity.Inputs.OrderBy(input => input.Order).ToArray();
        source.Append("    public static ").Append(activity.CallTypeName).Append(' ').Append(activity.MethodName).AppendLine("(");
        source.Append("        this global::Elsa.Workflows.Design.Core.Authoring.ISequenceBuilder sequence")
            .AppendLine(inputs.Length == 0 ? ")" : ",");
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            source.Append("        global::Elsa.Workflows.Design.Core.Authoring.ActivityArgument<")
                .Append(DisplayType(input.Type)).Append("> ").Append(input.Name);
            if (!input.IsRequired)
                source.Append(" = default");
            source.AppendLine(index == inputs.Length - 1 ? ")" : ",");
        }
        source.AppendLine("    {");
        source.AppendLine("        if (sequence is null)");
        source.AppendLine("            throw new global::System.ArgumentNullException(nameof(sequence));");
        source.AppendLine();
        source.Append("        var call = sequence.Add<").Append(DisplayType(activity.ActivityType)).Append(", ")
            .Append(DisplayType(activity.ResultType)).AppendLine(">(");
        source.Append("            ").Append(SymbolDisplay.FormatLiteral(activity.VersionId, quote: true));
        if (inputs.Length == 0)
        {
            source.AppendLine(");");
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
            source.AppendLine("            });");
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

    private static bool IsAttribute(AttributeData attribute, string metadataName) =>
        string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal);

    private static string ReadString(ImmutableArray<TypedConstant> arguments, int index) =>
        index < arguments.Length ? arguments[index].Value as string ?? string.Empty : string.Empty;

    private static ITypeSymbol? ReadType(ImmutableArray<TypedConstant> arguments, int index) =>
        index < arguments.Length ? arguments[index].Value as ITypeSymbol : null;

    private static int ReadInt32(ImmutableArray<TypedConstant> arguments, int index) =>
        index < arguments.Length && arguments[index].Value is int value ? value : -1;

    private static bool ReadBoolean(ImmutableArray<TypedConstant> arguments, int index, bool defaultValue) =>
        index < arguments.Length && arguments[index].Value is bool value ? value : defaultValue;

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
        public InputMetadata(string name, string key, ITypeSymbol? type, int order, bool isRequired)
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
        public int Order { get; }
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

    private const string MetadataSource = @"// <auto-generated/>
#nullable enable

namespace Elsa.Workflows.Design.CodeGeneration;

[global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ActivityCallAttribute : global::System.Attribute
{
    public ActivityCallAttribute(string methodName, string activityVersionId, global::System.Type resultType)
    {
        MethodName = methodName;
        ActivityVersionId = activityVersionId;
        ResultType = resultType;
    }

    public string MethodName { get; }
    public string ActivityVersionId { get; }
    public global::System.Type ResultType { get; }
}

[global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ActivityInputAttribute : global::System.Attribute
{
    public ActivityInputAttribute(string name, string key, global::System.Type type, int order, bool isRequired = true)
    {
        Name = name;
        Key = key;
        Type = type;
        Order = order;
        IsRequired = isRequired;
    }

    public string Name { get; }
    public string Key { get; }
    public global::System.Type Type { get; }
    public int Order { get; }
    public bool IsRequired { get; }
}

[global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ActivityOutputAttribute : global::System.Attribute
{
    public ActivityOutputAttribute(string name, string key, global::System.Type type)
    {
        Name = name;
        Key = key;
        Type = type;
    }

    public string Name { get; }
    public string Key { get; }
    public global::System.Type Type { get; }
}

[global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ActivityOutcomeAttribute : global::System.Attribute
{
    public ActivityOutcomeAttribute(string name, string key)
    {
        Name = name;
        Key = key;
    }

    public string Name { get; }
    public string Key { get; }
}
";
}
