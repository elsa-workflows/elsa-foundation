using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Services;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3ExpressionRewriterTests
{
    private readonly Elsa3ExpressionRewriter _sut = new(Support.ConversionServices.DotNetUsageAnalyzer());

    [Fact]
    public void JavaScript_rewrites_only_parser_proven_reads_and_preserves_literals_comments_and_property_names()
    {
        const string source = "\"getVariable('total')\" + getVariable(\"total\") + Math.variables.total /* getVariable('total') */";

        var result = _sut.Rewrite("JavaScript", source, new TypeReference("String"), Resolve);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "\"getVariable('total')\" + args.total + Math.variables.total /* getVariable('total') */",
            result.Definition!.Source);
        Assert.IsType<VariableExpressionParameterBinding>(result.Definition.Parameters["total"]);
    }

    [Fact]
    public void Liquid_rewrites_syntax_tree_member_but_not_comment_text()
    {
        const string source = "{% comment %}{{ Variables.customer.name }}{% endcomment %}{{ Variables.customer.name }}";

        var result = _sut.Rewrite("Liquid", source, new TypeReference("String"), Resolve);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "{% comment %}{{ Variables.customer.name }}{% endcomment %}{{ customer.name }}",
            result.Definition!.Source);
        Assert.IsType<VariableExpressionParameterBinding>(result.Definition.Parameters["customer"]);
    }

    [Theory]
    [InlineData("setVariable(\"total\", 42)")]
    [InlineData("variables.total = 42")]
    public void JavaScript_rejects_mutation(string source)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("String"), Resolve);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    /// <summary>
    /// The lowerer's write pre-pass agrees with the conversion (#5664): a host function receives its arguments as JSON,
    /// so passing a variable to a replaced global is no write, where passing it to an undeclared function may be.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Passing_a_variable_to_a_replaced_global_is_no_write(bool registered, bool mayWrite)
    {
        var lookup = new JavaScriptHostFunctionDeclaration("acme.lookupCustomer", _ => default);
        var rewriter = new Elsa3ExpressionRewriter(Support.ConversionServices.DotNetUsageAnalyzer(), hostFunctions: registered ? [lookup] : []);

        var result = rewriter.ScriptMayWriteVariables("lookupCustomer(getVariable('customer'))", reference => reference.Kind == Elsa3AmbientReferenceKind.Variable
            ? Elsa3ExpressionParameterResolution.SuccessVariable(new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, "variable-customer"), reference.Name)
            : Elsa3ExpressionParameterResolution.Failure(new Elsa3ExpressionRewriteIssue("x", "x", "x")));

        Assert.Equal(mayWrite, result);
    }

    /// <summary>
    /// #5894: an input expression that changes a value it read from a variable, through a local name or a member, is
    /// refused: Elsa 4 input expressions only read (spec 1228 FR-006a), so it would fault with VF-BIND-001. The first four
    /// are the JTest corpus patterns (array-methods, object-methods, logging-workflow, composition-execute-workflow-root).
    /// </summary>
    [Theory]
    [InlineData("const array = getArrayVariable();\r\nconst itemToAdd = getHttpRequestBody().unshift.itemToAdd\r\nconst newLength = array.unshift(itemToAdd);\r\n\r\nreturn {\r\n    unshiftedArray: array,\r\n    newLength: newLength,\r\n    arrayVariable: getArrayVariable(), correlationId: getCorrelationId()\r\n}\r\n")]
    [InlineData("var obj = getObjectVariable();\r\nconst propertyName = getHttpRequestBody().deleteProperty.name;\r\ndelete obj[propertyName];\r\n\r\nreturn {\r\n    result: obj,\r\n    objectVariable: getObjectVariable(), correlationId: getCorrelationId()\r\n}")]
    [InlineData("let responseBody = getHttpResponseBody();\r\nresponseBody.queryArguments = getQueryArguments();\r\nresponseBody.correlationId = getCorrelationId();\r\nreturn responseBody;")]
    [InlineData("var result = getHttpRequestBody().subworkflowInput;\r\nresult.MillisecondsToDelaySubworkflowExecutionTime = getHttpRequestBody().millisecondsToDelaySubworkflowExecutionTime;\r\nreturn result;")]
    [InlineData("getOrder().lines.push(1)")]
    [InlineData("delete getOrder().status")]
    [InlineData("Object.assign(getOrder(), { status: 'Paid' })")]
    [InlineData("var lines = getOrder().lines; lines.sort(); return lines;")]
    [InlineData("var order = getVariable('Order'); if (order.total > 10) { order.discount++; } return order;")]
    [InlineData("var copy = getOrder().lines.slice(); copy[0].quantity = 2; return copy;")]
    public void Input_expression_that_changes_a_value_it_read_is_refused(string source)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables);

        Assert.False(result.Succeeded);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(Elsa3ImportDiagnosticCodes.InputChangesVariable, issue.Code);
        Assert.Contains("VF-BIND-001", issue.Message, StringComparison.Ordinal);
        Assert.Contains("move it into a Run JavaScript step before this input", issue.Guidance, StringComparison.Ordinal);
        Assert.Contains("change a local copy", issue.Guidance, StringComparison.Ordinal);
    }

    /// <summary>
    /// #5894: a change to a local copy, a read alone, a new local object and an opaque call on a read value are no change
    /// of the variable, so they convert. A Run JavaScript body writes variables, so the same change converts there.
    /// </summary>
    [Theory]
    [InlineData("const items = getArrayVariable().slice(); items.push(1); return items;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = Object.assign({}, getOrder()); order.status = 'Paid'; return order;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = JSON.parse(JSON.stringify(getOrder())); order.lines[0].quantity = 2; return order;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var total = getOrder().total; total++; return total;", Elsa3JavaScriptMode.Expression)]
    [InlineData("const name = getHttpRequestBody().name; var result = {}; result[name] = getHttpRequestBody().value; return result;", Elsa3JavaScriptMode.Expression)]
    [InlineData("getArrayVariable().map(item => item * 2)", Elsa3JavaScriptMode.Expression)]
    [InlineData("getOrder().total.toFixed(2)", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = getOrder(); getHttpRequestBody().hooks.register(function () { order.touched = true; }); return order.status;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = getOrder(); return { status: order.status, lines: order.lines.length };", Elsa3JavaScriptMode.Expression)]
    [InlineData("var order = getOrder(); order.status = 'Paid'; return order;", Elsa3JavaScriptMode.Script)]
    public void Input_expression_that_changes_no_value_it_read_converts(string source, Elsa3JavaScriptMode mode)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables, mode);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == Elsa3ImportDiagnosticCodes.InputChangesVariable);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    /// <summary>
    /// #6222: a change to a local object whose computed key reads a variable changes only the local object; the key is a
    /// read. So it converts, in an input and in a Run JavaScript body alike, through a member chain, an update or a
    /// destructuring pattern.
    /// </summary>
    [Theory]
    [InlineData("var result = {}; result[getHttpRequestBody().name] = 1; return result;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var result = {}; result[getHttpRequestBody().name] = getHttpRequestBody().value; return result;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var result = { lines: {} }; result.lines[getOrder().id] = 1; return result;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var result = { lines: [{}] }; result.lines[getOrder().index].quantity = 2; return result;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var counts = {}; counts[getVariable('Order').status] = 0; counts[getVariable('Order').status]++; return counts;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var result = {}; result[getOrder()[getHttpRequestBody().name]] = 1; return result;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var target = []; [target[getOrder().index]] = [1]; return target;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var key; ({ [getOrder().field]: key } = { status: 'Paid' }); return key;", Elsa3JavaScriptMode.Expression)]
    [InlineData("var result = {}; result[getHttpRequestBody().name] = 1; return result;", Elsa3JavaScriptMode.Script)]
    public void Change_of_a_local_object_whose_computed_key_reads_a_variable_converts(string source, Elsa3JavaScriptMode mode)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables, mode);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// #6222: only the object chain decides, so a change that reaches a variable through it is still refused as a change
    /// of an ambient value (VF-IMP-005), whatever the keys read: the root itself, a nested member chain, a destructuring
    /// target, an object the converter cannot follow, and a change made inside a computed key.
    /// </summary>
    [Theory]
    [InlineData("getOrder().status = 'Paid'")]
    [InlineData("getOrder()[getHttpRequestBody().name] = 1")]
    [InlineData("getOrder().lines[getHttpRequestBody().index].quantity = 2")]
    [InlineData("getVariable('Order')[getHttpRequestBody().name]++")]
    [InlineData("variables.Order[getHttpRequestBody().name] = 1")]
    [InlineData("({ status: getOrder()[getHttpRequestBody().name] } = { status: 'Paid' })")]
    [InlineData("[getOrder().lines[getHttpRequestBody().index]] = [1]")]
    [InlineData("(getHttpRequestBody().useOrder ? getOrder() : {}).status = 'Paid'")]
    [InlineData("var result = {}; result[getOrder().count++] = 1; return result;")]
    [InlineData("var result = {}; result[getOrder().status = 'Paid'] = 1; return result;")]
    public void Change_through_an_ambient_object_chain_stays_refused(string source)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability &&
                                                issue.Message == "JavaScript mutates an Elsa 3 ambient value.");
    }

    /// <summary>
    /// #6222: a local name or member that holds a value read from a variable is no local object, so a change through it
    /// is still refused as an input changing a variable (#5894), with or without a computed key that reads a variable.
    /// </summary>
    [Theory]
    [InlineData("var order = getOrder(); order[getHttpRequestBody().name] = 1; return order;")]
    [InlineData("var order = getOrder(); order.lines[getHttpRequestBody().index].quantity = 2; return order;")]
    [InlineData("var lines = getOrder().lines; lines[getHttpRequestBody().index]++; return lines;")]
    [InlineData("var holder = { order: getOrder() }; holder.order[getHttpRequestBody().name] = 1; return holder;")]
    [InlineData("var result = {}; result[getOrder().lines.push(1)] = 1; return result;")]
    public void Change_through_an_alias_of_a_read_variable_stays_refused(string source)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables);

        Assert.False(result.Succeeded);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(Elsa3ImportDiagnosticCodes.InputChangesVariable, issue.Code);
    }

    /// <summary>
    /// #6230: a workflow instance value read with an Elsa 3 built-in is read-only in Elsa 4, so a change through it is
    /// refused (VF-IMP-005) wherever the script runs: on the value itself or a member chain below it, by assignment,
    /// update or compound assignment, through a destructuring target, and through an object that can hand the value back
    /// (either branch of a conditional, either side of a logical expression, a sequence's last expression, an awaited
    /// value, the value of an = or ??= assignment). A computed key on the way only reads.
    /// </summary>
    [Theory]
    [InlineData("getCorrelationId().x++", Elsa3JavaScriptMode.Expression, false, "getCorrelationId")]
    [InlineData("getCorrelationId().x++", Elsa3JavaScriptMode.Script, false, "getCorrelationId")]
    [InlineData("getCorrelationId().x++", Elsa3JavaScriptMode.Script, true, "getCorrelationId")]
    [InlineData("--getCorrelationId().x", Elsa3JavaScriptMode.Expression, false, "getCorrelationId")]
    [InlineData("getCorrelationId().x += 'acme'", Elsa3JavaScriptMode.Script, false, "getCorrelationId")]
    [InlineData("getCorrelationId() = 'acme'", Elsa3JavaScriptMode.Expression, false, "getCorrelationId")]
    [InlineData("getCorrelationId() += 'acme'", Elsa3JavaScriptMode.Script, true, "getCorrelationId")]
    [InlineData("getCorrelationId().x ??= 'acme'", Elsa3JavaScriptMode.Script, false, "getCorrelationId")]
    [InlineData("getCorrelationId().lines[getHttpRequestBody().index].quantity = 2", Elsa3JavaScriptMode.Expression, false, "getCorrelationId")]
    [InlineData("getWorkflowInstanceId().x = 1", Elsa3JavaScriptMode.Expression, false, "getWorkflowInstanceId")]
    [InlineData("getWorkflowInstanceId().x = 1", Elsa3JavaScriptMode.Script, false, "getWorkflowInstanceId")]
    [InlineData("getWorkflowInstanceId().x = 1", Elsa3JavaScriptMode.Script, true, "getWorkflowInstanceId")]
    [InlineData("getWorkflowInstanceId()[getHttpRequestBody().name]--", Elsa3JavaScriptMode.Script, true, "getWorkflowInstanceId")]
    [InlineData("getWorkflowInstanceName().x = 1", Elsa3JavaScriptMode.Script, false, "getWorkflowInstanceName")]
    [InlineData("(Math.random() > 0.5 ? getCorrelationId() : {}).x = 1", Elsa3JavaScriptMode.Expression, false, "getCorrelationId")]
    [InlineData("var a = {}; (a || getCorrelationId()).x = 1", Elsa3JavaScriptMode.Expression, false, "getCorrelationId")]
    [InlineData("var a = {}; (Math.random() > 0.5 ? a : getWorkflowInstanceId()).x = 1", Elsa3JavaScriptMode.Script, false, "getWorkflowInstanceId")]
    [InlineData("var a = {}; (getCorrelationId() ?? a).x = 1", Elsa3JavaScriptMode.Script, true, "getCorrelationId")]
    [InlineData("var a = {}; (a, getWorkflowInstanceId()).x = 1", Elsa3JavaScriptMode.Script, true, "getWorkflowInstanceId")]
    [InlineData("async function f() { (await getCorrelationId()).x = 1; } f(); return 1;", Elsa3JavaScriptMode.Script, false, "getCorrelationId")]
    [InlineData("var a; (a = getWorkflowInstanceId()).x = 1", Elsa3JavaScriptMode.Expression, false, "getWorkflowInstanceId")]
    [InlineData("var a; (a ??= getCorrelationId()).x = 1", Elsa3JavaScriptMode.Script, true, "getCorrelationId")]
    [InlineData("({ status: getCorrelationId().x } = { status: 'Paid' })", Elsa3JavaScriptMode.Expression, false, "getCorrelationId")]
    [InlineData("[getWorkflowInstanceId().x] = [1]", Elsa3JavaScriptMode.Script, false, "getWorkflowInstanceId")]
    public void Change_through_a_workflow_instance_value_is_refused(string source, Elsa3JavaScriptMode mode, bool reusable, string reader)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables, mode, reusableWorkflow: reusable);

        Assert.False(result.Succeeded, result.Definition?.Source);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(Elsa3ImportDiagnosticCodes.ForbiddenCapability, issue.Code);
        Assert.Contains($" the value '{reader}()' returns", issue.Message, StringComparison.Ordinal);
        Assert.EndsWith("in the Elsa 3 workflow before import.", issue.Guidance, StringComparison.Ordinal);
    }

    /// <summary>
    /// #6230: a member change is told to go, an assignment to the value itself to go to a local copy, and the latter also
    /// names what changes the instance value where the script runs: in a non-reusable Run JavaScript body the
    /// workflowState setter (setCorrelationId, setWorkflowInstanceName), in an input a Correlate activity for the
    /// correlation ID; a reusable workflow's body, which cannot correlate (#3023), and the instance ID have no route.
    /// </summary>
    [Theory]
    [InlineData("getCorrelationId().x = 1", Elsa3JavaScriptMode.Script, false, "Remove the change, or keep the member on a local object of the script's own, ")]
    [InlineData("getCorrelationId() += 'acme'", Elsa3JavaScriptMode.Script, false, "(let value = getCorrelationId();) and change the local; to change the instance's correlation ID, call setCorrelationId(value), ")]
    [InlineData("getCorrelationId() = 'acme'", Elsa3JavaScriptMode.Expression, false, "; to change the instance's correlation ID, use a Correlate activity, ")]
    [InlineData("getCorrelationId() = 'acme'", Elsa3JavaScriptMode.Script, true, "(let value = getCorrelationId();) and change the local, in the Elsa 3 workflow before import.")]
    [InlineData("getCorrelationId()++", Elsa3JavaScriptMode.Expression, false, "JavaScript changes the value 'getCorrelationId()' returns itself.")]
    [InlineData("getWorkflowInstanceName() = 'acme'", Elsa3JavaScriptMode.Script, false, "; to change the instance's name, call setWorkflowInstanceName(value), ")]
    [InlineData("getWorkflowInstanceId() = 'acme'", Elsa3JavaScriptMode.Script, false, "and change the local, in the Elsa 3 workflow before import.")]
    public void Change_through_a_workflow_instance_value_names_the_route_that_changes_the_instance(
        string source, Elsa3JavaScriptMode mode, bool reusable, string route)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables, mode, reusableWorkflow: reusable);

        var issue = Assert.Single(result.Issues);
        Assert.Contains(route, issue.Message + " " + issue.Guidance, StringComparison.Ordinal);
    }

    /// <summary>
    /// #6230: the instance refusal adds to the conversion's own checks rather than replacing them: a call with arguments,
    /// a script's own workflowState, and an ambient value, an undeclared name, a custom global or 'arguments' in the same
    /// destructuring target are still reported.
    /// </summary>
    [Theory]
    [InlineData("getCorrelationId(1).x = 2", Elsa3JavaScriptMode.Expression, "JavaScript calls 'getCorrelationId' with arguments, which it does not take.")]
    [InlineData("var workflowState = 1; getCorrelationId().x = 2;", Elsa3JavaScriptMode.Script, "JavaScript declares its own 'workflowState', and a converted read in its scope would become 'workflowState' too, reading the script's value instead of the workflow's.")]
    [InlineData("[getInput('a').x, getCorrelationId().y] = [1, 2]", Elsa3JavaScriptMode.Script, "JavaScript mutates an Elsa 3 ambient value.")]
    [InlineData("[getCorrelationId().x, acme.y] = [1, 2]; return 1;", Elsa3JavaScriptMode.Script, "JavaScript uses 'acme', which neither the script, Elsa 4 nor a registered host function declares.")]
    [InlineData("[getCorrelationId().x, arguments.y] = [1, 2]; return 1;", Elsa3JavaScriptMode.Script, "JavaScript uses 'arguments' outside a function. Elsa 3 failed there; Elsa 4 would return an empty value.")]
    [InlineData("[getCorrelationId().x, acme] = [1, 2]; return acme;", Elsa3JavaScriptMode.Script, "JavaScript assigns to 'acme', which it never declares. Elsa 3 created a global; Elsa 4 runs in strict mode, where the assignment fails.")]
    public void Change_through_a_workflow_instance_value_keeps_the_other_refusals(string source, Elsa3JavaScriptMode mode, string otherMessage)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables, mode);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Message.StartsWith("JavaScript may change a member reached through the value 'getCorrelationId()' returns.", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Message == otherMessage);
    }

    /// <summary>
    /// #6230: a call that only shares an instance built-in's name is no instance read: a function the script declares in
    /// a block keeps its own refusal, and a variable whose generated getter has the name keeps the ambiguity refusal.
    /// </summary>
    [Fact]
    public void Change_through_a_call_named_like_an_instance_built_in_keeps_its_own_refusal()
    {
        var blockFunction = _sut.Rewrite("JavaScript", "{ function getCorrelationId() { return {}; } } getCorrelationId().x = 1;",
            new TypeReference("Object"), ResolveVariables, Elsa3JavaScriptMode.Script);
        var ambiguous = _sut.Rewrite("JavaScript", "getCorrelationId().x = 1", new TypeReference("Object"),
            reference => reference.Kind == Elsa3AmbientReferenceKind.GeneratedAccessor
                ? Elsa3ExpressionParameterResolution.SuccessVariable(
                    new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, $"variable-{reference.Name}"), reference.Name)
                : ResolveVariables(reference));

        Assert.False(blockFunction.Succeeded);
        Assert.Contains(blockFunction.Issues, issue => issue.Message.StartsWith("JavaScript uses 'getCorrelationId' outside the block", StringComparison.Ordinal));
        Assert.DoesNotContain(blockFunction.Issues, issue => issue.Message.Contains("()' returns", StringComparison.Ordinal));
        Assert.False(ambiguous.Succeeded);
        Assert.Contains(ambiguous.Issues, issue => issue.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
        Assert.DoesNotContain(ambiguous.Issues, issue => issue.Message.Contains("()' returns", StringComparison.Ordinal));
    }

    /// <summary>
    /// #6230: a += or -= assignment makes a new value, so a change of what it returns is no change of the instance value.
    /// </summary>
    [Theory]
    [InlineData("var a = ''; (a += getCorrelationId()).x = 1; return 1;", Elsa3JavaScriptMode.Expression, false)]
    [InlineData("var n = 1; (n -= getWorkflowInstanceId()).x = 1; return 1;", Elsa3JavaScriptMode.Script, true)]
    public void Change_of_a_compound_assignment_with_an_instance_value_is_no_instance_change(string source, Elsa3JavaScriptMode mode, bool reusable)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables, mode, reusableWorkflow: reusable);

        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains("()' returns", StringComparison.Ordinal));
    }

    /// <summary>
    /// #6230: in a with statement's body the call may read the with object, so only the with statement's refusal reports.
    /// </summary>
    [Fact]
    public void Change_through_an_instance_built_in_in_a_with_body_reports_only_the_with_refusal()
    {
        var result = _sut.Rewrite("JavaScript", "var o = {}; with (o) { getCorrelationId().x = 1; } return 1;", new TypeReference("Object"),
            ResolveVariables, Elsa3JavaScriptMode.Script);

        Assert.False(result.Succeeded);
        var issue = Assert.Single(result.Issues);
        Assert.StartsWith("JavaScript uses a with statement", issue.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #6230: what only reads a workflow instance value still converts: a plain read, a local copy, a computed key
    /// (#6222), a call's argument, a conditional's test, an array or object literal's element, and a sequence's earlier
    /// expression; as does, in a Run JavaScript body, writing it into a variable (spec 1231 FR-032).
    /// </summary>
    [Theory]
    [InlineData("return getCorrelationId() + '-acme';", Elsa3JavaScriptMode.Expression, false)]
    [InlineData("var id = getCorrelationId(); id = id + '-acme'; return id;", Elsa3JavaScriptMode.Expression, false)]
    [InlineData("var id = getWorkflowInstanceId(); id += '-acme'; return id;", Elsa3JavaScriptMode.Script, true)]
    [InlineData("var name = getWorkflowInstanceName(); name += '-acme'; return name;", Elsa3JavaScriptMode.Script, false)]
    [InlineData("var result = {}; result[getCorrelationId()] = 1; return result;", Elsa3JavaScriptMode.Expression, false)]
    [InlineData("var result = {}; result[getWorkflowInstanceId()] = 1; return result;", Elsa3JavaScriptMode.Script, true)]
    [InlineData("getOrder().correlationId = getCorrelationId();", Elsa3JavaScriptMode.Script, false)]
    [InlineData("variables.Order[getWorkflowInstanceId()] = 1;", Elsa3JavaScriptMode.Script, false)]
    [InlineData("var wrap = function (id) { return { id: id }; }; wrap(getCorrelationId()).x = 1; return 1;", Elsa3JavaScriptMode.Script, false)]
    [InlineData("var wrap = function (id) { return { id: id }; }; wrap(getWorkflowInstanceId()).x = 1; return 1;", Elsa3JavaScriptMode.Expression, false)]
    [InlineData("var a = {}, b = {}; (getCorrelationId() === 'acme' ? a : b).x = 1; return a;", Elsa3JavaScriptMode.Expression, false)]
    [InlineData("[getCorrelationId()][0] = 1; return 1;", Elsa3JavaScriptMode.Script, true)]
    [InlineData("({ id: getWorkflowInstanceId() }).id = 'acme'; return 1;", Elsa3JavaScriptMode.Expression, false)]
    [InlineData("var a = {}; (a, getWorkflowInstanceName(), a).x = 1; return a;", Elsa3JavaScriptMode.Script, false)]
    public void Read_or_local_copy_of_a_workflow_instance_value_converts(string source, Elsa3JavaScriptMode mode, bool reusable)
    {
        var result = _sut.Rewrite("JavaScript", source, new TypeReference("Object"), ResolveVariables, mode, reusableWorkflow: reusable);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Every name a test script reads, through getVariable or a generated getter, is a workflow variable, except the
    /// workflow instance's metadata (correlation id, instance id and name), which no variable shadows.
    /// </summary>
    private static Elsa3ExpressionParameterResolution ResolveVariables(Elsa3AmbientReference reference) => reference.Kind switch
    {
        Elsa3AmbientReferenceKind.GeneratedAccessor when reference.Name is "CorrelationId" or "WorkflowInstanceId" or "WorkflowInstanceName" => Elsa3ExpressionParameterResolution.Failure(
            new Elsa3ExpressionRewriteIssue(Elsa3ImportDiagnosticCodes.DanglingReference, "No variable.", "None.")),
        Elsa3AmbientReferenceKind.Variable or Elsa3AmbientReferenceKind.GeneratedAccessor => Elsa3ExpressionParameterResolution.SuccessVariable(
            new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, $"variable-{reference.Name}"), reference.Name),
        _ => Resolve(reference),
    };

    private static Elsa3ExpressionParameterResolution Resolve(Elsa3AmbientReference reference) => reference.Kind switch
    {
        Elsa3AmbientReferenceKind.Variable => Elsa3ExpressionParameterResolution.Success(
            new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, $"variable-{reference.Name}")),
        Elsa3AmbientReferenceKind.WorkflowRequest => Elsa3ExpressionParameterResolution.Success(
            new WorkflowRequestExpressionParameterBinding(reference.Name)),
        Elsa3AmbientReferenceKind.ActivityResult => Elsa3ExpressionParameterResolution.Success(
            new ActivityResultExpressionParameterBinding("producer", $"key:{reference.Name}")),
        _ => throw new ArgumentOutOfRangeException(nameof(reference)),
    };
}
