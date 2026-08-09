using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Contracts.Generator;

/// <summary>
/// docs/consumer-guide/claims.json — the behavioural knowledge that no descriptor field can carry (spec 150
/// FR-B2). Each claim is one sentence in a typed envelope, and each names the tests that pin it.
/// </summary>
/// <remarks>
/// The envelope exists so a consumer can filter before it reads: <see cref="Scope"/> says which surface the
/// sentence is about, <see cref="Kind"/> whether it constrains what you author or predicts what the server
/// does, and <see cref="Stability"/> whether it is safe to build on. A claim with no pinning test is not
/// publishable — Gate A rejects it — because an unpinned sentence rots silently, which is worse than an
/// absent one.
/// </remarks>
public sealed record ConsumerClaimSet(string SchemaVersion, IReadOnlyList<ConsumerClaim> Claims);

public sealed record ConsumerClaim(
    string Id,
    string Scope,
    string Kind,
    string Stability,
    string Statement,
    IReadOnlyList<ClaimPinReference> Tests,
    string Since,
    // Optional: why the claim exists at all — usually the concrete failure it prevents.
    string? Rationale = null);

/// <summary>
/// A claim's pin: the test that guards it, and the exact assertion within that test the claim rests on.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Asserts"/> exists because naming a test is not enough. A published claim once asserted that a
/// node with no authored outputs produces no output evidence, pinned to a test named
/// <c>…_is_unenforced_when_the_node_authors_no_output_at_all</c>. That test is about publish-time
/// ENFORCEMENT; the claim was about runtime CAPTURE. The test passed, the gate stayed green, and the claim
/// was the opposite of the truth — a consumer following it concluded it could not assert on an unbound
/// output at all.
/// </para>
/// <para>
/// Quoting the assertion does not prove the claim follows from it — no gate can check entailment. It does
/// two things that would have caught that defect: it forces whoever writes the claim to read what is
/// actually asserted, and it fails loudly when the assertion is later edited or removed, which naming the
/// test alone does not.
/// </para>
/// </remarks>
[JsonConverter(typeof(ClaimPinReferenceConverter))]
public sealed record ClaimPinReference(string Test, string Asserts);

/// <summary>Accepts the object form only; a bare string is rejected with the reason, not silently upgraded.</summary>
public sealed class ClaimPinReferenceConverter : JsonConverter<ClaimPinReference>
{
    public override ClaimPinReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            throw new JsonException(
                $"Claim pin '{reader.GetString()}' is a bare test name. Pins must name the assertion they rest " +
                "on: {\"test\": \"…\", \"asserts\": \"…\"}.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var test = root.TryGetProperty("test", out var testValue) ? testValue.GetString() : null;
        var asserts = root.TryGetProperty("asserts", out var assertsValue) ? assertsValue.GetString() : null;

        if (string.IsNullOrWhiteSpace(test) || string.IsNullOrWhiteSpace(asserts))
            throw new JsonException("A claim pin requires both 'test' and 'asserts'.");

        return new ClaimPinReference(test, asserts);
    }

    public override void Write(Utf8JsonWriter writer, ClaimPinReference value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("test", value.Test);
        writer.WriteString("asserts", value.Asserts);
        writer.WriteEndObject();
    }
}

/// <summary>Vocabulary for <see cref="ConsumerClaim.Kind"/>; a closed set so consumers can branch on it.</summary>
public static class ConsumerClaimKinds
{
    /// <summary>A rule you must satisfy when authoring, or the server rejects the submission.</summary>
    public const string Constraint = "constraint";

    /// <summary>What the server does that you cannot see from the contract shapes.</summary>
    public const string Behaviour = "behaviour";

    /// <summary>A fact about how something is represented on the wire or in storage.</summary>
    public const string Representation = "representation";
}

public static class ConsumerClaimStability
{
    /// <summary>Pinned and intended to hold; a change is a breaking change.</summary>
    public const string Stable = "stable";

    /// <summary>True today and pinned, but the behaviour is under review and may change deliberately.</summary>
    public const string Provisional = "provisional";
}

/// <summary>
/// Gates A and B (spec 150 FR-B2). Both directions of the claim/test link are checked, because each
/// direction fails differently: a claim whose pin vanished is an unchecked sentence, and a
/// <c>[ConsumerContract]</c> test naming no claim is knowledge that never reaches the consumer.
/// </summary>
/// <remarks>
/// The gate verifies the link, not the assertion — CI running the suites is what verifies the assertion.
/// That split is deliberate: this tool must not need to execute test assemblies, so it reads them as
/// metadata only, which is also what lets it see every suite in the tree rather than the ones some
/// aggregator project happened to reference.
/// </remarks>
public sealed class ConsumerClaimsGate(Diagnostics diagnostics)
{
    private TestSourceIndex _sources = null!;

    public int Run(string repoRoot, string configuration)
    {
        _sources = new TestSourceIndex(repoRoot);

        var claimsPath = Path.Combine(repoRoot, "docs", "consumer-guide", "claims.json");
        if (!File.Exists(claimsPath))
        {
            diagnostics.Error(claimsPath, "ELSACT020", "docs/consumer-guide/claims.json is missing.");
            return 1;
        }

        var claimSet = JsonSerializer.Deserialize<ConsumerClaimSet>(File.ReadAllBytes(claimsPath), ReadOptions)
                       ?? throw new UsageException($"'{claimsPath}' did not deserialize into a claim set.");

        var pins = DiscoverPins(repoRoot, configuration);
        var claimIds = claimSet.Claims.Select(claim => claim.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var claim in claimSet.Claims)
            CheckClaim(claimsPath, claim, pins);

        // Gate B: a pin naming no claim.
        foreach (var pin in pins.Where(pin => !claimIds.Contains(pin.ClaimId)).OrderBy(pin => pin.Method, StringComparer.Ordinal))
        {
            diagnostics.Error(pin.AssemblyPath, "ELSACT022",
                $"Test '{pin.Method}' pins claim '{pin.ClaimId}', which docs/consumer-guide/claims.json does not publish.");
        }

        if (diagnostics.HasErrors)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Consumer claims and their pinning tests disagree. Either publish the claim, re-pin it, or retire it.");
            return 1;
        }

        Console.WriteLine($"{claimSet.Claims.Count} consumer claims, {pins.Count} pins; every claim is pinned and every pin is published.");
        return 0;
    }

    private void CheckClaim(string claimsPath, ConsumerClaim claim, IReadOnlyList<ClaimPin> pins)
    {
        if (claim.Tests.Count == 0)
        {
            // Gate A, the case that matters most: an unpinned sentence is a sentence nobody is checking.
            diagnostics.Error(claimsPath, "ELSACT021", $"Claim '{claim.Id}' names no pinning test.");
            return;
        }

        foreach (var reference in claim.Tests)
        {
            // [ConsumerContract] is AllowMultiple: one test can pin more than one claim, which is right when
            // a single behaviour is the evidence for two separate statements. Match against ALL of that
            // test's pins — looking only at the first rejected every claim but one.
            var pinned = pins
                .Where(candidate => string.Equals(candidate.Method, reference.Test, StringComparison.Ordinal))
                .Select(candidate => candidate.ClaimId)
                .ToArray();

            if (pinned.Length == 0)
            {
                diagnostics.Error(claimsPath, "ELSACT021",
                    $"Claim '{claim.Id}' names test '{reference.Test}', which does not exist or does not carry [ConsumerContract].");
                continue;
            }

            if (!pinned.Contains(claim.Id, StringComparer.Ordinal))
            {
                diagnostics.Error(claimsPath, "ELSACT021",
                    $"Claim '{claim.Id}' names test '{reference.Test}', but that test pins {string.Join(", ", pinned.Select(id => $"'{id}'"))}.");
                continue;
            }

            CheckAssertion(claimsPath, claim, reference);
        }
    }

    /// <summary>Gate A2: the quoted assertion must actually be in the named test's body.</summary>
    private void CheckAssertion(string claimsPath, ConsumerClaim claim, ClaimPinReference reference)
    {
        var body = _sources.MethodBody(reference.Test);
        if (body is null)
        {
            diagnostics.Error(claimsPath, "ELSACT023",
                $"Claim '{claim.Id}': the source of test '{reference.Test}' could not be located, so its " +
                "assertion cannot be verified.");
            return;
        }

        // Whitespace-insensitive: an assertion wrapped across lines by a formatter is the same assertion.
        if (!Collapse(body).Contains(Collapse(reference.Asserts), StringComparison.Ordinal))
        {
            diagnostics.Error(claimsPath, "ELSACT023",
                $"Claim '{claim.Id}' rests on assertion `{reference.Asserts}`, which is not in " +
                $"'{reference.Test}'. Either the assertion moved, or the claim is resting on something the " +
                "test does not actually check.");
        }
    }

    private static string Collapse(string text) => string.Concat(text.Where(character => !char.IsWhiteSpace(character)));

    /// <summary>Every <c>[ConsumerContract]</c>-marked method in every built test assembly, read as metadata.</summary>
    private static IReadOnlyList<ClaimPin> DiscoverPins(string repoRoot, string configuration)
    {
        var pins = new List<ClaimPin>();
        var testsRoot = Path.Combine(repoRoot, "tests");
        if (!Directory.Exists(testsRoot))
            return pins;

        foreach (var projectFile in Directory.EnumerateFiles(testsRoot, "*.csproj", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
            var assemblyPath = ContractsMerge.FindBuiltAssembly(projectFile, assemblyName, configuration);
            if (assemblyPath is not null)
                pins.AddRange(ClaimPinReader.Read(assemblyPath));
        }

        return pins.OrderBy(pin => pin.Method, StringComparer.Ordinal).ToArray();
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record ClaimPin(string ClaimId, string Method, string AssemblyPath);
