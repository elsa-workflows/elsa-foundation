using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Xml;
using Elsa.Decisions.Core;

namespace Elsa.Decisions;

/// <summary>The standalone compiler/evaluator for the explicitly bounded Elsa DMN table profile.</summary>
public sealed class DecisionEngine : IDecisionCompiler, IDecisionEvaluator
{
    public DecisionCompilation Compile(string xml, CancellationToken cancellationToken = default)
    {
        var parser = new DecisionXml(cancellationToken);
        try
        {
            var table = parser.Parse(xml);
            if (table is null) return new(null, parser.Diagnostics);
            var canonical = DecisionXml.Canonical(table, cancellationToken);
            if (canonical.Length > DecisionXml.MaximumXmlBytes) return CompilationFailure("CanonicalDocumentTooLarge");
            return new(new DecisionArtifact(table.Id, DecisionXml.Hash(DecisionXml.Bytes(xml)), DecisionXml.Hash(canonical), DecisionXml.Profile, DecisionXml.Semantics, canonical), []);
        }
        catch (OperationCanceledException) { return CompilationFailure("Cancelled"); }
        catch (XmlException) { return CompilationFailure("InvalidXml"); }
        catch (EncoderFallbackException) { return CompilationFailure("InvalidUnicode"); }
        catch (DecisionLanguageException exception) { return CompilationFailure(exception.Code); }
    }

    public DecisionEvaluation Evaluate(DecisionArtifact artifact, IReadOnlyDictionary<string, DecisionValue> inputs,
        int operationBudget = 1_000_000, CancellationToken cancellationToken = default)
    {
        var operations = 0;
        var traces = new List<DecisionRuleTrace>();
        var matched = new List<(DecisionRow Row, int TraceIndex)>();
        void Tick()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operations >= operationBudget) throw new EvaluationBudgetException();
            operations++;
        }
        DecisionEvaluation Result(string status, JsonElement value = default, string? code = null) => new(status,
            artifact?.DecisionId ?? "", artifact?.ContentHash ?? "", artifact?.ProfileId ?? "", artifact?.SemanticsId ?? "",
            value, traces, code is null ? [] : [new DecisionDiagnostic(code, "Decision evaluation did not produce a successful result.", artifact?.DecisionId)], operations);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (artifact is null || inputs is null) return Result("InvalidInput", code: "InvalidInvocation");
            if (artifact.ProfileId != DecisionXml.Profile || artifact.SemanticsId != DecisionXml.Semantics) return Result("Unavailable", code: "UnsupportedSemantics");
            var bytes = artifact.ExecutableBytes;
            if (bytes.Length > DecisionXml.MaximumXmlBytes || !ValidHash(artifact.ContentHash) || !ValidHash(artifact.SourceHash) || DecisionXml.Hash(bytes) != artifact.ContentHash)
                return Result("Unsupported", code: "InvalidArtifact");
            var parser = new DecisionXml(cancellationToken);
            var table = parser.Parse(new UTF8Encoding(false, true).GetString(bytes));
            if (table is null || table.Id != artifact.DecisionId || !DecisionXml.Canonical(table, cancellationToken).AsSpan().SequenceEqual(bytes))
                return Result("Unsupported", code: "InvalidArtifact");
            if (inputs.Count > table.Inputs.Length) return Result("InvalidInput", code: "InputMembership");
            var copied = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
            foreach (var entry in inputs)
            {
                Tick();
                if (copied.Count >= 32 || entry.Key is null || !copied.TryAdd(entry.Key, DecisionScalars.Normalize(entry.Value))) return Result("InvalidInput", code: "InputMembership");
            }
            if (JsonSerializer.SerializeToUtf8Bytes(copied, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Length > 262_144) return Result("InvalidInput", code: "InputPayloadTooLarge");
            foreach (var input in table.Inputs)
            {
                if (!copied.TryGetValue(input.Name, out var value)) return Result("InvalidInput", code: "MissingInput");
                if (value.Kind != "null" && value.Kind != input.Type) return Result("InvalidInput", code: "InputTypeMismatch");
            }
            foreach (var row in table.Rows)
            {
                Tick();
                var cells = new List<DecisionCellTrace>();
                var rowMatched = true;
                for (var i = 0; i < table.Inputs.Length; i++)
                {
                    Tick();
                    var test = row.Tests[i];
                    var input = copied[table.Inputs[i].Name];
                    bool? truth = test.Wildcard ? input.Kind != "null" : false;
                    if (!test.Wildcard)
                    {
                        var anyTrue = false; var anyUnknown = false;
                        foreach (var alternative in test.Alternatives)
                        {
                            Tick();
                            var current = alternative.Match(input);
                            anyTrue |= current == true;
                            anyUnknown |= current is null;
                        }
                        truth = anyTrue ? true : anyUnknown ? null : false;
                        if (test.Negated) truth = truth.HasValue ? !truth.Value : null;
                    }
                    cells.Add(new(table.Inputs[i].Id, truth == true ? "true" : truth == false ? "false" : "unknown"));
                    rowMatched &= truth == true;
                }
                if (rowMatched) matched.Add((row, traces.Count));
                traces.Add(new(row.Id, rowMatched, false, cells));
            }
            if (matched.Count == 0) return Result("NoMatch", Tagged(new DecisionValue("null", null)));
            if (table.Policy == "UNIQUE" && matched.Count > 1) return Result("HitPolicyViolation", code: "UniqueOverlap");
            if (table.Policy == "ANY")
            {
                foreach (var row in matched.Skip(1))
                {
                    for (var i = 0; i < table.Outputs.Length; i++)
                    {
                        Tick();
                        if (!DecisionScalars.Equal(matched[0].Row.Values[i], row.Row.Values[i])) return Result("HitPolicyViolation", code: "AnyConflict");
                    }
                }
            }
            var selected = table.Policy is "FIRST" or "UNIQUE" ? matched.Take(1).ToArray() : matched.ToArray();
            var outputs = new List<JsonElement>();
            foreach (var entry in selected)
            {
                Tick();
                var trace = traces[entry.TraceIndex];
                traces[entry.TraceIndex] = new(trace.RuleId, trace.Matched, true, trace.Cells);
                if (table.Policy == "ANY" && outputs.Count > 0) continue;
                for (var i = 0; i < table.Outputs.Length; i++) Tick();
                outputs.Add(table.Outputs.Length == 1 ? Tagged(entry.Row.Values[0]) : JsonSerializer.SerializeToElement(
                    table.Outputs.Select((output, i) => new KeyValuePair<string, JsonElement>(output.Name, Tagged(entry.Row.Values[i]))).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)));
            }
            return Result("Success", table.Policy is "RULE ORDER" or "COLLECT" ? JsonSerializer.SerializeToElement(outputs) : outputs[0]);
        }
        catch (OperationCanceledException) { return Result("Cancelled", code: "Cancelled"); }
        catch (EvaluationBudgetException) { return Result("BudgetExceeded", code: "BudgetExceeded"); }
        catch (DecisionLanguageException exception) { return Result("InvalidInput", code: exception.Code); }
        catch (XmlException) { return Result("Unsupported", code: "InvalidArtifact"); }
        catch (DecoderFallbackException) { return Result("Unsupported", code: "InvalidArtifact"); }
    }

    private static DecisionCompilation CompilationFailure(string code) => new(null, [new DecisionDiagnostic(code, "Decision content could not be compiled under the supported profile.")]);
    private static JsonElement Tagged(DecisionValue value) => JsonSerializer.SerializeToElement(new { kind = value.Kind, value = value.Value });
    private static bool ValidHash(string? hash) => hash?.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private sealed class EvaluationBudgetException : Exception;
}
