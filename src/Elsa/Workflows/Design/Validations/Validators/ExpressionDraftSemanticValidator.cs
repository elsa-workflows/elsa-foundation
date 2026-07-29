using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>Runs provider validation over every authored text expression without evaluating it.</summary>
public sealed class ExpressionDraftSemanticValidator(
    IExpressionToolingProviderResolver providerResolver,
    IActivityStructureService structureService) : IExpressionDraftSemanticValidator
{
    public async ValueTask<ExpressionDraftValidationResult> ValidateAsync(WorkflowDefinitionState state, string documentScope, CancellationToken cancellationToken)
    {
        var diagnostics = new List<ExpressionDiagnostic>();
        var unavailable = false;
        foreach (var node in Walk(state.RootActivity))
        foreach (var (argumentBag, argument) in node.Inputs.Select(argument => ("inputs", argument))
                     .Concat(node.Outputs.Select(argument => ("outputs", argument))))
        {
            if (string.IsNullOrWhiteSpace(argument.Value.ExpressionType) ||
                string.Equals(argument.Value.ExpressionType, "Literal", StringComparison.OrdinalIgnoreCase) ||
                !TryGetSource(argument.Value.Value, out var source))
                continue;
            var authoredPath = $"{node.NodeId}/{argumentBag}/{argument.ReferenceKey}";
            var provider = providerResolver.Find(argument.Value.ExpressionType);
            if (provider is null)
            {
                unavailable = true;
                continue;
            }

            var revision = Revision(source);
            var document = new ExpressionAuthoringDocument($"{documentScope}:{node.NodeId}:{argument.ReferenceKey}:{argument.Value.ExpressionType}", documentScope, node.NodeId, argument.ReferenceKey, argument.Value.ExpressionType, revision);
            var context = new ExpressionAuthoringContext(ExpressionToolingContractVersion.V1, document, revision, revision, [], new());
            ExpressionToolingOutcome<ExpressionDiagnosticSet> result;
            try
            {
                result = await provider.ValidateAsync(new(new(ExpressionToolingContractVersion.V1, document, context), source), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or
                StackOverflowException or
                AccessViolationException))
            {
                unavailable = true;
                continue;
            }
            if (result.State == ExpressionToolingOutcomeState.Unavailable)
                unavailable = true;
            else if (!result.IsSuccess)
                return new(MapFailure(result.State), diagnostics, result.Code);
            else if (result.Payload is not null)
                diagnostics.AddRange(result.Payload.Diagnostics.Select(diagnostic => diagnostic with { AuthoredPath = authoredPath }));
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == ExpressionDiagnosticSeverity.Error)
            ? new(ExpressionDraftValidationState.Errors, diagnostics)
            : unavailable
                ? new(ExpressionDraftValidationState.Unavailable, diagnostics, "provider-unavailable")
                : new(ExpressionDraftValidationState.Valid, diagnostics);

        IEnumerable<ActivityNode> Walk(ActivityNode? root)
        {
            if (root is null) yield break;
            var stack = new Stack<ActivityNode>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            stack.Push(root);
            while (stack.TryPop(out var node))
            {
                if (!visited.Add(node.NodeId)) continue;
                yield return node;
                foreach (var child in structureService.ProjectChildren(node).SelectMany(projection => projection.Activities)) stack.Push(child);
            }
        }
    }

    private static bool TryGetSource(object? value, out string source)
    {
        switch (value)
        {
            case string text:
                source = text;
                return true;
            case ExpressionDefinition definition:
                source = definition.Source;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                source = element.GetString() ?? string.Empty;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Object } element when TryGetJsonSource(element, out var jsonSource):
                source = jsonSource;
                return true;
            default:
                source = string.Empty;
                return false;
        }
    }

    private static bool TryGetJsonSource(JsonElement element, out string source)
    {
        foreach (var property in element.EnumerateObject().Where(property =>
                     string.Equals(property.Name, "source", StringComparison.OrdinalIgnoreCase) &&
                     property.Value.ValueKind == JsonValueKind.String))
        {
            source = property.Value.GetString() ?? string.Empty;
            return true;
        }
        source = string.Empty;
        return false;
    }

    private static ExpressionDraftValidationState MapFailure(ExpressionToolingOutcomeState state) => state switch
    {
        ExpressionToolingOutcomeState.Unauthorized => ExpressionDraftValidationState.Unauthorized,
        ExpressionToolingOutcomeState.Incompatible => ExpressionDraftValidationState.Incompatible,
        ExpressionToolingOutcomeState.Stale => ExpressionDraftValidationState.Stale,
        ExpressionToolingOutcomeState.Canceled => ExpressionDraftValidationState.Canceled,
        _ => ExpressionDraftValidationState.Unavailable
    };

    private static string Revision(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
}
