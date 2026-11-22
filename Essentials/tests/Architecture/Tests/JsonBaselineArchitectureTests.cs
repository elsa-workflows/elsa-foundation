using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonBaselineArchitectureTests
{
    // Written before the scanner: each independent bypass must be rejected without relying on the repository's current tree.
    [Theory]
    [InlineData("class C { object x = new JsonSerializerOptions(); }", "construction")]
    [InlineData("class C { JsonSerializerOptions x = new(); }", "construction")]
    [InlineData("class C { object x = JsonSerializerOptions.Web; }", "defaults")]
    [InlineData("class C { object x = JsonSerializerOptions.Default; }", "defaults")]
    [InlineData("[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)] class C {}", "PropertyNameCaseInsensitive")]
    [InlineData("class C { void M() { options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase; } }", "DictionaryKeyPolicy")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeToElement(value, value.GetType()); } }", "optionless")]
    [InlineData("class C { void M() { payload.Deserialize<T>(); } }", "optionless")]
    public void Every_unregistered_deviation_is_rejected(string source, string setting) =>
        Assert.Contains(JsonBaselineSourceScan.Scan(source), x => x.Setting == setting);

    [Theory]
    [InlineData("new JsonSerializerOptions()", "construction")]
    [InlineData("JsonSerializerOptions.Web", "defaults")]
    public void A_reasoned_register_entry_accounts_for_its_specific_setting(string expression, string setting)
    {
        var violations = JsonBaselineSourceScan.Scan($"class C {{ object x = {expression}; }}");
        Assert.NotEmpty(violations);
        Assert.Empty(violations.Where(x => x.Setting != setting));
    }

    [Fact]
    public void Legacy_accounting_never_admits_a_workflow_data_conversion()
    {
        var call = Assert.Single(JsonBaselineSourceScan.Scan("class C { void M() { JsonSerializer.Serialize(value); } }"));
        Assert.False(JsonBaselineSourceScan.IsAccounted(call, [call.Signature], [], workflowData: true));
        Assert.True(JsonBaselineSourceScan.IsAccounted(call, [call.Signature], [], workflowData: false));
        Assert.False(JsonBaselineSourceScan.IsAccounted(call, [], [], workflowData: false));
        Assert.Empty(Array.Empty<JsonBaselineSourceScan.Finding>().Where(x => !JsonBaselineSourceScan.IsAccounted(x, [], [], false)));
    }

    [Fact]
    public void Baseline_copies_explicit_type_info_and_protected_defaults_are_accepted()
    {
        const string source = "class C { void M() { var options = JsonBaseline.CreateCopy(); options.PropertyNameCaseInsensitive = false; options.NumberHandling = JsonNumberHandling.Strict; JsonSerializer.Serialize(value, options); JsonSerializer.Serialize(value, Context.WireContext.Value); } }";
        Assert.Empty(JsonBaselineSourceScan.Scan(source));
    }
}

internal static class JsonBaselineSourceScan
{
    internal sealed record Finding(string Setting, string Signature);

    internal static bool IsAccounted(Finding finding, IReadOnlyCollection<string> legacy, IReadOnlyCollection<string> settings, bool workflowData) =>
        finding.Setting == "optionless" ? !workflowData && legacy.Contains(finding.Signature) : settings.Contains(finding.Setting);

    internal static IReadOnlyList<Finding> Scan(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var findings = new List<Finding>();
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            if (creation.Type.ToString().EndsWith("JsonSerializerOptions", StringComparison.Ordinal)) Add("construction", creation);
        foreach (var creation in root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
            if (creation.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault()?.Type.ToString().EndsWith("JsonSerializerOptions", StringComparison.Ordinal) == true ||
                creation.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Type.ToString().EndsWith("JsonSerializerOptions", StringComparison.Ordinal) == true)
                Add("construction", creation);
        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            if (access.Expression.ToString().EndsWith("JsonSerializerOptions", StringComparison.Ordinal) && access.Name.Identifier.ValueText is "Web" or "Default") Add("defaults", access);
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var name = assignment.Left is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : assignment.Left.ToString();
            if (IsDeviation(name, assignment.Right.ToString())) Add(name, assignment);
        }
        foreach (var argument in root.DescendantNodes().OfType<AttributeArgumentSyntax>().Where(x => x.NameEquals is not null))
            if (argument.Ancestors().OfType<AttributeSyntax>().First().Name.ToString().Contains("JsonSourceGenerationOptions", StringComparison.Ordinal) && IsDeviation(argument.NameEquals!.Name.Identifier.ValueText, argument.Expression.ToString())) Add(argument.NameEquals.Name.Identifier.ValueText, argument);
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax member) continue;
            var name = member.Name.Identifier.ValueText;
            var serializer = member.Expression.ToString().EndsWith("JsonSerializer", StringComparison.Ordinal);
            var arguments = call.ArgumentList.Arguments;
            if (serializer && name is "Serialize" or "SerializeToElement" or "SerializeToUtf8Bytes" or "Deserialize" &&
                (arguments.Count == 1 || arguments.Count == 2 && (arguments[1].Expression is TypeOfExpressionSyntax || arguments[1].Expression.ToString().EndsWith(".GetType()", StringComparison.Ordinal)))) Add("optionless", call);
            else if (!serializer && name == "Deserialize" && arguments.Count == 0) Add("optionless", call);
        }
        return findings;

        void Add(string setting, Microsoft.CodeAnalysis.SyntaxNode node) => findings.Add(new(setting, node.WithoutTrivia().ToString().Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)));
    }

    private static bool IsDeviation(string name, string value) => name switch
    {
        "DictionaryKeyPolicy" => value != "null",
        "PropertyNameCaseInsensitive" => value != "false",
        "PropertyNamingPolicy" => value is not ("JsonNamingPolicy.CamelCase" or "JsonKnownNamingPolicy.CamelCase"),
        "NumberHandling" => value != "JsonNumberHandling.Strict",
        "DefaultIgnoreCondition" => value != "JsonIgnoreCondition.Never",
        "Encoder" => !value.EndsWith("JavaScriptEncoder.UnsafeRelaxedJsonEscaping", StringComparison.Ordinal),
        _ => false
    };
}
