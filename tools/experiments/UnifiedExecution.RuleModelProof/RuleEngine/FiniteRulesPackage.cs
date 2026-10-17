using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.UnifiedExecution.ModelContracts;
using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.UnifiedExecution.FiniteRules;

/// <summary>Separately packaged finite positive propositional rule engine for the extension-contract proof.</summary>
public sealed class FiniteRulesPackage : IExecutionModelPackage
{
    public const string ModelId = "proof.finite-positive-rules";
    public const string HandlerId = "finite-rule-round";
    public const string HandlerVersion = "1";
    public const string StateSchema = "urn:elsa:proof:finite-positive-facts:1";
    private static readonly string[] Schemas = [StateSchema];

    public string PackageId => "elsa.proof.finite-positive-rules";
    public string PackageVersion => "1.0.0";
    public IReadOnlyCollection<string> SupportedStateSchemas => Schemas;
    public IReadOnlyCollection<ModelWorkHandler> WorkHandlers { get; } = [new(HandlerId, HandlerVersion)];

    public ICompositionPolicy CreatePolicy(PinnedModelArtifact artifact)
    {
        if (artifact.ModelId != ModelId || artifact.StateSchema != StateSchema)
            throw new InvalidDataException("The finite-rules package does not support the requested model identity or state schema.");
        return new FiniteRulesPolicy(this, artifact, RuleProgram.Parse(artifact.Json));
    }

    public string ExecuteWork(PinnedModelArtifact artifact, string handlerId, string handlerVersion, string inputJson)
    {
        if (handlerId != HandlerId || handlerVersion != HandlerVersion)
            throw new NotSupportedException($"This package handles only {HandlerId}@{HandlerVersion}.");
        if (artifact.ModelId != ModelId || artifact.StateSchema != StateSchema)
            throw new InvalidDataException("The worker artifact identity or state schema does not match this package.");
        RuleProgram program = RuleProgram.Parse(artifact.Json);
        RuleRoundRequest request = JsonSerializer.Deserialize<RuleRoundRequest>(inputJson)
            ?? throw new InvalidDataException("Rule-round request is empty.");
        ValidateRequest(artifact, request);
        RuleRoundResult result = Evaluate(program, artifact, request);
        return JsonSerializer.Serialize(result);
    }

    public void ValidateState(PinnedModelArtifact artifact, string stateJson)
    {
        if (artifact.ModelId != ModelId || artifact.StateSchema != StateSchema)
            throw new InvalidDataException("The finite-rules package cannot validate this model artifact.");
        RuleProgram program = RuleProgram.Parse(artifact.Json);
        RuleModelState state = JsonSerializer.Deserialize<RuleModelState>(stateJson)
            ?? throw new InvalidDataException("Persisted rule-model state is empty.");

        if (state.ModelId != artifact.ModelId || state.ModelVersion != artifact.ModelVersion
            || state.ArtifactFingerprint != artifact.Fingerprint || state.StateSchema != artifact.StateSchema)
            throw new InvalidDataException("Persisted model identity, artifact fingerprint, or state schema differs from the pinned package request.");
        if (state.Status is not ("Running" or "Completed"))
            throw new InvalidDataException($"Persisted rule-model status '{state.Status}' is unsupported.");
        if (state.Round < 0 || state.Trace.Length != state.Round)
            throw new InvalidDataException("Persisted rule round and trace length disagree.");
        if (state.Facts.Distinct(StringComparer.Ordinal).Count() != state.Facts.Length
            || !state.Facts.SequenceEqual(state.Facts.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Persisted facts must be a sorted set.");
        if (!state.Facts.ToHashSet(StringComparer.Ordinal).IsSubsetOf(program.Vocabulary))
            throw new InvalidDataException("Persisted state contains a fact outside the finite artifact vocabulary.");

        var replayedFacts = program.InitialFacts.ToHashSet(StringComparer.Ordinal);
        for (int i = 0; i < state.Trace.Length; i++)
        {
            RuleRoundTrace round = state.Trace[i];
            if (round.Round != i || !round.AddedFacts.SequenceEqual(round.AddedFacts.Order(StringComparer.Ordinal), StringComparer.Ordinal)
                || round.AddedFacts.Distinct(StringComparer.Ordinal).Count() != round.AddedFacts.Length)
                throw new InvalidDataException("Persisted round trace has invalid numbering or added facts.");

            RuleDefinition[] fired = program.Rules.Where(rule => rule.WhenAll.All(replayedFacts.Contains)).ToArray();
            string[] expectedFired = fired.Select(rule => rule.Id).Order(StringComparer.Ordinal).ToArray();
            string[] expectedAdded = fired.Select(rule => rule.Then).Where(fact => !replayedFacts.Contains(fact))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (!round.FiredRuleIds.SequenceEqual(expectedFired, StringComparer.Ordinal)
                || !round.AddedFacts.SequenceEqual(expectedAdded, StringComparer.Ordinal))
                throw new InvalidDataException("Persisted round trace differs from deterministic evaluation of the pinned rule artifact.");
            if (round.AddedFacts.Any(fact => !replayedFacts.Add(fact)))
                throw new InvalidDataException("Rule inference trace is not monotone; it repeats an already-present fact.");
            if (round.AddedFacts.Length == 0 && i != state.Trace.Length - 1)
                throw new InvalidDataException("A least-fixed-point round cannot be followed by another persisted round.");
        }
        if (!replayedFacts.SetEquals(state.Facts))
            throw new InvalidDataException("Persisted facts do not equal the base facts plus the trace additions.");
        bool fixedPointReached = state.Trace.Length > 0 && state.Trace[^1].AddedFacts.Length == 0;
        if ((state.Status == "Completed") != fixedPointReached || state.LeastFixedPointReached != fixedPointReached)
            throw new InvalidDataException("Execution completion must coincide with the recorded least-fixed-point condition.");
    }

    public ModelInspection Inspect(PinnedModelArtifact artifact, string stateJson)
    {
        ValidateState(artifact, stateJson);
        RuleModelState state = JsonSerializer.Deserialize<RuleModelState>(stateJson)!;
        var detail = new
        {
            facts = state.Facts,
            round = state.Round,
            trace = state.Trace,
            leastFixedPointReached = state.LeastFixedPointReached
        };
        return new ModelInspection(artifact.ModelId, artifact.ModelVersion, artifact.StateSchema, artifact.Fingerprint,
            state.Status, JsonSerializer.Serialize(detail));
    }

    internal static RuleRoundRequest MakeRequest(PinnedModelArtifact artifact, int round, string[] facts)
    {
        string[] orderedFacts = facts.Order(StringComparer.Ordinal).ToArray();
        return new RuleRoundRequest(round, artifact.Fingerprint, orderedFacts, Fingerprint(JsonSerializer.Serialize(orderedFacts)));
    }

    internal static RuleRoundResult Evaluate(RuleProgram program, PinnedModelArtifact artifact, RuleRoundRequest request)
    {
        ValidateRequest(artifact, request);
        HashSet<string> facts = request.Facts.ToHashSet(StringComparer.Ordinal);
        RuleDefinition[] fired = program.Rules
            .Where(rule => rule.WhenAll.All(facts.Contains))
            .OrderBy(rule => rule.Id, StringComparer.Ordinal)
            .ToArray();
        string[] added = fired.Select(x => x.Then).Where(x => !facts.Contains(x)).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        return new RuleRoundResult(request.Round, artifact.Fingerprint, request.FactsFingerprint,
            fired.Select(x => x.Id).ToArray(), added);
    }

    private static void ValidateRequest(PinnedModelArtifact artifact, RuleRoundRequest request)
    {
        if (request.ArtifactFingerprint != artifact.Fingerprint || request.Round < 0
            || !request.Facts.SequenceEqual(request.Facts.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || request.Facts.Distinct(StringComparer.Ordinal).Count() != request.Facts.Length
            || request.FactsFingerprint != Fingerprint(JsonSerializer.Serialize(request.Facts)))
            throw new InvalidDataException("Rule-round request is not bound to the pinned artifact and canonical fact snapshot.");
    }

    private static string Fingerprint(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed class FiniteRulesPolicy(
    FiniteRulesPackage package,
    PinnedModelArtifact artifact,
    RuleProgram program) : ICompositionPolicy
{
    public string Id => $"{artifact.ModelId}@{artifact.ModelVersion}:{artifact.Fingerprint}:{artifact.StateSchema}";

    public CompositionPlan Start(string inputJson)
    {
        RuleModelState state = new(artifact.ModelId, artifact.ModelVersion, artifact.Fingerprint, artifact.StateSchema,
            program.InitialFacts.Order(StringComparer.Ordinal).ToArray(), 0, "Running", false, []);
        string stateJson = JsonSerializer.Serialize(state);
        package.ValidateState(artifact, stateJson);
        RuleRoundRequest request = FiniteRulesPackage.MakeRequest(artifact, 0, state.Facts);
        ModelWorkHandler handler = package.WorkHandlers.Single();
        return new CompositionPlan(stateJson,
            [new WorkDefinition(InvocationId(0), handler.Id, handler.Version, JsonSerializer.Serialize(request))],
            [], false);
    }

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        package.ValidateState(artifact, stateJson);
        RuleModelState state = JsonSerializer.Deserialize<RuleModelState>(stateJson)!;
        if (state.Status != "Running" || state.LeastFixedPointReached)
            throw new InvalidOperationException("A completed finite-rule execution cannot consume another round result.");
        if (receipt.Status != "Succeeded" || receipt.InvocationId != InvocationId(state.Round))
            throw new InvalidOperationException("The result is not the current successful rule round.");

        RuleRoundResult result = JsonSerializer.Deserialize<RuleRoundResult>(receipt.ResultJson)
            ?? throw new InvalidDataException("Rule-round result is empty.");
        RuleRoundRequest request = FiniteRulesPackage.MakeRequest(artifact, state.Round, state.Facts);
        if (result.Round != state.Round || result.ArtifactFingerprint != artifact.Fingerprint
            || result.InputFactsFingerprint != request.FactsFingerprint)
            throw new InvalidDataException("Rule-round result does not match the committed round request.");

        RuleRoundResult expected = FiniteRulesPackage.Evaluate(program, artifact, request);
        if (!expected.FiredRuleIds.SequenceEqual(result.FiredRuleIds, StringComparer.Ordinal)
            || !expected.AddedFacts.SequenceEqual(result.AddedFacts, StringComparer.Ordinal))
            throw new InvalidDataException("Rule-round result differs from deterministic package evaluation.");

        string[] facts = state.Facts.Concat(result.AddedFacts).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (!state.Facts.ToHashSet(StringComparer.Ordinal).IsSubsetOf(facts))
            throw new InvalidOperationException("Rule execution attempted a non-monotone fact transition.");
        var trace = state.Trace.Append(new RuleRoundTrace(state.Round, result.FiredRuleIds, result.AddedFacts)).ToArray();
        bool fixedPoint = result.AddedFacts.Length == 0;
        var next = state with
        {
            Facts = facts,
            Round = state.Round + 1,
            Status = fixedPoint ? "Completed" : "Running",
            LeastFixedPointReached = fixedPoint,
            Trace = trace
        };
        string nextStateJson = JsonSerializer.Serialize(next);
        package.ValidateState(artifact, nextStateJson);
        if (fixedPoint)
            return new CompositionPlan(nextStateJson, [], [], true);

        RuleRoundRequest nextRequest = FiniteRulesPackage.MakeRequest(artifact, next.Round, next.Facts);
        ModelWorkHandler handler = package.WorkHandlers.Single();
        return new CompositionPlan(nextStateJson,
            [new WorkDefinition(InvocationId(next.Round), handler.Id, handler.Version, JsonSerializer.Serialize(nextRequest))],
            [], false);
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
        => throw new InvalidOperationException($"Finite positive rule closure has no continuation '{continuationId}'.");

    private static string InvocationId(int round) => $"rule-round-{round:D4}";
}

internal sealed record RuleDefinition(string Id, string[] WhenAll, string Then);
internal sealed record RuleProgram(string[] InitialFacts, RuleDefinition[] Rules, HashSet<string> Vocabulary)
{
    public static RuleProgram Parse(string json)
    {
        RuleArtifact artifact = JsonSerializer.Deserialize<RuleArtifact>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Rule artifact is empty.");
        if (artifact.InitialFacts.Length == 0 || artifact.Rules.Length == 0)
            throw new InvalidDataException("Finite rule artifact requires at least one base fact and rule.");
        if (artifact.Rules.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != artifact.Rules.Length)
            throw new InvalidDataException("Rule identifiers must be unique.");
        if (artifact.InitialFacts.Any(string.IsNullOrWhiteSpace) || artifact.Rules.Any(rule =>
                string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Then)
                || rule.WhenAll.Length == 0 || rule.WhenAll.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("Rules require non-empty identifiers, premises, and conclusions.");
        if (artifact.InitialFacts.Length + artifact.Rules.Length > 512)
            throw new InvalidDataException("Finite proof artifacts are bounded to 512 base facts and rule conclusions.");
        string[] initial = artifact.InitialFacts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        RuleDefinition[] rules = artifact.Rules.Select(x => new RuleDefinition(x.Id,
                x.WhenAll.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), x.Then))
            .OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var vocabulary = initial.Concat(rules.SelectMany(x => x.WhenAll.Append(x.Then))).ToHashSet(StringComparer.Ordinal);
        return new RuleProgram(initial, rules, vocabulary);
    }

    private sealed record RuleArtifact(string[] InitialFacts, RuleDefinition[] Rules);
}

internal sealed record RuleModelState(
    string ModelId,
    string ModelVersion,
    string ArtifactFingerprint,
    string StateSchema,
    string[] Facts,
    int Round,
    string Status,
    bool LeastFixedPointReached,
    RuleRoundTrace[] Trace);

internal sealed record RuleRoundRequest(int Round, string ArtifactFingerprint, string[] Facts, string FactsFingerprint);
internal sealed record RuleRoundResult(int Round, string ArtifactFingerprint, string InputFactsFingerprint, string[] FiredRuleIds, string[] AddedFacts);
internal sealed record RuleRoundTrace(int Round, string[] FiredRuleIds, string[] AddedFacts);
