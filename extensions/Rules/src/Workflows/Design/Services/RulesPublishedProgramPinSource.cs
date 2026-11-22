using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Rules.Workflows.Design.Services;

/// <summary>Compiles literal finite Rules programs and pins their artifact and installed identity at publication.</summary>
internal sealed class RulesPublishedProgramPinSource(
    IRuleArtifactCompiler artifactCompiler,
    IRuleImplementationIdentitySource identitySource) : IExecutableCompilationSource
{
    private const string ProgramInputKey = nameof(RunRules.Program);
    private const int MaximumRawLiteralBytes = (RuleLimits.MaxArtifactBytes * 6) + 2;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 12 };

    public async ValueTask<ExecutableCompilationContribution> GetContributionAsync(
        ExecutableCompilationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nodes = context.RootActivity.DescendantsAndSelf()
            .Where(node => StringComparer.Ordinal.Equals(node.ActivityType, RunRules.ActivityType))
            .OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal)
            .ToArray();
        if (nodes.Length == 0)
            return new ExecutableCompilationContribution();

        var prepared = new List<(ExecutableNode Node, string Source, RuleArtifactCompilation Compilation)>(nodes.Length);
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ReadLiteralProgram(node);
            try
            {
                var program = ParseProgram(source);
                var compilation = artifactCompiler.Compile(program)
                    ?? throw new InvalidDataException("The Rules artifact compiler returned no artifact.");
                prepared.Add((node, source, compilation));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException or
                                              NotSupportedException or NullReferenceException or OverflowException)
            {
                throw Refused(node, "requires a valid bounded finite Rules program literal.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        RuleSemanticPin? implementation;
        try
        {
            implementation = await identitySource.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException or
                                          NotSupportedException or IOException or UnauthorizedAccessException)
        {
            throw Refused(prepared[0].Node, "could not verify the installed Rules implementation identity.");
        }

        if (implementation is null)
            throw Refused(prepared[0].Node, "could not verify the installed Rules implementation identity.");

        var claims = new List<ExecutableNodeMetadataClaim>(prepared.Count);
        foreach (var item in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var pin = new RuleWorkflowProgramPin(1,
                    RuleWorkflowProgramPin.SourceHash(item.Source), item.Compilation.Artifact,
                    implementation, item.Compilation.VocabularyCount);
                claims.Add(new(item.Node.ExecutableNodeId, RunRules.PinMetadataKey, pin.Serialize()));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                              NotSupportedException or OverflowException)
            {
                throw Refused(item.Node, "could not pin the compiled program to a supported installed Rules identity.");
            }
        }

        return new ExecutableCompilationContribution(nodeMetadata: claims);
    }

    private static string ReadLiteralProgram(ExecutableNode node)
    {
        var binding = node.InputBindings.GetValueOrDefault(ProgramInputKey);
        SecretBindingDiagnostics.ThrowIfSecretRead(binding, node.ExecutableNodeId, ProgramInputKey);
        if (binding?.Source != RuntimeInputBindingSource.Literal ||
            binding.Literal is not { Presence: ValuePresence.Present } ||
            binding.LiteralValue is not { ValueKind: JsonValueKind.String } literal)
            throw Refused(node, $"requires a literal '{ProgramInputKey}' finite Rules program.");

        var rawLiteral = JsonMarshal.GetRawUtf8Value(literal);
        if (rawLiteral.Length > MaximumRawLiteralBytes)
            throw Refused(node, "has an oversized finite Rules program literal.");

        var source = literal.GetString();
        if (string.IsNullOrEmpty(source) || source.Length > RuleLimits.MaxArtifactBytes)
            throw Refused(node, "requires a nonempty bounded finite Rules program literal.");

        try
        {
            if (StrictUtf8.GetByteCount(source) > RuleLimits.MaxArtifactBytes)
                throw Refused(node, "has an oversized finite Rules program literal.");
        }
        catch (EncoderFallbackException)
        {
            throw Refused(node, "has a finite Rules program literal that is not valid Unicode.");
        }

        return source;
    }

    private static RuleProgram ParseProgram(string source)
    {
        using var document = JsonDocument.Parse(source, DocumentOptions);
        var root = ReadObject(document.RootElement, ["formatVersion", "languageId", "languageVersion", "vocabulary", "rules"]);
        var formatVersion = ReadInteger(Required(root, "formatVersion"));
        var languageId = ReadString(Required(root, "languageId"));
        var languageVersion = ReadInteger(Required(root, "languageVersion"));
        var vocabulary = ReadFacts(Required(root, "vocabulary"), RuleLimits.MaxVocabularyFacts);

        var rulesElement = Required(root, "rules");
        if (rulesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException();
        var rules = new List<RuleDefinition>();
        foreach (var ruleElement in rulesElement.EnumerateArray())
        {
            if (rules.Count == RuleLimits.MaxRules)
                throw new InvalidDataException();
            var rule = ReadObject(ruleElement, ["id", "premises", "conclusion"]);
            var id = ReadString(Required(rule, "id"));
            var premises = ReadFacts(Required(rule, "premises"), RuleLimits.MaxPremisesPerRule);
            var conclusion = ReadString(Required(rule, "conclusion"));
            rules.Add(new RuleDefinition(id, premises, new RuleFact(conclusion)));
        }

        return new RuleProgram(formatVersion, languageId, languageVersion, vocabulary, rules);
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement value, string[] allowedProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException();

        var allowed = allowedProperties.ToHashSet(StringComparer.Ordinal);
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !properties.TryAdd(property.Name, property.Value))
                throw new InvalidDataException();
        }

        if (properties.Count != allowed.Count)
            throw new InvalidDataException();
        return properties;
    }

    private static JsonElement Required(Dictionary<string, JsonElement> properties, string name) => properties[name];

    private static int ReadInteger(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidDataException();

    private static string ReadString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new InvalidDataException()
            : throw new InvalidDataException();

    private static RuleFact[] ReadFacts(JsonElement value, int maximumCount)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException();

        var facts = new List<RuleFact>();
        foreach (var item in value.EnumerateArray())
        {
            if (facts.Count == maximumCount)
                throw new InvalidDataException();
            facts.Add(new RuleFact(ReadString(item)));
        }

        return facts.ToArray();
    }

    private static ArgumentException Refused(ExecutableNode node, string reason) =>
        new($"RunRules node '{node.ExecutableNodeId}' {reason}");
}
