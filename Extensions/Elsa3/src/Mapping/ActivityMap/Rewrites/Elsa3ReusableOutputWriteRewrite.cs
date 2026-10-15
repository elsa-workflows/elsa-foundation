using System.Security.Cryptography;
using System.Text;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using Elsa3.Models;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// SetOutput in a reusable workflow (spec FR-004e; member-map "SetOutput in reusable workflows"). Elsa 3 writes a
/// declared output of the workflow-as-activity; Elsa 4's <c>set-output</c> intrinsic writes the running workflow's
/// output and does not see graph boundaries. So a <c>SetOutput</c> naming one of the reusable workflow's declared
/// outputs becomes the <c>set</c> intrinsic on a synthesized graph variable, and the graph manifest's
/// <c>outputMappings</c> map the output to that variable. Elsewhere (a plain workflow, or an undeclared name, Elsa 3's
/// fallback) it stays the <c>set-output</c> intrinsic.
/// </summary>
internal sealed class Elsa3ReusableOutputWriteRewrite : IElsa3NodeRewrite
{
    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.ReusableOutputWrite;

    public ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        if (!context.IsReusable ||
            !context.Activity.AdditionalProperties.TryGetValue(Elsa3IntrinsicContracts.NameKey, out var name) ||
            !Elsa3TypedValues.TryGetLiteral(name, out var outputName) ||
            context.Plan.Definition.Outputs?.FirstOrDefault(output => StringComparer.OrdinalIgnoreCase.Equals(output.Name, outputName)) is not { } declared)
            return ValueTask.CompletedTask;

        var variableKey = VariableKey(declared.Name);
        var type = context.ResolveType(string.IsNullOrWhiteSpace(declared.Type) ? "Object" : declared.Type);
        if (!context.Plan.Definition.Variables.Any(variable => StringComparer.Ordinal.Equals(variable.Id, variableKey)))
            context.Plan.Definition.Variables.Add(new Elsa3Variable { Id = variableKey, Name = variableKey, TypeName = declared.Type ?? "Object" });
        if (!context.Plan.OutputMappings.Any(mapping => StringComparer.Ordinal.Equals(mapping.OutputReferenceKey, declared.Name)))
            context.Plan.OutputMappings.Add(new Elsa3GraphOutputMapping(declared.Name, variableKey, type, declared.StorageDriverType));

        context.Activity.AdditionalProperties.Remove(Elsa3IntrinsicContracts.NameKey);
        context.Node.Resolved = Elsa3IntrinsicContracts.Resolve(Elsa3IntrinsicContracts.Set, context.Node.Resolved?.Entry);
        context.Node.Intrinsic = new AuthoredWorkflowIntrinsic(
            AuthoredWorkflowIntrinsicKind.Set,
            type,
            new VariableReference(variableKey, VariableReference.WorkflowScopeId));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// <c>elsa3.output.{first 16 hex characters of SHA-256(output reference key)}</c>: deterministic, and no Elsa 3
    /// variable id has that shape.
    /// </summary>
    internal static string VariableKey(string outputReferenceKey) =>
        $"elsa3.output.{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(outputReferenceKey)))[..16]}";
}
