using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elsa.Architecture.Tests;

internal static class JsonBaselineSourceScan
{
    internal sealed record Finding(string Setting, string Signature, int Line, string Site = "");
    internal sealed record Registration(string File, IReadOnlySet<string> Settings, string Reason, string Approval);
    private static readonly HashSet<string> WorkflowDataFiles = new(StringComparer.Ordinal)
    {
        "ActivityCompletionProjector.cs", "ActivityInputHydrator.cs", "StepVariableSession.cs", "RuntimeActivityInputMaterializer.cs",
        "RuntimeInputBindingCompiler.cs", "WorkflowExecutableInputValidator.cs", "WorkflowExecutionStartDispatch.cs", "StatefulActivity.cs",
        "BackgroundActivityCompletionCodec.cs", "RuntimeDurableValueStorageDrivers.cs", "GraphActivity.cs", "WorkflowBuilder.cs",
        "DescriptorPayloadSerializer.cs", "ObjectConverter.cs", "RuntimeWorkflowStateSeed.cs", "ActivitySecretInputResolver.cs", "ParentResumeExecutor.cs"
    };

    internal static string Identity(string path, Finding finding) => path.Replace('\\', '/') + " | " + finding.Site + " | " + finding.Signature;
    internal static bool IsWorkflowData(string path) => WorkflowDataFiles.Contains(Path.GetFileName(path));
    internal static bool IsAccounted(string path, Finding finding, IReadOnlySet<string> legacy, IReadOnlyList<Registration> registrations) =>
        finding.Setting == "optionless"
            ? !IsWorkflowData(path) && legacy.Contains(Identity(path, finding))
            : registrations.Any(row => row.File == path.Replace('\\', '/') && row.Settings.Contains(finding.Setting));

    internal static IReadOnlyList<Registration> ReadRegister(string markdown)
    {
        var heading = markdown.IndexOf("## Baseline deviations", StringComparison.Ordinal);
        if (heading < 0) throw new InvalidDataException("Missing Baseline deviations register.");
        var rows = new List<Registration>();
        var end = markdown.IndexOf("\n## ", heading + 1, StringComparison.Ordinal);
        var section = end < 0 ? markdown[heading..] : markdown[heading..end];
        foreach (var line in section.Split('\n').Where(line => line.TrimStart().StartsWith("| `", StringComparison.Ordinal)))
        {
            var cells = line.Split('|').Skip(1).SkipLast(1).Select(cell => cell.Trim().Trim('`')).ToArray();
            if (cells.Length != 4 || cells[2].Length < 10 || cells[3].Length == 0) throw new InvalidDataException("Every baseline register row needs file, specific settings, reason and approval.");
            rows.Add(new(cells[0], cells[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal), cells[2], cells[3]));
        }
        if (rows.Count == 0) throw new InvalidDataException("Baseline register is empty.");
        return rows;
    }

    internal static IReadOnlyList<Finding> Scan(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var findings = new List<Finding>();
        var optionAliases = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(x => x.Alias is not null && x.Name?.ToString().EndsWith("JsonSerializerOptions", StringComparison.Ordinal) == true)
            .Select(x => x.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        bool OptionsType(string value) => (value == "JsonSerializerOptions" || value.EndsWith(".JsonSerializerOptions", StringComparison.Ordinal)) || optionAliases.Contains(value);
        bool BaselineCopy(ArgumentListSyntax? args) => args?.Arguments.Count == 1 &&
            (args.Arguments[0].Expression.ToString().EndsWith("JsonBaseline.Options", StringComparison.Ordinal) || args.Arguments[0].Expression.ToString().EndsWith("JsonBaseline.CreateCopy()", StringComparison.Ordinal));
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            if (OptionsType(creation.Type.ToString()) && !BaselineCopy(creation.ArgumentList)) Add("construction", creation);
        foreach (var creation in root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            var type = creation.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault()?.Type.ToString()
                ?? creation.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Type.ToString()
                ?? creation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.ReturnType.ToString();
            if (type is not null && OptionsType(type) && !BaselineCopy(creation.ArgumentList)) Add("construction", creation);
        }
        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            if (OptionsType(access.Expression.ToString()) && access.Name.Identifier.ValueText is "Web" or "Default") Add("defaults", access);
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var name = assignment.Left is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : assignment.Left.ToString();
            if (IsDeviation(name, assignment.Right.ToString())) Add(name, assignment);
        }
        foreach (var argument in root.DescendantNodes().OfType<AttributeArgumentSyntax>().Where(x => x.NameEquals is not null))
            if (argument.Ancestors().OfType<AttributeSyntax>().First().Name.ToString().Contains("JsonSourceGenerationOptions", StringComparison.Ordinal) && IsDeviation(argument.NameEquals!.Name.Identifier.ValueText, argument.Expression.ToString())) Add(argument.NameEquals.Name.Identifier.ValueText, argument);
        var serializerAliases = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(usingDirective => usingDirective.Alias is not null && usingDirective.Name?.ToString().EndsWith(".JsonSerializer", StringComparison.Ordinal) == true)
            .Select(usingDirective => usingDirective.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        var staticSerializer = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(usingDirective =>
            usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && usingDirective.Name?.ToString().EndsWith(".JsonSerializer", StringComparison.Ordinal) == true);
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var member = call.Expression as MemberAccessExpressionSyntax;
            var name = member?.Name.Identifier.ValueText ?? (call.Expression as SimpleNameSyntax)?.Identifier.ValueText;
            if (name is null) continue;
            var receiver = member?.Expression.ToString();
            var serializer = receiver is not null && (receiver == "JsonSerializer" || receiver.EndsWith(".JsonSerializer", StringComparison.Ordinal) || serializerAliases.Contains(receiver)) || member is null && staticSerializer;
            var arguments = call.ArgumentList.Arguments;
            var effective = arguments.Where(argument => argument.NameColon?.Name.Identifier.ValueText != "cancellationToken").ToArray();
            if (serializer && name is "Serialize" or "SerializeToNode" or "SerializeToElement" or "SerializeToUtf8Bytes" or "SerializeAsync" or "Deserialize" or "DeserializeAsync" or "DeserializeAsyncEnumerable")
            {
                var minimum = name == "SerializeAsync" ? 2 : 1;
                var last = effective.LastOrDefault()?.Expression;
                if (effective.Length == minimum || last?.IsKind(SyntaxKind.NullLiteralExpression) == true ||
                    effective.Any(argument => argument.NameColon?.Name.Identifier.ValueText is "options" or "jsonTypeInfo" && argument.Expression.IsKind(SyntaxKind.NullLiteralExpression)) ||
                    effective.Length == minimum + 1 && (last is TypeOfExpressionSyntax || last?.ToString().EndsWith(".GetType()", StringComparison.Ordinal) == true)) Add("optionless", call);
            }
            else if (!serializer && member is not null && name is "Deserialize" or "DeserializeAsync" &&
                (effective.Length == 0 || effective.Any(argument => argument.NameColon?.Name.Identifier.ValueText is "options" or "jsonTypeInfo" && argument.Expression.IsKind(SyntaxKind.NullLiteralExpression)) ||
                 member.Name is not GenericNameSyntax && effective.Length == 1 && effective[0].Expression is TypeOfExpressionSyntax)) Add("optionless", call);
            if (member is null) continue;
            if (member.Expression is MemberAccessExpressionSyntax list)
            {
                if (list.Name.Identifier.ValueText == "Converters")
                {
                    if (name is "Add" or "Insert") Add("converter:" + ConverterName(arguments.Last().Expression), call);
                    else if (name is "Remove" or "RemoveAt" or "Clear") Add("converter-mutation:" + name, call);
                }
                if (list.Name.Identifier.ValueText == "Modifiers" && name is "Add" or "Insert")
                    Add("modifier:" + ModifierName(arguments.Last().Expression), call);
            }
            if (name == "WithAddedModifier" && arguments.Count > 0) Add("modifier:" + ModifierName(arguments.Last().Expression), call);
        }
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(x => x.Left.ToString() == "Converters"))
            foreach (var converter in assignment.Right.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>()) Add("converter:" + ConverterName(converter), converter);
        // An inserted identical call must not borrow an existing receipt. Preserve a stable ordinal
        // within its fully qualified declaration and exact normalized expression.
        return findings.GroupBy(finding => (finding.Site, finding.Signature, finding.Setting))
            .SelectMany(group => group.Select((finding, index) => finding with { Site = finding.Site + "#" + (index + 1) })).ToArray();

        void Add(string setting, SyntaxNode node) => findings.Add(new(setting, node.WithoutTrivia().NormalizeWhitespace().ToFullString().Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal), node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, Site(node)));
    }

    private static string Site(SyntaxNode node)
    {
        var types = node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(type => type.Identifier.ValueText);
        var namespaces = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(space => space.Name.ToString());
        var owner = string.Join(".", namespaces.Concat(types));
        if (owner.Length == 0) owner = "<global>";
        var method = node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        var name = method switch { MethodDeclarationSyntax declaration => declaration.Identifier.ValueText, ConstructorDeclarationSyntax declaration => declaration.Identifier.ValueText, _ => node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? node.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "<initializer>" };
        return owner + "::" + name + (method is null ? "" : "(" + string.Join(",", method.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString())) + ")");
    }

    private static string ConverterName(ExpressionSyntax expression) => expression switch
    {
        ObjectCreationExpressionSyntax creation => creation.Type switch
        {
            GenericNameSyntax generic => generic.Identifier.ValueText,
            QualifiedNameSyntax qualified when qualified.Right is GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => creation.Type.ToString().Split('.').Last()
        },
        _ => expression.ToString()
    };
    private static string ModifierName(ExpressionSyntax expression) => expression is LambdaExpressionSyntax ? "lambda" : expression.ToString();
    private static bool IsDeviation(string name, string value) => name switch
    {
        "DictionaryKeyPolicy" => value != "null",
        "PropertyNameCaseInsensitive" => value != "false",
        "PropertyNamingPolicy" => value is not ("JsonNamingPolicy.CamelCase" or "JsonKnownNamingPolicy.CamelCase"),
        "NumberHandling" => value != "JsonNumberHandling.Strict",
        "DefaultIgnoreCondition" => value != "JsonIgnoreCondition.Never",
        "Encoder" => !value.EndsWith("JavaScriptEncoder.UnsafeRelaxedJsonEscaping", StringComparison.Ordinal) && !value.EndsWith("JsonBaseline.Options.Encoder", StringComparison.Ordinal),
        _ => false
    };
}
