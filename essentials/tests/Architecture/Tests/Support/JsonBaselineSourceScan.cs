using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Elsa.Architecture.Tests;

internal static class JsonBaselineSourceScan
{
    internal sealed record Finding(string Setting, string Signature, int Line, string Site = "", bool CarriesData = false);
    internal sealed record Registration(string File, IReadOnlySet<string> Settings, string Reason, string Approval);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<SyntaxNode>, HashSet<string>> ContextNameCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<SyntaxNode>, TypeDeclarationSyntax[]> TypeCache = new();
    private static readonly Lazy<CSharpCompilation> ConstructionCompilation = new(() =>
    {
        // The test host's dependency closure includes the shipping contracts. Reuse their actual
        // metadata, rather than guessing constructor, delegate, indexer or collection signatures.
        // Do not inject global usings or combine unrelated projects into one synthetic assembly.
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted
                ? trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                : [typeof(object).Assembly.Location, typeof(Enumerable).Assembly.Location])
            .Append(typeof(System.Text.Json.JsonSerializerOptions).Assembly.Location)
            .Append(typeof(Elsa.Serialization.Core.JsonBaseline).Assembly.Location)
            .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        return CSharpCompilation.Create("JsonBaselineConstructionTargets", references: paths.Select(path => MetadataReference.CreateFromFile(path)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
    });
    private static readonly HashSet<string> WorkflowDataFiles = new(StringComparer.Ordinal)
    {
        "ActivityCompletionProjector.cs", "ActivityInputHydrator.cs", "StepVariableSession.cs", "RuntimeActivityInputMaterializer.cs",
        "RuntimeInputBindingCompiler.cs", "WorkflowExecutableInputValidator.cs", "WorkflowExecutionStartDispatch.cs", "StatefulActivity.cs",
        "BackgroundActivityCompletionCodec.cs", "RuntimeDurableValueStorageDrivers.cs", "GraphActivity.cs", "WorkflowBuilder.cs",
        "DescriptorPayloadSerializer.cs", "ObjectConverter.cs", "RuntimeWorkflowStateSeed.cs", "ActivitySecretInputResolver.cs", "ParentResumeExecutor.cs",
        "VariableMapper.cs", "VariableDefaultValueFormatter.cs", "ActivityInputOption.cs", "AgentSchemaCompiler.cs", "AgentSchemaValidator.cs", "Elsa3ValueFlowLowerer.cs",
        "Elsa3HttpTriggerResponseModeRewrite.cs",
        "RuntimeInputBindingResolver.cs",
        "AgentActivityProvider.cs", "GraphActivityProvider.cs", "GraphBodyReferenceValidator.cs", "ShellFeatureStore.cs", "JsonShellFeatureConfigurationStore.cs", "SecretSettingMask.cs"
    };

    internal static string Identity(string path, Finding finding) => path.Replace('\\', '/') + " | " + finding.Site + " | " + finding.Signature;
    internal static string RegistrationKey(Finding finding) => finding.Setting + "@" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(finding.Site + " | " + finding.Signature)))[..12];
    internal static bool IsWorkflowData(string path) => WorkflowDataFiles.Contains(Path.GetFileName(path));
    internal static bool IsAccounted(string path, Finding finding, IReadOnlySet<string> legacy, IReadOnlyList<Registration> registrations) =>
        finding.Setting == "construction-unresolved" ? false : finding.Setting == "optionless"
            ? !IsWorkflowData(path) && !finding.CarriesData && legacy.Contains(Identity(path, finding))
            : registrations.Any(row => row.File == path.Replace('\\', '/') && row.Settings.Contains(RegistrationKey(finding)));

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

    internal static IReadOnlyList<Finding> Scan(string source, IReadOnlyList<SyntaxNode>? storageRoots = null) =>
        Scan(CSharpSyntaxTree.ParseText(source).GetRoot(), storageRoots);

    internal static IReadOnlyList<Finding> Scan(SyntaxNode root, IReadOnlyList<SyntaxNode>? storageRoots = null,
        JsonBaselineConstructionContexts? constructionContexts = null)
    {
        if (constructionContexts is null) return ScanCore(root, storageRoots, null);
        // Parse before walking syntax: enabled #if code is otherwise DisabledTextTrivia and
        // cannot be recovered by asking for semantic facts after the source scan skipped it.
        return constructionContexts.Views(root).SelectMany(view => ScanCore(view.Root, view.StorageRoots, view.Model)).Distinct().ToArray();
    }

    private static IReadOnlyList<Finding> ScanCore(SyntaxNode root, IReadOnlyList<SyntaxNode>? storageRoots,
        Func<SemanticModel>? constructionModel)
    {
        var findings = new List<Finding>();
        var optionAliases = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(x => x.Alias is not null && x.Name?.ToString().EndsWith("JsonSerializerOptions", StringComparison.Ordinal) == true)
            .Select(x => x.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        bool OptionsType(string value) => value.TrimEnd('?') is { } name && (name == "JsonSerializerOptions" || name.EndsWith(".JsonSerializerOptions", StringComparison.Ordinal) || optionAliases.Contains(name));
        bool BaselineCopy(ArgumentListSyntax? args) => args?.Arguments.Count == 1 && IsBaselineExpression(args.Arguments[0].Expression);
        SemanticModel? cachedConstructionModel = null;
        SemanticModel Model() => cachedConstructionModel ??= constructionModel?.Invoke() ?? ConstructionCompilation.Value.AddSyntaxTrees(root.SyntaxTree)
            .GetSemanticModel(root.SyntaxTree, ignoreAccessibility: true);
        bool? KnownOptionsType(ExpressionSyntax expression)
        {
            var type = Model().GetTypeInfo(expression).Type;
            return HasUnresolvedTypeFacts(type) || type!.TypeKind == TypeKind.Dynamic ? null : IsFrameworkOptions(type);
        }
        NullableFlowView? nullableFlow = null;
        bool NullProfile(ExpressionSyntax expression, SyntaxNode site)
        {
            var semantic = Model();
            // ! and #nullable affect analysis, never runtime values. Ask the compiler about
            // an enabled view without those suppressions so actual null assignments, branch
            // joins and alias copies cannot disappear behind NotNull or FlowState.None.
            var flow = nullableFlow ??= new NullableFlowView(root, semantic);
            var selected = flow.Select(expression);
            return IsNullProfile(selected, selected, () => flow.Model);
        }
        bool ProfileExpression(ExpressionSyntax expression, SyntaxNode site) => IsProfileExpression(expression, site, NullProfile);
        var constructionTargets = new Dictionary<ImplicitObjectCreationExpressionSyntax, bool>();
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            if (OptionsType(creation.Type.ToString()) && !BaselineCopy(creation.ArgumentList) && KnownOptionsType(creation) != false)
                Add(constructionModel is not null && KnownOptionsType(creation) is null ? "construction-unresolved" : "construction", creation);
        foreach (var creation in root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            if (!BaselineCopy(creation.ArgumentList) && IsOptionsConstruction(creation)) Add("construction", creation);
        }
        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            if (OptionsType(access.Expression.ToString()) && access.Name.Identifier.ValueText is "Web" or "Default" && KnownOptionsType(access.Expression) != false) Add("defaults", access);
        var contextNames = storageRoots is null ? FindContextNames([root]) : ContextNameCache.GetValue(storageRoots, FindContextNames);
        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            if (access.Name.Identifier.ValueText == "Default" &&
                (contextNames.Contains(ContextTypeName(access.Expression.ToString())) || ContextTypeName(access.Expression.ToString()).EndsWith("JsonContext", StringComparison.Ordinal))) Add("context-default", access);
        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            var attributeName = AttributeTypeName(attribute);
            if (attributeName.Contains("JsonSourceGenerationOptions", StringComparison.Ordinal))
                foreach (var argument in attribute.ArgumentList?.Arguments.Where(argument => argument.NameEquals is null) ?? [])
                    Add("source-generation-defaults", argument);
            if (attributeName is "Column" or "ColumnAttribute" or "System.ComponentModel.DataAnnotations.Schema.Column" or "System.ComponentModel.DataAnnotations.Schema.ColumnAttribute")
                foreach (var argument in attribute.ArgumentList?.Arguments.Where(argument => argument.NameEquals?.Name.Identifier.ValueText == "TypeName") ?? [])
                    foreach (var storage in StorageTypes(argument.Expression, attribute, storageRoots)) Add("storage:" + storage, argument);
        }
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is ElementAccessExpressionSyntax index)
            {
                var collection = DeclaredType(index.Expression, assignment);
                if (IsConverterCollection(collection) || index.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Converters" }) Add("converter-mutation:replace", assignment);
                if (IsModifierCollection(collection) || index.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Modifiers" }) Add("modifier-mutation:replace", assignment);
            }
            var name = assignment.Left is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : assignment.Left.ToString();
            if (IsDeviation(name, assignment.Right.ToString()))
            {
                if (constructionModel is not null && assignment.Left is MemberAccessExpressionSyntax receiver &&
                    HasUnresolvedTypeFacts(Model().GetTypeInfo(receiver.Expression).Type)) Add("construction-unresolved", receiver.Expression);
                if (IsOptionsAssignment(assignment, OptionsType, IsOptionsConstruction, KnownOptionsType)) Add(name, assignment);
            }
        }
        foreach (var argument in root.DescendantNodes().OfType<AttributeArgumentSyntax>().Where(x => x.NameEquals is not null))
            if (AttributeTypeName(argument.Ancestors().OfType<AttributeSyntax>().First()).Contains("JsonSourceGenerationOptions", StringComparison.Ordinal))
            {
                var name = argument.NameEquals!.Name.Identifier.ValueText;
                if (IsDeviation(name, argument.Expression.ToString())) Add(name, argument);
                if (name == "UseStringEnumConverter" && argument.Expression.ToString() == "true") Add("converter:JsonStringEnumConverter", argument);
                if (name == "Converters")
                    foreach (var converter in argument.Expression.DescendantNodesAndSelf().OfType<TypeOfExpressionSyntax>()) Add("converter:" + converter.Type.ToString().Split('.').Last(), converter);
            }
        var serializerAliases = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(usingDirective => usingDirective.Alias is not null && usingDirective.Name?.ToString().EndsWith(".JsonSerializer", StringComparison.Ordinal) == true)
            .Select(usingDirective => usingDirective.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        var staticSerializer = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(usingDirective =>
            usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && usingDirective.Name?.ToString().EndsWith(".JsonSerializer", StringComparison.Ordinal) == true);
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var member = call.Expression as MemberAccessExpressionSyntax;
            var binding = call.Expression as MemberBindingExpressionSyntax;
            var name = member?.Name.Identifier.ValueText ?? binding?.Name.Identifier.ValueText ?? (call.Expression as SimpleNameSyntax)?.Identifier.ValueText;
            if (name is null) continue;
            var receiver = member?.Expression.ToString();
            var serializer = receiver is not null && (receiver == "JsonSerializer" || receiver.EndsWith(".JsonSerializer", StringComparison.Ordinal) || serializerAliases.Contains(receiver)) || member is null && staticSerializer;
            var arguments = call.ArgumentList.Arguments;
            if (name is "HasColumnType" or "SetColumnType" && arguments.Count > 0)
            {
                var typeArgument = arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText is "typeName" or "type" or "columnType")
                    ?? arguments.FirstOrDefault(argument => argument.NameColon is null);
                if (typeArgument is not null)
                    foreach (var storage in StorageTypes(typeArgument.Expression, call, storageRoots)) Add("storage:" + storage, call);
            }
            if (name is "Column" or "AlterColumn" && arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == "type") is { } columnType)
                foreach (var storage in StorageTypes(columnType.Expression, call, storageRoots)) Add("storage:" + storage, call);
            if (name == "ToJson")
            {
                var target = member is null ? null : DeclaredType(member.Expression, call);
                Add(target?.Contains("NavigationBuilder", StringComparison.Ordinal) == true || target?.Contains("EntityTypeBuilder", StringComparison.Ordinal) == true ? "storage:owned-json" : "json-method:ToJson", call);
            }
            var effective = arguments.Where(argument => argument.NameColon?.Name.Identifier.ValueText != "cancellationToken").ToArray();
            var httpRead = name is "ReadFromJsonAsync" or "ReadFromJsonAsAsyncEnumerable" or "GetFromJsonAsync";
            var httpWrite = name is "WriteAsJsonAsync" or "PostAsJsonAsync" or "PutAsJsonAsync" or "PatchAsJsonAsync";
            var httpResult = receiver is "Results" or "TypedResults" or "Microsoft.AspNetCore.Http.Results" or "Microsoft.AspNetCore.Http.TypedResults";
            if (httpRead || httpWrite || httpResult && name is "Json" or "Ok" or "Created" or "Accepted" or "BadRequest" or "Conflict" or "UnprocessableEntity")
            {
                var profileStart = httpRead ? name == "GetFromJsonAsync" ? 1 : 0 : httpWrite ? name == "WriteAsJsonAsync" ? 1 : 2 : 1;
                var acceptsProfile = !httpResult || name == "Json";
                var explicitProfile = acceptsProfile && (arguments.Any(argument => argument.NameColon?.Name.Identifier.ValueText is "options" or "jsonTypeInfo" or "jsonSerializerOptions" && !NullProfile(argument.Expression, call)) ||
                    effective.Skip(profileStart).Take(httpResult ? 1 : 2).Any(argument => argument.NameColon is null && ProfileExpression(argument.Expression, call)));
                var bodyCount = name is "Created" or "Accepted" ? 2 : 1;
                if (!explicitProfile && (httpRead || httpWrite || effective.Length >= bodyCount)) Add("optionless", call);
            }
            if (serializer && name is "Serialize" or "SerializeToNode" or "SerializeToElement" or "SerializeToUtf8Bytes" or "SerializeAsync" or "Deserialize" or "DeserializeAsync" or "DeserializeAsyncEnumerable")
            {
                var namedProfile = effective.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText is "options" or "jsonTypeInfo" or "context");
                // Required stream/writer/type/bool arguments are never evidence of a profile.
                // An unfamiliar profile expression can name its actual parameter explicitly.
                if (namedProfile is not null ? NullProfile(namedProfile.Expression, call) :
                    !effective.Skip(1).Any(argument => argument.NameColon is null && ProfileExpression(argument.Expression, call))) Add("optionless", call);
            }
            else if (!serializer && (member is not null || binding is not null) && name is "Deserialize" or "DeserializeAsync")
            {
                // A domain codec's nullable input is not an STJ options parameter. Resolve valid
                // business methods before applying JSON-profile flow; incomplete controls keep
                // the conservative syntax fallback used by this scan.
                if (Model().GetSymbolInfo(call).Symbol is IMethodSymbol called &&
                    called.ContainingNamespace.ToDisplayString() is not ("System.Text.Json" or "System.Text.Json.Nodes")) continue;
                var profile = effective.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText is "options" or "jsonTypeInfo");
                if (profile is null)
                {
                    var positional = effective.Where(argument => argument.NameColon is null).ToArray();
                    var typeArgument = (member?.Name ?? binding!.Name) is not GenericNameSyntax && positional.Length > 0 && IsTypeExpression(positional[0].Expression, call);
                    profile = positional.ElementAtOrDefault(typeArgument ? 1 : 0);
                }
                if (profile is null || NullProfile(profile.Expression, call)) Add("optionless", call);
            }
            if (member is null) continue;
            var collectionType = name is "Add" or "Insert" or "Remove" or "RemoveAt" or "Clear" ? DeclaredType(member.Expression, call) : null;
            if (IsConverterCollection(collectionType))
            {
                if (name is "Add" or "Insert") Add("converter:" + ConverterName(arguments.Last().Expression), call);
                else if (name is "Remove" or "RemoveAt" or "Clear") Add("converter-mutation:" + name, call);
            }
            if (IsModifierCollection(collectionType))
            {
                if (name is "Add" or "Insert") Add("modifier:" + ModifierName(arguments.Last().Expression), call);
                else if (name is "Remove" or "RemoveAt" or "Clear") Add("modifier-mutation:" + name, call);
            }
            if (member.Expression is MemberAccessExpressionSyntax list)
            {
                if (list.Name.Identifier.ValueText == "Converters" && !IsConverterCollection(collectionType))
                {
                    if (name is "Add" or "Insert") Add("converter:" + ConverterName(arguments.Last().Expression), call);
                    else if (name is "Remove" or "RemoveAt" or "Clear") Add("converter-mutation:" + name, call);
                }
                if (list.Name.Identifier.ValueText == "Modifiers" && !IsModifierCollection(collectionType) && name is "Add" or "Insert")
                    Add("modifier:" + ModifierName(arguments.Last().Expression), call);
                if (list.Name.Identifier.ValueText == "Modifiers" && !IsModifierCollection(collectionType) && name is "Remove" or "RemoveAt" or "Clear") Add("modifier-mutation:" + name, call);
            }
            if (name == "WithAddedModifier" && arguments.Count > 0) Add("modifier:" + ModifierName(arguments.Last().Expression), call);
        }
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(x => x.Left.ToString() is "Converters" or "Modifiers"))
        {
            var expressions = assignment.Right switch
            {
                InitializerExpressionSyntax initializer => initializer.Expressions,
                CollectionExpressionSyntax collection => SyntaxFactory.SeparatedList(collection.Elements.OfType<ExpressionElementSyntax>().Select(element => element.Expression)),
                _ => default
            };
            foreach (var expression in expressions)
                Add(assignment.Left.ToString() == "Converters" ? "converter:" + ConverterName(expression) : "modifier:" + ModifierName(expression), expression);
        }
        // An inserted identical call must not borrow an existing receipt. Preserve a stable ordinal
        // within its fully qualified declaration and exact normalized expression.
        return findings.GroupBy(finding => (finding.Site, finding.Signature, finding.Setting))
            .SelectMany(group => group.Select((finding, index) => finding with { Site = finding.Site + "#" + (index + 1) })).ToArray();

        void Add(string setting, SyntaxNode node) => findings.Add(new(setting, node.WithoutTrivia().NormalizeWhitespace().ToFullString().Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal), node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, Site(node), setting == "optionless" && node is InvocationExpressionSyntax invocation && CarriesData(invocation)));

        string ContextTypeName(string name)
        {
            var alias = root.DescendantNodes().OfType<UsingDirectiveSyntax>().FirstOrDefault(usingDirective => usingDirective.Alias?.Name.Identifier.ValueText == name);
            return (alias?.Name?.ToString() ?? name).Split('.').Last();
        }

        bool IsOptionsConstruction(ImplicitObjectCreationExpressionSyntax creation)
        {
            if (constructionTargets.TryGetValue(creation, out var known)) return known;
            var resolved = IsOptionsConstructionCore(creation);
            constructionTargets.Add(creation, resolved);
            return resolved;
        }

        bool IsOptionsConstructionCore(ImplicitObjectCreationExpressionSyntax creation)
        {
            var target = Model().GetTypeInfo(creation).ConvertedType;
            // A resolved business type is authoritative even when it happens to be named
            // JsonSerializerOptions. Only System.Text.Json owns the protected options type.
            if (!HasUnresolvedTypeFacts(target))
            {
                return IsFrameworkOptions(target!);
            }
            if (constructionModel is not null)
            {
                // The production sweep has its owning sources and built dependency closure.
                // An unresolved target is a coverage failure, never an implicit exemption.
                Add("construction-unresolved", creation);
                return false;
            }
            if (target is IErrorTypeSymbol error)
            {
                // Source-scan controls intentionally include incomplete fragments. An unbound
                // named parameter can still give new() an exact declared target; an ambiguous
                // symbol never licenses borrowing a same-named declaration elsewhere.
                if (error.CandidateSymbols.Length != 0) return false;
                if (OptionsType(error.ToDisplayString())) return true;
            }
            var declared = ImplicitConstructionType(creation);
            return declared is not null && OptionsType(declared);
        }

        string? ImplicitConstructionType(ImplicitObjectCreationExpressionSyntax creation)
        {
            ExpressionSyntax value = creation;
            while (value.Parent is ParenthesizedExpressionSyntax parentheses) value = parentheses;
            // Assignments and arguments have their own target contracts. They never borrow the
            // enclosing method's return type when the target is unrelated or cannot be resolved.
            if (value.Parent is AssignmentExpressionSyntax assignment && assignment.Right == value)
                return DeclaredType(assignment.Left, assignment);
            if (value.Parent is ArgumentSyntax argument)
                return ArgumentTargetType(argument);
            // Stop at the nearest target boundary. In particular, an object local function or
            // lambda inside an options-returning method must not borrow the outer return type.
            foreach (var ancestor in creation.Ancestors())
                switch (ancestor)
                {
                    case VariableDeclarationSyntax variable: return variable.Type.ToString();
                    case LocalFunctionStatementSyntax local: return local.ReturnType.ToString();
                    case LambdaExpressionSyntax: return null;
                    case AnonymousMethodExpressionSyntax: return null;
                    case PropertyDeclarationSyntax property: return property.Type.ToString();
                    case MethodDeclarationSyntax method: return method.ReturnType.ToString();
                }
            return null;
        }

        string? ArgumentTargetType(ArgumentSyntax argument)
        {
            if (argument.Parent is not ArgumentListSyntax arguments || arguments.Parent is not InvocationExpressionSyntax invocation) return null;
            var member = invocation.Expression as MemberAccessExpressionSyntax;
            var name = member?.Name.Identifier.ValueText ?? (invocation.Expression as SimpleNameSyntax)?.Identifier.ValueText;
            if (name is null) return null;
            var enclosingType = invocation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var owner = member is null || member.Expression is ThisExpressionSyntax ? enclosingType
                : root.DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault(type =>
                    type.Identifier.ValueText == ContextTypeName(DeclaredType(member.Expression, invocation) ?? member.Expression.ToString()));
            // A local function shadows same-named members. Otherwise only declarations on the
            // selected owner are candidates; unrelated methods elsewhere cannot supply the type.
            var local = member is null ? invocation.Ancestors().OfType<BlockSyntax>()
                .Select(block => block.Statements.OfType<LocalFunctionStatementSyntax>().Where(function => function.Identifier.ValueText == name).ToArray())
                .FirstOrDefault(functions => functions.Length != 0) : null;
            var declarations = local is not null ? local.Select(function => function.ParameterList).ToArray()
                : owner?.Members.OfType<MethodDeclarationSyntax>().Where(method => method.Identifier.ValueText == name)
                    .Select(method => method.ParameterList).ToArray() ?? [];
            if (declarations.Length != 0)
            {
                var types = declarations.Where(parameters => arguments.Arguments.Count <= parameters.Parameters.Count &&
                        arguments.Arguments.Count >= parameters.Parameters.Count(parameter => parameter.Default is null))
                    .Select(parameters => argument.NameColon is { } named
                        ? parameters.Parameters.FirstOrDefault(parameter => parameter.Identifier.ValueText == named.Name.Identifier.ValueText)?.Type?.ToString()
                        : parameters.Parameters.ElementAtOrDefault(arguments.Arguments.IndexOf(argument))?.Type?.ToString())
                    .Distinct(StringComparer.Ordinal).ToArray();
                return types.Length == 1 ? types[0] : null; // Do not guess an unresolved overload.
            }
            var receiver = member?.Expression.ToString();
            var serializer = owner is null && receiver is ("JsonSerializer" or "System.Text.Json.JsonSerializer" or "global::System.Text.Json.JsonSerializer") ||
                receiver is not null && root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(usingDirective =>
                    usingDirective.Alias?.Name.Identifier.ValueText == receiver && usingDirective.Name?.ToString().Replace("global::", "", StringComparison.Ordinal) == "System.Text.Json.JsonSerializer") ||
                member is null && root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(usingDirective =>
                    usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && usingDirective.Name?.ToString().Replace("global::", "", StringComparison.Ordinal) == "System.Text.Json.JsonSerializer");
            return serializer && argument.NameColon?.Name.Identifier.ValueText == "options" ? "JsonSerializerOptions" : null;
        }
    }

    private static bool IsConverterCollection(string? type) => type is not null && type.Contains("JsonConverter", StringComparison.Ordinal);
    private static bool IsModifierCollection(string? type) => type == "JsonModifiers" || type is not null && type.Contains("JsonTypeInfo", StringComparison.Ordinal) && type.Contains("Action", StringComparison.Ordinal);

    private static IEnumerable<string> StorageTypes(ExpressionSyntax expression, SyntaxNode site, IReadOnlyList<SyntaxNode>? roots = null, HashSet<string>? visited = null)
    {
        visited ??= new(StringComparer.Ordinal);
        var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(expression.SyntaxTree) + ":" + expression.SpanStart + ":" + expression.ToString();
        if (!visited.Add(key)) return ["dynamic"];
        if (expression is LiteralExpressionSyntax literal)
        {
            var type = literal.Token.ValueText.Trim().ToLowerInvariant();
            return type is "json" or "jsonb" ? [type] : [];
        }
        if (expression is ThrowExpressionSyntax) return [];
        if (expression is ParenthesizedExpressionSyntax parentheses) return StorageTypes(parentheses.Expression, site, roots, visited);
        if (expression is SwitchExpressionSyntax choice)
            return choice.Arms.SelectMany(arm => StorageTypes(arm.Expression, site, roots, new(visited, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal);
        if (expression is ConditionalExpressionSyntax conditional)
            return new[] { conditional.WhenTrue, conditional.WhenFalse }.SelectMany(branch => StorageTypes(branch, site, roots, new(visited, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal);
        if (expression is IdentifierNameSyntax identifier)
        {
            var method = site.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
            var scope = (SyntaxNode?)method
                ?? site.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var initializer = scope?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText)?.Initializer?.Value;
            var assignments = scope?.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(assignment =>
                assignment.Left is IdentifierNameSyntax target && target.Identifier.ValueText == identifier.Identifier.ValueText).ToArray() ?? [];
            if (assignments.Length > 0) return assignments.Select(assignment => assignment.Right).Concat(initializer is null ? [] : new[] { initializer })
                .SelectMany(value => StorageTypes(value, value, roots, new(visited, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal);
            if (initializer is not null && initializer.ToString() != expression.ToString()) return StorageTypes(initializer, initializer, roots, visited);
            if (method is MethodDeclarationSyntax declaration)
            {
                var parameter = declaration.ParameterList.Parameters.FirstOrDefault(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText);
                if (parameter is not null)
                {
                    var index = declaration.ParameterList.Parameters.IndexOf(parameter);
                    var calls = (roots ?? [site.SyntaxTree.GetRoot()]).SelectMany(root => root.DescendantNodes()).OfType<InvocationExpressionSyntax>().Where(call =>
                        (call.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText == declaration.Identifier.ValueText ||
                        (call.Expression as SimpleNameSyntax)?.Identifier.ValueText == declaration.Identifier.ValueText).Where(call =>
                        call.Expression is not MemberAccessExpressionSyntax access || access.Expression.ToString() is "base" or "this"
                            ? IsOwnerOrDerived(call.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault(), declaration.Ancestors().OfType<TypeDeclarationSyntax>().First(), roots ?? [site.SyntaxTree.GetRoot()])
                            : access.Expression.ToString().Split('.').Last() == declaration.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText).Where(call =>
                        {
                            var argument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                            var actual = argument is null ? null : DeclaredType(argument, call);
                            if (argument is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Entity") actual = "EntityTypeBuilder";
                            var expected = declaration.ParameterList.Parameters.FirstOrDefault()?.Type?.ToString();
                            return actual is null || expected is null || actual.Split('<')[0].Split('.').Last() == expected.Split('<')[0].Split('.').Last();
                        }).ToArray();
                    if (calls.Length > 0) return calls.SelectMany(call =>
                    {
                        var argument = call.ArgumentList.Arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == identifier.Identifier.ValueText)
                            ?? call.ArgumentList.Arguments.ElementAtOrDefault(index);
                        return argument is null ? ["dynamic"] : StorageTypes(argument.Expression, call, roots, new(visited, StringComparer.Ordinal));
                    }).Distinct(StringComparer.Ordinal);
                }
            }
        }
        return ["dynamic"];
    }

    private static bool IsOwnerOrDerived(TypeDeclarationSyntax? candidate, TypeDeclarationSyntax owner, IReadOnlyList<SyntaxNode> roots, HashSet<string>? visited = null)
    {
        if (candidate is null) return false;
        if (candidate.Identifier.ValueText == owner.Identifier.ValueText) return true;
        visited ??= new(StringComparer.Ordinal);
        if (!visited.Add(candidate.Identifier.ValueText)) return false;
        var types = TypeCache.GetValue(roots, static trees => trees.SelectMany(tree => tree.DescendantNodes()).OfType<TypeDeclarationSyntax>().ToArray());
        return candidate.BaseList?.Types.Any(parent =>
        {
            var name = parent.Type.ToString().Split('.').Last().Split('<')[0];
            return name == owner.Identifier.ValueText || types.Where(type => type.Identifier.ValueText == name)
                .Any(type => IsOwnerOrDerived(type, owner, roots, new(visited, StringComparer.Ordinal)));
        }) == true;
    }

    private static bool CarriesData(InvocationExpressionSyntax invocation)
    {
        var member = invocation.Expression as MemberAccessExpressionSyntax;
        var methodName = member?.Name ?? (invocation.Expression as MemberBindingExpressionSyntax)?.Name;
        if (methodName is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Any(argument =>
            DataType(argument, new HashSet<string>(StringComparer.Ordinal)))) return true;
        var arguments = invocation.ArgumentList.Arguments;
        foreach (var expression in arguments.SelectMany(argument => argument.Expression.DescendantNodesAndSelf()).OfType<ExpressionSyntax>())
            if (DeclaredType(expression, invocation, inferDynamic: false) is { } type && DataType(SyntaxFactory.ParseTypeName(type), new HashSet<string>(StringComparer.Ordinal))) return true;
        if (methodName?.Identifier.ValueText.StartsWith("Deserialize", StringComparison.Ordinal) == true &&
            arguments.Any(argument => IsTypeExpression(argument.Expression, invocation))) return true;
        return arguments.Any(argument => argument.Expression is InvocationExpressionSyntax call &&
            call.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText is "GetProperty" or "Clone" or "EnumerateArray");

        bool DataType(TypeSyntax type, HashSet<string> aliases)
        {
            if (type is NullableTypeSyntax nullable) return DataType(nullable.ElementType, aliases);
            if (type is ArrayTypeSyntax array) return DataType(array.ElementType, aliases);
            if (type is QualifiedNameSyntax qualified) return DataType(qualified.Right, aliases);
            if (type is AliasQualifiedNameSyntax global) return DataType(global.Name, aliases);
            if (type is GenericNameSyntax generic) return generic.TypeArgumentList.Arguments.Any(argument => DataType(argument, new(aliases, StringComparer.Ordinal)));
            var name = type.ToString();
            if (name is "object" or "Object" or "dynamic" or "JsonElement" or "JsonNode" or "JsonObject" or "JsonArray" or "JsonValue" or "ExpandoObject") return true;
            if (invocation.Ancestors().OfType<MethodDeclarationSyntax>().Any(method => method.TypeParameterList?.Parameters.Any(parameter => parameter.Identifier.ValueText == name) == true) ||
                invocation.Ancestors().OfType<TypeDeclarationSyntax>().Any(owner => owner.TypeParameterList?.Parameters.Any(parameter => parameter.Identifier.ValueText == name) == true)) return true;
            var alias = invocation.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()
                .FirstOrDefault(usingDirective => usingDirective.Alias?.Name.Identifier.ValueText == name)?.Name;
            return alias is not null && aliases.Add(name) && DataType(SyntaxFactory.ParseTypeName(alias.ToString()), aliases);
        }
    }

    private static bool IsOptionsAssignment(AssignmentExpressionSyntax assignment, Func<string, bool> optionsType,
        Func<ImplicitObjectCreationExpressionSyntax, bool>? implicitOptionsType = null, Func<ExpressionSyntax, bool?>? knownOptionsType = null)
    {
        if (assignment.Left is MemberAccessExpressionSyntax member)
        {
            if (knownOptionsType?.Invoke(member.Expression) is { } actualOptions) return actualOptions;
            if (DeclaredType(member.Expression, assignment) is { } actualType && optionsType(actualType)) return true;
            var receiver = member.Expression.ToString();
            if (member.Expression is IdentifierNameSyntax identifier)
            {
                var method = assignment.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
                var parameterType = method?.ParameterList.Parameters.FirstOrDefault(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText)?.Type?.ToString();
                if (parameterType is not null) return optionsType(parameterType);
                var scope = (SyntaxNode?)method ?? assignment.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                var declaration = scope?.DescendantNodes().OfType<VariableDeclaratorSyntax>().FirstOrDefault(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText);
                var receiverType = (declaration?.Parent as VariableDeclarationSyntax)?.Type.ToString();
                if (receiverType == "var" && declaration?.Initializer?.Value is ObjectCreationExpressionSyntax created) receiverType = created.Type.ToString();
                if (receiverType == "var" && declaration?.Initializer?.Value is { } initializer && IsBaselineExpression(initializer)) return true;
                if (receiverType is not null && receiverType != "var") return optionsType(receiverType);
            }
            return receiver.EndsWith("options", StringComparison.OrdinalIgnoreCase) || receiver.EndsWith("Options.Value.SerializerOptions", StringComparison.Ordinal) ||
                receiver.Contains(".JsonSerializerOptions", StringComparison.Ordinal) || receiver.Contains(".SerializerOptions", StringComparison.Ordinal);
        }
        var creation = assignment.Ancestors().FirstOrDefault(node => node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax);
        if (creation is ObjectCreationExpressionSyntax explicitCreation)
            return knownOptionsType?.Invoke(explicitCreation) ?? optionsType(explicitCreation.Type.ToString());
        var implicitCreation = creation as ImplicitObjectCreationExpressionSyntax;
        if (implicitCreation is not null && implicitOptionsType is not null) return implicitOptionsType(implicitCreation);
        var declaredType = implicitCreation?.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault()?.Type.ToString()
            ?? implicitCreation?.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Type.ToString()
            ?? implicitCreation?.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.ReturnType.ToString();
        return declaredType is not null && optionsType(declaredType);
    }

    private static bool IsFrameworkOptions(ITypeSymbol type) => type is INamedTypeSymbol { Name: "JsonSerializerOptions", Arity: 0 } &&
        type.ContainingNamespace.ToDisplayString() == "System.Text.Json" &&
        type.ContainingAssembly.Identity.Name == typeof(System.Text.Json.JsonSerializerOptions).Assembly.GetName().Name;

    internal static bool HasUnresolvedTypeFacts(ITypeSymbol? type)
    {
        var pending = new Stack<ITypeSymbol>();
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        if (type is null) return true;
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            if (current.TypeKind == TypeKind.Error) return true;
            switch (current)
            {
                case INamedTypeSymbol named:
                    if (named.ContainingType is { } owner) pending.Push(owner);
                    foreach (var argument in named.TypeArguments) pending.Push(argument);
                    break;
                case IArrayTypeSymbol array: pending.Push(array.ElementType); break;
                case IPointerTypeSymbol pointer: pending.Push(pointer.PointedAtType); break;
                case IFunctionPointerTypeSymbol function:
                    pending.Push(function.Signature.ReturnType);
                    foreach (var parameter in function.Signature.Parameters) pending.Push(parameter.Type);
                    break;
            }
        }
        return false;
    }

    private static bool IsBaselineExpression(ExpressionSyntax expression)
    {
        var text = expression.ToString();
        return new[] { "JsonBaseline", "Elsa.Serialization.Core.JsonBaseline", "global::Elsa.Serialization.Core.JsonBaseline" }
            .Any(owner => text == owner + ".Options" || text == owner + ".CreateCopy()");
    }

    private static bool IsTypeExpression(ExpressionSyntax expression, SyntaxNode scope) => expression is TypeOfExpressionSyntax ||
        expression.ToString().EndsWith(".GetType()", StringComparison.Ordinal) || DeclaredType(expression, scope) is "Type" or "System.Type";

    private static string AttributeTypeName(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString().Replace("global::", "", StringComparison.Ordinal);
        foreach (var alias in attribute.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>().Where(usingDirective => usingDirective.Alias is not null))
        {
            var prefix = alias.Alias!.Name.Identifier.ValueText;
            if (name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal)) return alias.Name + name[prefix.Length..];
        }
        return name;
    }

    private static HashSet<string> FindContextNames(IReadOnlyList<SyntaxNode> roots) => roots.SelectMany(tree => tree.DescendantNodes()).OfType<TypeDeclarationSyntax>()
        .Where(type => type.BaseList?.Types.Any(parent => parent.Type.ToString().EndsWith("JsonSerializerContext", StringComparison.Ordinal)) == true)
        .Select(type => type.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);

    private static bool IsProfileExpression(ExpressionSyntax expression, SyntaxNode site, Func<ExpressionSyntax, SyntaxNode, bool> nullProfile)
    {
        if (nullProfile(expression, site)) return false;
        var type = DeclaredType(expression, site);
        if (type is not null)
        {
            type = type.TrimEnd('?');
            var alias = site.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>().FirstOrDefault(usingDirective => usingDirective.Alias?.Name.Identifier.ValueText == type);
            type = alias?.Name?.ToString() ?? type;
            type = type.Replace("global::", "", StringComparison.Ordinal);
            return type is "JsonSerializerOptions" or "System.Text.Json.JsonSerializerOptions" or
                "JsonTypeInfo" or "System.Text.Json.Serialization.Metadata.JsonTypeInfo" or
                "JsonSerializerContext" or "System.Text.Json.Serialization.JsonSerializerContext" ||
                type.StartsWith("JsonTypeInfo<", StringComparison.Ordinal) ||
                type.StartsWith("System.Text.Json.Serialization.Metadata.JsonTypeInfo<", StringComparison.Ordinal) ||
                FindContextNames([site.SyntaxTree.GetRoot()]).Contains(type.Split('.').Last());
        }
        return IsBaselineExpression(expression);
    }

    private static bool IsNullProfile(ExpressionSyntax expression, SyntaxNode site, Func<SemanticModel>? model = null, HashSet<string>? visited = null)
    {
        if (expression.IsKind(SyntaxKind.NullLiteralExpression) || expression.IsKind(SyntaxKind.DefaultLiteralExpression) || expression is DefaultExpressionSyntax) return true;
        if (expression is ParenthesizedExpressionSyntax parentheses) return IsNullProfile(parentheses.Expression, site, model, visited);
        if (expression is CastExpressionSyntax cast) return IsNullProfile(cast.Expression, site, model, visited);
        if (expression is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression)) return IsNullProfile(suppression.Operand, site, model, visited);
        if (model is not null)
        {
            var semantic = model();
            // The compiler tracks reaching writes, branches, coalescing assignments, captures and
            // lexical symbol identity. A write elsewhere is never proof that this value is non-null.
            // Inspect the operand before !: suppression must not turn a known null into a profile.
            var type = semantic.GetTypeInfo(expression);
            if (type.Nullability.FlowState == NullableFlowState.MaybeNull) return true;
            if (type.Nullability.FlowState == NullableFlowState.NotNull)
                return expression is IdentifierNameSyntax local && CapturedNullBefore(local, site, semantic);
        }
        if (expression is not IdentifierNameSyntax name) return false;
        visited ??= new(StringComparer.Ordinal);
        if (!visited.Add(name.Identifier.ValueText)) return false;
        var method = site.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        if (method is null) return false;
        var initializer = method.DescendantNodes().OfType<VariableDeclaratorSyntax>().FirstOrDefault(variable => variable.Identifier.ValueText == name.Identifier.ValueText)?.Initializer?.Value;
        return initializer is not null && IsNullProfile(initializer, site, model, visited);
    }

    private sealed class NullableFlowView
    {
        private const string Annotation = "json-baseline-original-expression";
        private readonly IReadOnlyDictionary<string, ExpressionSyntax>? expressions;
        internal SemanticModel Model { get; }

        internal NullableFlowView(SyntaxNode root, SemanticModel model)
        {
            Model = model;
            var options = (CSharpCompilationOptions)model.Compilation.Options;
            if (options.NullableContextOptions == NullableContextOptions.Enable &&
                !root.DescendantNodes().Any(node => node.IsKind(SyntaxKind.SuppressNullableWarningExpression)) &&
                !root.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsKind(SyntaxKind.NullableDirectiveTrivia))) return;
            var rewritten = new SuppressionRewriter().Visit(root)!;
            var tree = root.SyntaxTree.WithRootAndOptions(rewritten, root.SyntaxTree.Options);
            Model = model.Compilation.ReplaceSyntaxTree(root.SyntaxTree, tree)
                .WithOptions(options.WithNullableContextOptions(NullableContextOptions.Enable)).GetSemanticModel(tree, ignoreAccessibility: true);
            expressions = tree.GetRoot().DescendantNodesAndSelf().OfType<ExpressionSyntax>()
                .SelectMany(expression => expression.GetAnnotations(Annotation).Select(annotation => (annotation.Data!, expression)))
                .ToDictionary(pair => pair.Item1, pair => pair.expression, StringComparer.Ordinal);
        }

        internal ExpressionSyntax Select(ExpressionSyntax expression) => expressions is null ? expression :
            expressions.TryGetValue(Key(expression), out var selected) ? selected :
                throw new InvalidOperationException("A JSON profile expression lost its compiler null-flow identity.");

        private static string Key(SyntaxNode node) => node.SpanStart + ":" + node.Span.Length;

        private sealed class SuppressionRewriter : CSharpSyntaxRewriter
        {
            internal SuppressionRewriter() : base(visitIntoStructuredTrivia: true) { }

            public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) => trivia.IsKind(SyntaxKind.NullableDirectiveTrivia)
                ? SyntaxFactory.EndOfLine("\n") : base.VisitTrivia(trivia);

            public override SyntaxNode? Visit(SyntaxNode? node)
            {
                if (node is null) return null;
                var result = base.Visit(node)!;
                if (result is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                    result = suppression.Operand.WithTriviaFrom(suppression);
                return node is ExpressionSyntax ? result.WithAdditionalAnnotations(new SyntaxAnnotation(Annotation, Key(node))) : result;
            }
        }
    }

    private static bool CapturedNullBefore(IdentifierNameSyntax name, SyntaxNode site, SemanticModel model)
    {
        // Nullable flow deliberately does not inline a delegate invocation. Its statically known
        // captured writes still matter: `Action reset = () => profile = null; reset(); Serialize(..., profile)`.
        var target = model.GetSymbolInfo(name).Symbol;
        if (target is not (ILocalSymbol or IParameterSymbol)) return false;
        var owner = ExecutableOwner(site);
        if (owner is null) return false;
        var graphs = new Dictionary<SyntaxNode, ControlFlowGraph?>();
        var facts = new Dictionary<SyntaxNode, ProfileControlFlow?>();
        ProfileControlFlow? Facts(SyntaxNode node)
        {
            var executable = ExecutableOwner(node);
            if (executable is null) return null;
            if (!facts.TryGetValue(executable, out var selected))
            {
                selected = Graph(executable) is { } graph ? new ProfileControlFlow(graph) : null;
                facts.Add(executable, selected);
            }
            return selected;
        }
        ControlFlowGraph? Graph(SyntaxNode executable)
        {
            if (graphs.TryGetValue(executable, out var known)) return known;
            ControlFlowGraph? selected;
            if (executable is LocalFunctionStatementSyntax local)
                selected = ExecutableOwner(local.Parent!) is { } localParent && Graph(localParent) is { } localGraph && model.GetDeclaredSymbol(local) is { } localSymbol
                    ? localGraph.GetLocalFunctionControlFlowGraph(localSymbol) : null;
            else if (executable is AnonymousFunctionExpressionSyntax anonymous)
            {
                var anonymousGraph = ExecutableOwner(anonymous.Parent!) is { } anonymousParent ? Graph(anonymousParent) : null;
                var operation = anonymousGraph is null ? null : ProfileControlFlow.Operations(anonymousGraph).OfType<IFlowAnonymousFunctionOperation>()
                    .SingleOrDefault(operation => operation.Syntax.Span == anonymous.Span);
                selected = operation is null ? null : anonymousGraph!.GetAnonymousFunctionControlFlowGraph(operation);
            }
            else selected = model.GetOperation(executable) switch
            {
                IMethodBodyOperation body => ControlFlowGraph.Create(body),
                IConstructorBodyOperation body => ControlFlowGraph.Create(body),
                IBlockOperation { Parent: null } body => ControlFlowGraph.Create(body),
                _ => null
            };
            graphs.Add(executable, selected);
            return selected;
        }
        bool Reachable(SyntaxNode node) => Facts(node)?.IsReachable(node) != false;
        var calls = owner.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => ExecutableOwner(call) == owner && Reachable(call) &&
                WritesNull(Callable(call.Expression), new HashSet<SyntaxNode>())).ToArray();
        // A null-forgiving RHS changes compiler annotations, not the value passed to STJ.
        var nullSites = calls.Cast<SyntaxNode>().Concat(owner.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(assignment => ExecutableOwner(assignment) == owner && Reachable(assignment) &&
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, target) && SuppressedNull(assignment.Right))
            .Cast<SyntaxNode>()).Concat(owner.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(variable => ExecutableOwner(variable) == owner && Reachable(variable) &&
                SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(variable), target) && variable.Initializer is { } initializer && SuppressedNull(initializer.Value))).ToArray();
        if (nullSites.Length == 0) return false;
        var clears = owner.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(assignment =>
            ExecutableOwner(assignment) == owner &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, target) &&
            !NullLiteral(assignment.Right) && !SuppressedNull(assignment.Right) && model.GetTypeInfo(assignment.Right).Nullability.FlowState == NullableFlowState.NotNull)
            .Cast<SyntaxNode>().Concat(owner.DescendantNodes().OfType<VariableDeclaratorSyntax>().Where(variable =>
                ExecutableOwner(variable) == owner && SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(variable), target) &&
                variable.Initializer is { } initializer && !NullLiteral(initializer.Value) && !SuppressedNull(initializer.Value) &&
                model.GetTypeInfo(initializer.Value).Nullability.FlowState == NullableFlowState.NotNull)).ToArray();
        return Facts(name)?.NullCanReach(name, nullSites, clears) ?? true;

        SyntaxNode? Callable(ExpressionSyntax expression)
        {
            var symbol = model.GetSymbolInfo(expression).Symbol;
            if (symbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } && expression is MemberAccessExpressionSyntax member)
                symbol = model.GetSymbolInfo(member.Expression).Symbol;
            var declaration = symbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            return declaration switch
            {
                LocalFunctionStatementSyntax function => (SyntaxNode?)function.Body ?? function.ExpressionBody?.Expression,
                VariableDeclaratorSyntax { Initializer.Value: LambdaExpressionSyntax lambda } => lambda.Body,
                VariableDeclaratorSyntax { Initializer.Value: AnonymousMethodExpressionSyntax anonymous } => anonymous.Block,
                _ => null
            };
        }

        bool WritesNull(SyntaxNode? body, HashSet<SyntaxNode> visited)
        {
            if (body is null || !visited.Add(body)) return false;
            var callableOwner = ExecutableOwner(body);
            return body.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                ExecutableOwner(assignment) == callableOwner && Reachable(assignment) && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, target) &&
                (NullLiteral(assignment.Right) || model.GetTypeInfo(assignment.Right).Nullability.FlowState == NullableFlowState.MaybeNull)) ||
                body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(call =>
                    ExecutableOwner(call) == callableOwner && Reachable(call) && WritesNull(Callable(call.Expression), visited));
        }

        static bool NullLiteral(ExpressionSyntax expression) => expression switch
        {
            ParenthesizedExpressionSyntax parentheses => NullLiteral(parentheses.Expression),
            CastExpressionSyntax cast => NullLiteral(cast.Expression),
            PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression) => NullLiteral(suppression.Operand),
            _ => expression.IsKind(SyntaxKind.NullLiteralExpression) || expression.IsKind(SyntaxKind.DefaultLiteralExpression) || expression is DefaultExpressionSyntax
        };

        static bool SuppressedNull(ExpressionSyntax expression) => expression switch
        {
            ParenthesizedExpressionSyntax parentheses => SuppressedNull(parentheses.Expression),
            CastExpressionSyntax cast => SuppressedNull(cast.Expression),
            PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                NullLiteral(suppression.Operand) || SuppressedNull(suppression.Operand),
            ConditionalExpressionSyntax conditional => SuppressedNull(conditional.WhenTrue) || SuppressedNull(conditional.WhenFalse),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) => SuppressedNull(binary.Right),
            _ => false
        };
    }

    private static SyntaxNode? ExecutableOwner(SyntaxNode node) => node.AncestorsAndSelf().FirstOrDefault(ancestor =>
        ancestor is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);

    // Compiler blocks carry constant-branch reachability, jumps and loop back-edges. Walk from
    // every known null to the actual argument read, cutting only paths that execute a non-null write.
    private sealed class ProfileControlFlow
    {
        private readonly ControlFlowGraph graph;
        private readonly (IOperation Operation, BasicBlock Block, int Index)[] operations;

        internal ProfileControlFlow(ControlFlowGraph graph)
        {
            this.graph = graph;
            operations = graph.Blocks.SelectMany(block => BlockOperations(block).Select((operation, index) => (operation, block, index))).ToArray();
        }

        internal bool IsReachable(SyntaxNode node) => Point(node)?.Block.IsReachable != false;
        internal bool NullCanReach(SyntaxNode use, IReadOnlyList<SyntaxNode> nulls, IReadOnlyList<SyntaxNode> clears)
        {
            var destination = Point(use);
            var starts = nulls.Select(Point).ToArray();
            // Missing operation coverage never proves a null safe. Exception continuations are
            // implicit CFG edges: without a dedicated proof, retain the refusal for those shapes.
            if (destination is null || starts.Any(start => start is null) ||
                graph.OriginalOperation.Syntax.DescendantNodes().Any(node => node is TryStatementSyntax)) return true;
            var cuts = clears.Select(Point).Where(point => point is not null).Select(point => point!.Value).ToArray();
            var pending = new Queue<(BasicBlock Block, int Index)>();
            foreach (var start in starts.Select(point => point!.Value).Where(point => point.Block.IsReachable))
                pending.Enqueue((start.Block, start.Index + 1));
            var visited = new HashSet<(int Block, int Index)>();
            while (pending.TryDequeue(out var cursor))
            {
                if (!visited.Add((cursor.Block.Ordinal, cursor.Index))) continue;
                var firstClear = cuts.Where(cut => cut.Block.Ordinal == cursor.Block.Ordinal && cut.Index >= cursor.Index)
                    .Select(cut => cut.Index).DefaultIfEmpty(int.MaxValue).Min();
                if (destination.Value.Block.Ordinal == cursor.Block.Ordinal && destination.Value.Index >= cursor.Index &&
                    destination.Value.Index < firstClear) return true;
                if (firstClear != int.MaxValue) continue;
                foreach (var next in Successors(cursor.Block).Where(block => block.IsReachable)) pending.Enqueue((next, 0));
            }
            return false;
        }

        private (BasicBlock Block, int Index)? Point(SyntaxNode node) => operations
            .Where(entry => entry.Operation.Syntax.SyntaxTree == node.SyntaxTree && entry.Operation.Syntax.Span.Contains(node.Span))
            .OrderBy(entry => entry.Operation.Syntax.Span.Length).ThenBy(entry => entry.Index)
            .Select(entry => ((BasicBlock Block, int Index)?)(entry.Block, entry.Index)).FirstOrDefault();

        private static IEnumerable<BasicBlock> Successors(BasicBlock block)
        {
            var conditional = block.ConditionalSuccessor?.Destination;
            var fallThrough = block.FallThroughSuccessor?.Destination;
            if (block.BranchValue is { ConstantValue: { HasValue: true, Value: bool value } } && block.ConditionKind != ControlFlowConditionKind.None)
            {
                var next = value == (block.ConditionKind == ControlFlowConditionKind.WhenTrue) ? conditional : fallThrough;
                if (next is not null) yield return next;
                yield break;
            }
            if (conditional is not null) yield return conditional;
            if (fallThrough is not null && fallThrough != conditional) yield return fallThrough;
        }

        internal static IEnumerable<IOperation> Operations(ControlFlowGraph graph) => graph.Blocks.SelectMany(BlockOperations);
        private static IEnumerable<IOperation> BlockOperations(BasicBlock block)
        {
            var roots = block.BranchValue is { } branch ? block.Operations.Append(branch) : block.Operations;
            var pending = new Stack<(IOperation Operation, bool Visited)>(roots.Reverse().Select(operation => (operation, false)));
            while (pending.TryPop(out var current))
            {
                if (current.Visited) { yield return current.Operation; continue; }
                pending.Push((current.Operation, true));
                foreach (var child in current.Operation.ChildOperations.Reverse()) pending.Push((child, false));
            }
        }
    }

    private static string? DeclaredType(ExpressionSyntax expression, SyntaxNode site, HashSet<string>? visited = null, bool inferDynamic = true)
    {
        if (expression is CastExpressionSyntax cast) return cast.Type.ToString();
        if (expression is ObjectCreationExpressionSyntax created) return created.Type.ToString();
        if (expression is ParenthesizedExpressionSyntax parentheses) return DeclaredType(parentheses.Expression, site, visited, inferDynamic);
        if (expression is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression)) return DeclaredType(suppression.Operand, site, visited, inferDynamic);
        if (expression is MemberAccessExpressionSyntax member)
        {
            if (IsBaselineExpression(member)) return "JsonSerializerOptions";
            if (member.Expression is ThisExpressionSyntax)
            {
                // An explicit this member cannot bind to a same-named local or parameter.
                var enclosingType = site.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                var ownProperty = enclosingType?.Members.OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(property => property.Identifier.ValueText == member.Name.Identifier.ValueText);
                var ownField = enclosingType?.Members.OfType<FieldDeclarationSyntax>()
                    .FirstOrDefault(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == member.Name.Identifier.ValueText));
                return ownProperty?.Type.ToString() ?? ownField?.Declaration.Type.ToString()
                    ?? (member.Name.Identifier.ValueText == "SerializerOptions" ? "JsonSerializerOptions" : null);
            }
            if (member.Expression is BaseExpressionSyntax)
                return member.Name.Identifier.ValueText == "SerializerOptions" ? "JsonSerializerOptions"
                    : DeclaredType(SyntaxFactory.IdentifierName(member.Name.Identifier), site, visited, inferDynamic);
            var owner = DeclaredType(member.Expression, site, visited, inferDynamic);
            if (member.Name.Identifier.ValueText == "Options" && owner?.Split('.').Last() == "JsonConversionContract") return "JsonSerializerOptions";
            if (member.Name.Identifier.ValueText == "Value" && owner?.EndsWith("?", StringComparison.Ordinal) == true) return owner[..^1];
            var ownerDeclaration = site.SyntaxTree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(type => type.Identifier.ValueText == (owner ?? member.Expression.ToString()).Split('.').Last());
            var property = ownerDeclaration?.Members.OfType<PropertyDeclarationSyntax>().FirstOrDefault(property => property.Identifier.ValueText == member.Name.Identifier.ValueText);
            if (property is not null) return property.Type.ToString();
            var field = ownerDeclaration?.Members.OfType<FieldDeclarationSyntax>().FirstOrDefault(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == member.Name.Identifier.ValueText));
            if (field is not null) return field.Declaration.Type.ToString();
            if (member.Name.Identifier.ValueText == "SerializerOptions") return "JsonSerializerOptions";
            if (member.Name.Identifier.ValueText == "Converters" && KnownType(owner, "JsonSerializerOptions")) return "JsonConverters";
            if (member.Name.Identifier.ValueText == "Modifiers" && KnownType(owner, "DefaultJsonTypeInfoResolver")) return "JsonModifiers";
            if (member.ToString().Contains(".WireContext.", StringComparison.Ordinal)) return "JsonTypeInfo";
        }
        if (expression is not IdentifierNameSyntax identifier) return null;
        visited ??= new(StringComparer.Ordinal);
        if (!visited.Add(identifier.Identifier.ValueText)) return null;
        var method = site.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        var parameter = method?.ParameterList.Parameters.FirstOrDefault(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText);
        if (parameter is not null) return parameter.Type?.ToString();
        var scope = (SyntaxNode?)method ?? site.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var declaration = scope?.DescendantNodes().OfType<VariableDeclaratorSyntax>().FirstOrDefault(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText)
            ?? site.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Members.OfType<FieldDeclarationSyntax>()
                .SelectMany(field => field.Declaration.Variables).FirstOrDefault(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText);
        if (declaration is null)
            return site.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Members.OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(property => property.Identifier.ValueText == identifier.Identifier.ValueText)?.Type.ToString();
        var type = (declaration.Parent as VariableDeclarationSyntax)?.Type.ToString();
        // A dynamic business value can change after its initializer. Mutation analysis may
        // follow its known options origin; legacy/data admission must remain conservative.
        if (type == "dynamic" && !inferDynamic) return type;
        if (type is not ("var" or "dynamic")) return type;
        var inferred = declaration.Initializer?.Value switch
        {
            ObjectCreationExpressionSyntax creation => creation.Type.ToString(),
            IdentifierNameSyntax alias => DeclaredType(alias, site, visited, inferDynamic),
            MemberAccessExpressionSyntax access => DeclaredType(access, site, visited, inferDynamic),
            TypeOfExpressionSyntax => "Type",
            InvocationExpressionSyntax invocation when IsBaselineExpression(invocation) => "JsonSerializerOptions",
            _ when declaration.Initializer?.Value is { } initializer && initializer.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "GetTypeInfo" && access.Expression.ToString().Contains(".WireContext", StringComparison.Ordinal)) == true => "JsonTypeInfo",
            _ => null
        };
        return inferred ?? (type == "dynamic" ? "dynamic" : null);

        bool KnownType(string? typeName, string expected)
        {
            if (typeName is null) return false;
            typeName = typeName.TrimEnd('?');
            if (typeName.Split('.').Last() == expected) return true;
            return site.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>().Any(alias =>
                alias.Alias?.Name.Identifier.ValueText == typeName && alias.Name?.ToString().Split('.').Last() == expected);
        }
    }

    private static string Site(SyntaxNode node)
    {
        var types = node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(type => type.Identifier.ValueText);
        var namespaces = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(space => space.Name.ToString());
        var owner = string.Join(".", namespaces.Concat(types));
        if (owner.Length == 0) owner = "<global>";
        var method = node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        var name = method switch { MethodDeclarationSyntax declaration => declaration.Identifier.ValueText, ConstructorDeclarationSyntax declaration => declaration.Identifier.ValueText, _ => node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? node.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "<initializer>" };
        var locals = node.Ancestors().OfType<LocalFunctionStatementSyntax>().Reverse().Select(local => local.Identifier.ValueText);
        return owner + "::" + name + (method is null ? "" : "(" + string.Join(",", method.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString())) + ")") + string.Concat(locals.Select(local => "::" + local));
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
        "Encoder" => value is not ("JavaScriptEncoder.UnsafeRelaxedJsonEscaping" or "System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping" or "global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping" or "JsonBaseline.Options.Encoder" or "Elsa.Serialization.Core.JsonBaseline.Options.Encoder" or "global::Elsa.Serialization.Core.JsonBaseline.Options.Encoder"),
        "UnmappedMemberHandling" => value != "JsonUnmappedMemberHandling.Skip",
        "MaxDepth" => value is not ("0" or "64"),
        "WriteIndented" or "AllowTrailingCommas" or "IncludeFields" or "IgnoreReadOnlyFields" or "IgnoreReadOnlyProperties" => value != "false",
        "ReadCommentHandling" or "CommentHandling" => value is not ("JsonCommentHandling.Disallow" or "System.Text.Json.JsonCommentHandling.Disallow" or "global::System.Text.Json.JsonCommentHandling.Disallow"),
        "ReferenceHandler" => value != "null",
        _ => false
    };
}
