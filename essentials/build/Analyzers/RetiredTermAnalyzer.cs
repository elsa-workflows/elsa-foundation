using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.Architecture.Analyzers;

/// <summary>
/// Refuses retired terms in type and namespace names: every type declaration (class, struct, interface, record, enum,
/// delegate, at any accessibility, nested or not) and every namespace declaration.
/// </summary>
/// <remarks>
/// <para>
/// Which terms a file is held to comes from <see cref="ConfigurationKey"/> in the .editorconfig sections that apply to
/// that file, so a term is activated domain by domain: a section such as <c>[Essentials/src/Tasks/**.cs]</c> holds the
/// Tasks domain to its list and no other folder. A file no section configures is held to nothing.
/// </para>
/// <para>
/// A configuration value that cannot be parsed is itself an error (<see cref="InvalidConfigurationId"/>) on every file it
/// applies to, so a typo cannot quietly switch the rule off.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RetiredTermAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The .editorconfig key listing the retired terms; see <see cref="RetiredTermList"/> for its grammar.</summary>
    public const string ConfigurationKey = "elsa_naming.retired_terms";

    public const string TypeNameId = "ELSANAME001";
    public const string NamespaceId = "ELSANAME002";
    public const string InvalidConfigurationId = "ELSANAME003";

    private const string Category = "Naming";

    private static readonly DiagnosticDescriptor TypeNameRule = new(
        TypeNameId,
        "Type name uses a retired term",
        "Type '{0}' uses the retired term '{1}'; use '{2}' instead",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A retired term may not appear in a type name, at any accessibility.");

    private static readonly DiagnosticDescriptor NamespaceRule = new(
        NamespaceId,
        "Namespace uses a retired term",
        "Namespace '{0}' uses the retired term '{1}'; use '{2}' instead",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A retired term may not appear in a namespace name.");

    private static readonly DiagnosticDescriptor InvalidConfigurationRule = new(
        InvalidConfigurationId,
        "Retired-term configuration is invalid",
        "The " + ConfigurationKey + " setting that applies to this file is invalid: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The retired-term list could not be parsed, so no term would be enforced for this file.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(TypeNameRule, NamespaceRule, InvalidConfigurationRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            // Every file under one section carries the same value, so each distinct value is parsed once.
            var lists = new ConcurrentDictionary<string, RetiredTermList>(StringComparer.Ordinal);
            start.RegisterSyntaxTreeAction(treeContext => AnalyzeTree(treeContext, lists));
        });
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context, ConcurrentDictionary<string, RetiredTermList> lists)
    {
        if (!context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree).TryGetValue(ConfigurationKey, out var value))
            return;

        var list = lists.GetOrAdd(value, RetiredTermList.Parse);
        if (list.Error is { } error)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidConfigurationRule, Location.Create(context.Tree, new TextSpan(0, 0)), error));
            return;
        }

        if (list.Terms.IsEmpty)
            return;

        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var node in root.DescendantNodes(descend => descend is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case BaseTypeDeclarationSyntax type:
                    ReportType(context, list, type.Identifier);
                    break;
                case DelegateDeclarationSyntax @delegate:
                    ReportType(context, list, @delegate.Identifier);
                    break;
                case BaseNamespaceDeclarationSyntax @namespace:
                    ReportNamespace(context, list, @namespace.Name);
                    break;
            }
        }
    }

    private static void ReportType(SyntaxTreeAnalysisContext context, RetiredTermList list, SyntaxToken identifier)
    {
        foreach (var (spelling, replacement) in list.Find(identifier.ValueText))
            context.ReportDiagnostic(Diagnostic.Create(TypeNameRule, identifier.GetLocation(), identifier.ValueText, spelling, replacement));
    }

    private static void ReportNamespace(SyntaxTreeAnalysisContext context, RetiredTermList list, NameSyntax name)
    {
        var text = name.ToString();
        var segments = name.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(segment => segment.Identifier.ValueText);
        foreach (var (spelling, replacement) in segments.SelectMany(list.Find).Distinct())
            context.ReportDiagnostic(Diagnostic.Create(NamespaceRule, name.GetLocation(), text, spelling, replacement));
    }
}
