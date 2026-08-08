using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Api.Services;
using Elsa.Contracts.Generator;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Agreement between separately published facts. Each test here exists because a consumer merge-gate
/// evaluation found two published statements that contradicted each other, or a load-bearing fact that
/// was published nowhere — failures the byte-level freshness check cannot see, because both files were
/// internally consistent and reproducible.
/// </summary>
public sealed class PublishedContractAgreementTests
{
    /// <summary>
    /// The catalog publishes <c>intrinsic.kind</c> for a client to echo into a submission; the submit
    /// schema constrains the same value to the wire spelling of <see cref="AuthoredWorkflowIntrinsicKind"/>.
    /// Publishing "Set" while the schema said "set" gave a consumer two spellings and no tiebreaker.
    /// </summary>
    [Fact]
    public void Intrinsic_kinds_are_published_in_the_submit_schema_wire_spelling()
    {
        var wireSpellings = Enum.GetNames<AuthoredWorkflowIntrinsicKind>()
            .Select(name => JsonSerializer.Serialize(Enum.Parse<AuthoredWorkflowIntrinsicKind>(name), WireOptions).Trim('"'))
            .ToHashSet(StringComparer.Ordinal);

        var published = new IntrinsicAuthoringDescriptorProvider().GetDescriptors()
            .Select(descriptor => descriptor.Intrinsic!.Kind)
            .ToArray();

        Assert.NotEmpty(published);
        Assert.All(published, kind => Assert.True(wireSpellings.Contains(kind),
            $"Published intrinsic kind '{kind}' is not a wire spelling of AuthoredWorkflowIntrinsicKind ({string.Join(", ", wireSpellings)}); a client echoing it into a submission would be rejected."));
    }

    /// <summary>
    /// An intrinsic's variable target is carried by the node's <c>intrinsic</c> block
    /// (<c>AuthoredWorkflowIntrinsic</c> exposes Kind/ValueType/Variable only) — never as an argument
    /// binding. The descriptor still lists it so a designer can render a variable picker, so the contract
    /// must say where it is authored or it reads as a required argument that does not exist.
    /// </summary>
    [Fact]
    public void Intrinsic_variable_targets_are_marked_as_intrinsic_block_authored()
    {
        var fragment = ProjectDesignApiFragment();

        var withVariableTarget = fragment.Intrinsics.Where(intrinsic => intrinsic.Intrinsic.VariableInputKey is not null).ToArray();
        Assert.NotEmpty(withVariableTarget);

        foreach (var intrinsic in withVariableTarget)
        {
            var variableInput = Assert.Single(intrinsic.Inputs, input => input.ReferenceKey == intrinsic.Intrinsic.VariableInputKey);
            Assert.Equal(InputAuthoringSites.IntrinsicBlock, variableInput.AuthoredVia);

            // Everything else on an intrinsic is a genuine argument binding.
            Assert.All(
                intrinsic.Inputs.Where(input => input.ReferenceKey != intrinsic.Intrinsic.VariableInputKey),
                input => Assert.Equal(InputAuthoringSites.Argument, input.AuthoredVia));
        }
    }

    /// <summary>
    /// Literal/Object/Input are accepted on every authored input but are not provider-registered, so a
    /// projection that only enumerated providers published neither — leaving a contracts-only consumer
    /// unable to author the expression type nearly every input uses.
    /// </summary>
    [Fact]
    public void Intrinsic_expression_types_are_published()
    {
        var published = ProjectFragment(typeof(IntrinsicExpressionDescriptors).Assembly.GetName().Name!)
            .Expressions!.Descriptors.Select(descriptor => descriptor.Type)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Literal", published);
        Assert.Contains("Object", published);
        Assert.Contains("Input", published);
    }

    /// <summary>
    /// An enum-typed input publishes its type and its default; without the member list the consumer must
    /// guess the rest. A measured guess ("blocking") was rejected at publish; another ("sync") silently
    /// changed response behaviour.
    /// </summary>
    [Fact]
    public void Enum_typed_inputs_publish_their_accepted_members_in_wire_spelling()
    {
        var responseMode = ProjectFragment("Elsa.Activities.Http").Activities
            .Single(activity => activity.ActivityTypeKey.EndsWith(".HttpEndpoint", StringComparison.Ordinal))
            .Inputs.Single(input => input.Name == "ResponseMode");

        Assert.NotNull(responseMode.EnumValues);
        Assert.Equal(["async", "sync"], responseMode.EnumValues);
        // The published default must itself be one of the published members.
        Assert.Contains(responseMode.DefaultValue!.Value.GetString()!, responseMode.EnumValues!);
    }

    [Fact]
    public void Every_published_enum_default_is_one_of_that_inputs_published_members()
    {
        var contractsRoot = FindContractsRoot();
        foreach (var fragmentPath in Directory.EnumerateFiles(Path.Combine(contractsRoot, "fragments"), "*.json"))
        {
            using var fragment = JsonDocument.Parse(File.ReadAllBytes(fragmentPath));
            if (!fragment.RootElement.TryGetProperty("activities", out var activities))
                continue;

            var defaultedEnumInputs = activities.EnumerateArray()
                .SelectMany(activity => activity.GetProperty("inputs").EnumerateArray())
                .Where(input => input.GetProperty("enumValues").ValueKind == JsonValueKind.Array &&
                                input.GetProperty("defaultValue").ValueKind == JsonValueKind.String);

            foreach (var input in defaultedEnumInputs)
            {
                var members = input.GetProperty("enumValues").EnumerateArray().Select(value => value.GetString()!).ToArray();
                Assert.True(members.Contains(input.GetProperty("defaultValue").GetString()!, StringComparer.Ordinal),
                    $"{Path.GetFileName(fragmentPath)}: input '{input.GetProperty("referenceKey").GetString()}' publishes a default that is not among its published members.");
            }
        }
    }

    private static readonly JsonSerializerOptions WireOptions = CreateWireOptions();

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static ContractFragment ProjectDesignApiFragment() => ProjectFragment("Elsa.Activities.Design.Api");

    private static ContractFragment ProjectFragment(string assemblyName)
    {
        TargetAssembly.AddProbeDirectory(AppContext.BaseDirectory);
        var diagnostics = new Diagnostics();
        var references = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").ToArray();
        var fragment = new FragmentProjector(diagnostics).Project(
            Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll"), references);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return fragment;
    }

    private static string FindContractsRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent!)
        {
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return Path.Combine(current.FullName, "docs", "contracts");
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
