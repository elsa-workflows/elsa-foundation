using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 247: Elsa 3 host functions that Elsa 4 runs the same convert; the rest are refused with guidance naming the
/// Elsa 3 rewrite. Converted sources are run in the Elsa 4 evaluator.
/// </summary>
public sealed class Elsa3JavaScriptHostFunctionTests
{
    [Theory]
    [InlineData("getCorrelationId()", "workflowCorrelationId", WorkflowInstanceExpressionParameterBinding.Members.CorrelationId)]
    [InlineData("getWorkflowInstanceId()", "workflowInstanceId", WorkflowInstanceExpressionParameterBinding.Members.InstanceId)]
    public async Task Instance_metadata_reads_become_a_workflow_instance_parameter(string expression, string parameter, string member)
    {
        var result = await ConvertTextAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var converted = ReadExpression(result);
        Assert.Equal($"args.{parameter}", converted.Source);
        Assert.Equal(member, Assert.IsType<WorkflowInstanceExpressionParameterBinding>(converted.Parameters[parameter]).Member);
    }

    [Fact]
    public async Task A_converted_correlation_id_read_returns_the_instance_value()
    {
        var result = await ConvertTextAsync("return { correlationId: getCorrelationId(), again: getCorrelationId() }");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var value = await PortableJavaScript.EvaluateAsync(ReadExpression(result), new Dictionary<string, JsonElement>
        {
            ["workflowCorrelationId"] = JsonSerializer.SerializeToElement("c-1"),
        });
        Assert.Equal("c-1", value.GetProperty("correlationId").GetString());
        Assert.Equal("c-1", value.GetProperty("again").GetString());
    }

    [Theory]
    [InlineData("CorrelationId")]
    [InlineData("WorkflowInstanceId")]
    public async Task A_variable_whose_accessor_has_a_built_in_name_is_ambiguous(string variable)
    {
        var result = await ConvertTextAsync($"get{variable}()", new Elsa3Variable { Id = "v", Name = variable, TypeName = "String" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    /// <summary>
    /// A variable named like the parameter a built-in becomes no longer collides with it: the variable is read through the
    /// variables root, the built-in through args (spec 1222 FR-003, FR-026).
    /// </summary>
    [Fact]
    public async Task A_variable_named_like_a_built_in_parameter_is_read_through_its_own_root()
    {
        var result = await ConvertTextAsync("getVariable('workflowCorrelationId') + getCorrelationId()", new Elsa3Variable { Id = "v", Name = "workflowCorrelationId", TypeName = "String" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var expression = ReadExpression(result);
        Assert.Equal("variables.workflowCorrelationId + args.workflowCorrelationId", expression.Source);
        Assert.IsType<VariableExpressionParameterBinding>(expression.Parameters["variables.workflowCorrelationId"]);
        Assert.IsType<WorkflowInstanceExpressionParameterBinding>(expression.Parameters["workflowCorrelationId"]);
    }

    /// <summary>A variable whose generated accessor has a built-in's name leaves the call ambiguous, so it is refused.</summary>
    [Fact]
    public async Task A_variable_whose_accessor_is_a_built_in_is_refused()
    {
        var result = await ConvertTextAsync("getVariable('workflowInstanceId') + getWorkflowInstanceId()", new Elsa3Variable { Id = "v", Name = "workflowInstanceId", TypeName = "String" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    [Fact]
    public async Task A_built_in_called_with_arguments_is_refused()
    {
        var result = await ConvertTextAsync("getCorrelationId('x')");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task A_function_the_script_declares_with_a_built_in_name_is_a_local_call()
    {
        var result = await ConvertTextAsync("(() => { function getCorrelationId() { return 'local'; } return getCorrelationId(); })()");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Empty(ReadExpression(result).Parameters);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a")]
    [InlineData(" a ")]
    [InlineData("\t\r\n")]
    [InlineData("\u0085")]
    [InlineData("  　")]
    [InlineData("﻿")]
    [InlineData("\u001C")]
    public async Task String_helpers_return_what_dotnet_returned(string? value)
    {
        var result = await ConvertTextAsync(
            "return { empty: isNullOrEmpty(getVariable('s')), blank: isNullOrWhiteSpace(getVariable('s')) }",
            new Elsa3Variable { Id = "s", Name = "s", TypeName = "String" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var evaluated = await PortableJavaScript.EvaluateAsync(ReadExpression(result), new Dictionary<string, JsonElement>
        {
            ["s"] = JsonSerializer.SerializeToElement(value),
        });
        Assert.Equal(string.IsNullOrEmpty(value), evaluated.GetProperty("empty").GetBoolean());
        Assert.Equal(string.IsNullOrWhiteSpace(value), evaluated.GetProperty("blank").GetBoolean());
    }

    [Fact]
    public async Task A_string_helper_in_a_single_expression_is_wrapped_with_its_prelude()
    {
        var result = await ConvertTextAsync("isNullOrEmpty('') && isNullOrEmpty('')");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var source = ReadExpression(result).Source;
        Assert.StartsWith("(function () {\nfunction __isNullOrEmpty(v)", source, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, "function __isNullOrEmpty"));
        Assert.True((await PortableJavaScript.EvaluateAsync(ReadExpression(result))).GetBoolean());
    }

    [Fact]
    public async Task A_string_helper_evaluates_its_argument_once()
    {
        var result = await ConvertTextAsync("var calls = []; isNullOrWhiteSpace((calls.push(1), ' ')); calls.length");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(1, (await PortableJavaScript.EvaluateAsync(ReadExpression(result))).GetInt32());
    }

    [Fact]
    public async Task A_string_helper_in_a_script_gets_its_prelude()
    {
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal("isNullOrEmpty('')"))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(
            "function __isNullOrEmpty(v) { return v == null || v === \"\"; }\nreturn __isNullOrEmpty('')",
            Text(Input(Node(result.Value!.State, "node"), "script")));
    }

    [Theory]
    [InlineData("isNullOrEmpty()")]
    [InlineData("isNullOrEmpty('a', 'b')")]
    public async Task A_string_helper_with_other_than_one_argument_is_refused(string expression)
    {
        var result = await ConvertTextAsync(expression);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task A_script_declaring_the_prelude_name_is_ambiguous()
    {
        var result = await ConvertTextAsync("var __isNullOrEmpty = 1; isNullOrEmpty('')");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    [Theory]
    [InlineData("DateTime.Now", "DateTime", "Date or Temporal")]
    [InlineData("Guid.NewGuid()", "Guid", "newGuid()")]
    [InlineData("crypto.randomUUID()", "crypto.randomUUID", "newGuid()")]
    [InlineData("bytesToString(x)", "bytesToString", "activity")]
    [InlineData("streamToBytes(x)", "streamToBytes", "activity")]
    [InlineData("getConfig('Key')", "getConfig", "workflow input")]
    [InlineData("getWorkflowDefinitionId()", "getWorkflowDefinitionId", "correlation ID and the instance ID")]
    [InlineData("setCorrelationId('x')", "setCorrelationId", "Correlate activity")]
    [InlineData("setWorkflowInstanceName('x')", "setWorkflowInstanceName", "Set Name activity")]
    [InlineData("getLastResult()", "getLastResult", "variable")]
    public async Task A_refused_construct_names_itself_and_the_elsa3_rewrite(string expression, string named, string guidance)
    {
        var result = await ConvertTextAsync(expression);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains($"'{named}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(guidance, diagnostic.Guidance, StringComparison.Ordinal);
    }

    // Spec 1208 FR-027: a RunJavaScript script's setOutcome / setOutcomes converts to the returned completion form.
    [Theory]
    [InlineData("setOutcome('Approved')")]
    [InlineData("setOutcomes(['Approved', 'Logged'])")]
    public async Task A_script_setting_a_literal_outcome_converts_to_the_returned_completion_form(string script)
    {
        var root = Sequence("root",
            Activity("before", "Elsa.WriteLine", ("text", Literal("a"))),
            Activity("node", "Elsa.RunJavaScript", ("script", Literal(script))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        var converted = Text(Input(Node(result.Value!.State, "node"), "script"));
        Assert.Contains("complete(", converted, StringComparison.Ordinal);
        Assert.DoesNotContain("setOutcome(", converted, StringComparison.Ordinal);
        Assert.DoesNotContain("setOutcomes(", converted, StringComparison.Ordinal);
    }

    // Review round 1: a returned comma expression stays one argument of complete(...), whose value is its last
    // operand as Elsa 3 returned it, not two arguments that would shift the selection.
    [Theory]
    [InlineData("setOutcome('Approved'); return 1, 2;", "complete((1, 2), ")]
    [InlineData("setOutcome('Approved'); 1, 2", "complete((1, 2), ")]
    public async Task A_returned_comma_expression_stays_one_completion_result(string script, string expected)
    {
        var root = Sequence("root",
            Activity("before", "Elsa.WriteLine", ("text", Literal("a"))),
            Activity("node", "Elsa.RunJavaScript", ("script", Literal(script))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(expected, Text(Input(Node(result.Value!.State, "node"), "script")), StringComparison.Ordinal);
    }

    // Review round 1: Elsa 4 reserves 'complete' in a script activity whether or not the script sets an outcome, so a
    // script that declares it is refused at import (with the rename) rather than importing and failing draft validation.
    [Theory]
    [InlineData("function complete(x) { return x; } return complete(1);")]
    [InlineData("var complete = 1; return complete;")]
    [InlineData("function complete(x) { return x; } setOutcome('Approved'); return complete(1);")]
    public async Task A_script_declaring_complete_is_refused_with_rename_guidance(string script)
    {
        var root = Sequence("root",
            Activity("before", "Elsa.WriteLine", ("text", Literal("a"))),
            Activity("node", "Elsa.RunJavaScript", ("script", Literal(script))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains("'complete'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Rename", diagnostic.Guidance, StringComparison.Ordinal);
    }

    // Review round 2: 'complete' is reserved in every JavaScript surface, so an input expression that declares it is
    // refused at import too, rather than importing and failing draft validation (JavaScript/ReservedName).
    [Theory]
    [InlineData("(function () { var complete = 'x'; return complete; })()")]
    [InlineData("((complete) => complete)('x')")]
    public async Task An_expression_declaring_complete_is_refused_with_rename_guidance(string expression)
    {
        var root = Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains("'complete'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Rename", diagnostic.Guidance, StringComparison.Ordinal);
    }

    /// <summary>
    /// Spec 1208 FR-027: a computed name declares the catch-all on the possible outcomes whichever order the members
    /// have, and the possible outcomes are published as a JSON list, not as text Elsa 4 would split at its commas.
    /// </summary>
    [Fact]
    public async Task A_computed_outcome_declares_the_catch_all_on_the_possible_outcomes_list()
    {
        var root = Sequence("root",
            Activity("node", "Elsa.RunJavaScript",
                ("script", Literal("setOutcome(variables.decision)")),
                ("possibleOutcomes", Literal("[\"Approved\",\"Rejected\"]", "List<String>"))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root,
            variables: [new Elsa3Variable { Id = "decision", Name = "decision", TypeName = "String" }]));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var possibleOutcomes = Input(Node(result.Value!.State, "node"), "possibleOutcomes");
        var list = Assert.IsType<JsonElement>(possibleOutcomes.Value.Value);
        Assert.Equal(["Approved", "Rejected", "Unlisted outcome"], list.EnumerateArray().Select(item => item.GetString()));
    }

    // Spec 1208 FR-027, Q7: a computed name is refused when the activity lists no possible outcomes.
    [Theory]
    [InlineData("var name = 'Appro' + 'ved'; setOutcome(name)")]
    [InlineData("var names = ['Approved']; setOutcomes(names)")]
    public async Task A_script_setting_a_computed_outcome_without_possible_outcomes_refuses_the_whole_definition(string script)
    {
        var root = Sequence("root",
            Activity("before", "Elsa.WriteLine", ("text", Literal("a"))),
            Activity("node", "Elsa.RunJavaScript", ("script", Literal(script))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains("computed name", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Possible Outcomes", diagnostic.Guidance, StringComparison.Ordinal);
    }

    // Spec 1208 FR-027: outside a RunJavaScript script (an input expression) setOutcome stays refused.
    [Theory]
    [InlineData("setOutcome('Approved')", "setOutcome")]
    [InlineData("setOutcomes(['Approved', 'Logged'])", "setOutcomes")]
    public async Task An_input_expression_setting_an_outcome_is_refused(string expression, string named)
    {
        var result = await ConvertTextAsync(expression);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains($"'{named}'", diagnostic.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An undeclared function keeps VF-IMP-005, with guidance naming the declaration route (#5664), whether the call
    /// reaches the generic refusal (it has arguments) or the generated-accessor lookup (a get… call without arguments).
    /// </summary>
    [Theory]
    [InlineData("getConfiguration('Key')", "getConfiguration")]
    [InlineData("getCustomer()", "getCustomer")]
    public async Task An_undeclared_host_function_keeps_the_refusal_with_the_declaration_route(string expression, string name)
    {
        var result = await ConvertTextAsync(expression);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains($"unknown or unapproved host function '{name}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains($"'acme.{name}'", diagnostic.Guidance, StringComparison.Ordinal);
        Assert.Contains("ReplacesGlobalFunctions", diagnostic.Guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("deterministic", diagnostic.Guidance, StringComparison.OrdinalIgnoreCase);
    }

    // ---- #5664: an Elsa 3 custom function rebuilt as a prefixed Elsa 4 host function ----

    private static readonly JavaScriptHostFunctionDeclaration LookupCustomer =
        new("acme.lookupCustomer", _ => JsonSerializer.SerializeToElement(new { name = "Ada" }))
        {
            ReplacesGlobalFunctions = ["lookupCustomer"],
        };

    [Fact]
    public async Task A_declared_global_is_rewritten_to_its_prefixed_host_function_and_runs()
    {
        var result = await ConvertTextAsync("lookupCustomer('c-1').name", [LookupCustomer]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var converted = ReadExpression(result);
        Assert.Equal("acme.lookupCustomer('c-1').name", converted.Source);
        var value = await PortableJavaScript.EvaluateAsync(converted, configure: services => services.AddSingleton(LookupCustomer));
        Assert.Equal("Ada", value.GetString());
    }

    [Fact]
    public async Task A_declared_global_call_converts_its_arguments()
    {
        var lookup = new JavaScriptHostFunctionDeclaration("acme.lookup", call => call.Arguments[0]) { ReplacesGlobalFunctions = ["lookup"] };

        var result = await ConvertTextAsync("lookup(getVariable('Key'))", [lookup], new Elsa3Variable { Id = "v", Name = "Key", TypeName = "String" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("acme.lookup(variables.Key)", ReadExpression(result).Source);
    }

    /// <summary>
    /// A host function receives its arguments as JSON, so passing a variable to one cannot change it: a reusable workflow's
    /// script that does converts, as with a built-in.
    /// </summary>
    [Fact]
    public async Task A_reusable_script_passing_a_variable_to_a_declared_global_converts()
    {
        var lookup = new JavaScriptHostFunctionDeclaration("acme.lookup", call => call.Arguments[0]) { ReplacesGlobalFunctions = ["lookup"] };
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal("return lookup(getVariable('Order'));"))));

        var result = await ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            ConversionServices.Definition(root, variables: [new Elsa3Variable { Id = "order-id", Name = "Order", TypeName = "Object" }]),
            isReusable: true,
            configure: services => services.AddSingleton(lookup));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    /// <summary>
    /// A claimed converted name stays native, so a reusable script's write through it is still found: String runs the
    /// argument's toString, which changes the variable.
    /// </summary>
    [Fact]
    public async Task A_reusable_script_writing_through_a_claimed_converted_name_is_refused()
    {
        var claim = new JavaScriptHostFunctionDeclaration("acme.String", _ => default) { ReplacesGlobalFunctions = ["String"] };
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script",
            Literal("const o = getVariable('Order'); String({ toString() { o.Status = 'Paid'; return ''; } }); return 1;"))));

        var result = await ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            ConversionServices.Definition(root, variables: [new Elsa3Variable { Id = "order-id", Name = "Order", TypeName = "Object" }]),
            isReusable: true,
            configure: services => services.AddSingleton(claim));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    /// <summary>A declaration does not take over a name the converter converts itself.</summary>
    [Fact]
    public async Task A_declaration_claiming_a_converted_name_keeps_the_conversion()
    {
        var claim = new JavaScriptHostFunctionDeclaration("acme.toJson", _ => default) { ReplacesGlobalFunctions = ["toJson", "newGuid"] };

        var result = await ConvertTextAsync("toJson({ a: 1 }) + newGuid()", [claim]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("JSON.stringify({ a: 1 }) + newGuid()", ReadExpression(result).Source);
    }

    [Fact]
    public async Task A_global_two_host_functions_replace_is_ambiguous()
    {
        var other = new JavaScriptHostFunctionDeclaration("other.lookupCustomer", _ => default) { ReplacesGlobalFunctions = ["lookupCustomer"] };

        var result = await ConvertTextAsync("lookupCustomer()", [LookupCustomer, other]);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
        Assert.Contains("'acme.lookupCustomer', 'other.lookupCustomer'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_global_that_is_also_a_generated_accessor_is_ambiguous()
    {
        var getCustomer = new JavaScriptHostFunctionDeclaration("acme.getCustomer", _ => default) { ReplacesGlobalFunctions = ["getCustomer"] };

        var result = await ConvertTextAsync("getCustomer()", [getCustomer], new Elsa3Variable { Id = "v", Name = "Customer", TypeName = "String" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    /// <summary>Two variables whose generated accessor is the same name: Elsa 3 generated it, so the clash stands.</summary>
    [Fact]
    public async Task A_declared_global_that_is_also_an_ambiguous_generated_accessor_is_ambiguous()
    {
        var getCustomer = new JavaScriptHostFunctionDeclaration("acme.getCustomer", _ => default) { ReplacesGlobalFunctions = ["getCustomer"] };

        var result = await ConvertTextAsync("getCustomer()", [getCustomer],
            new Elsa3Variable { Id = "v1", Name = "customer", TypeName = "String" },
            new Elsa3Variable { Id = "v2", Name = "Customer", TypeName = "String" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference &&
            diagnostic.Message.Contains("replaced Elsa 3 function", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_script_declaring_the_namespace_root_is_refused()
    {
        var result = await ConvertTextAsync("const acme = 1; return lookupCustomer();", [LookupCustomer]);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
        Assert.Contains("'acme'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_function_the_script_declares_itself_is_not_rewritten()
    {
        var result = await ConvertTextAsync("function lookupCustomer() { return 1; } return lookupCustomer();", [LookupCustomer]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain("acme", ReadExpression(result).Source, StringComparison.Ordinal);
    }

    // ---- #5691: a replaced global used other than in a direct call ----

    /// <summary>
    /// Only a direct call is rewritten, so an alias, a callback, a stored function, a shorthand property, a typeof test or
    /// a construction is refused once per name, with guidance to call the function directly.
    /// </summary>
    [Theory]
    [InlineData("(() => { const find = lookupCustomer; return find('c-1'); })()")]
    [InlineData("['c-1'].map(lookupCustomer)")]
    [InlineData("(() => { const helpers = {}; helpers.find = lookupCustomer; return helpers.find('c-1'); })()")]
    [InlineData("({ lookupCustomer }).lookupCustomer('c-1')")]
    [InlineData("typeof lookupCustomer")]
    [InlineData("new lookupCustomer('c-1')")]
    [InlineData("lookupCustomer('c-1') && [lookupCustomer, lookupCustomer]")]
    public async Task A_declared_global_used_other_than_in_a_direct_call_is_refused(string expression)
    {
        var result = await ConvertTextAsync(expression, [LookupCustomer]);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ImportDiagnosticCodes.ReplacedFunctionReference, diagnostic.Code);
        Assert.Contains("'lookupCustomer' as a value", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'acme.lookupCustomer'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("lookupCustomer(item)", diagnostic.Guidance, StringComparison.Ordinal);
    }

    /// <summary>A property name, a member name or a script's own binding is not a reference to the replaced global.</summary>
    [Theory]
    [InlineData("({ lookupCustomer: 1 }).lookupCustomer")]
    [InlineData("(() => { const lookupCustomer = id => id; return ['c-1'].map(lookupCustomer); })()")]
    [InlineData("['c-1'].map(id => lookupCustomer(id).name)")]
    public async Task A_name_that_is_not_a_reference_to_the_declared_global_converts(string expression)
    {
        var result = await ConvertTextAsync(expression, [LookupCustomer]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    /// <summary>Without a declaration the name is not a replaced global, so no reference diagnostic is reported.</summary>
    [Fact]
    public async Task An_undeclared_global_used_as_a_value_gets_no_replaced_reference_diagnostic()
    {
        var result = await ConvertTextAsync("['c-1'].map(lookupCustomer)");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ReplacedFunctionReference);
    }

    /// <summary>A write to the name is an undeclared write, not a reference.</summary>
    [Fact]
    public async Task A_write_to_a_declared_global_is_an_undeclared_write()
    {
        var result = await ConvertTextAsync("(() => { lookupCustomer = null; return 1; })()", [LookupCustomer]);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ImportDiagnosticCodes.UnsupportedShape, diagnostic.Code);
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertTextAsync(string expression, params Elsa3Variable[] variables) =>
        ConvertTextAsync(expression, [], variables);

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertTextAsync(
        string expression,
        IReadOnlyList<JavaScriptHostFunctionDeclaration> hostFunctions,
        params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))), variables: variables),
            configure: services =>
            {
                foreach (var hostFunction in hostFunctions)
                    services.AddSingleton(hostFunction);
            });

    private static ExpressionDefinition ReadExpression(Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition> result)
    {
        var argument = Input(Node(result.Value!.State, "node"), "text");
        return (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
            .Deserialize<ExpressionDefinition>()!;
    }
}
