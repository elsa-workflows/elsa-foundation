using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Mechanics fixtures only: these are not real Message activity/model integration witnesses.</summary>
public sealed class MessageMaterialInterestTests
{
    [Fact]
    public void Only_a_recognized_empty_result_proves_no_material_receivers()
    {
        var material = Native();
        Unavailable(() => new MessageMaterialInterestExtractor([]).Extract(material));
        Unavailable(() => new MessageMaterialInterestExtractor([new Provider("declined", null,
            _ => MessageMaterialInterestResult.NotRecognized)]).Extract(material));
        var knownEmpty = new MessageMaterialInterestExtractor([new Provider("checked", null,
            _ => MessageMaterialInterestResult.Recognized([]))]).Extract(material);
        Assert.True(knownEmpty.IsRecognized);
        Assert.Empty(knownEmpty.Declarations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_and_exact_model_selectors_invoke_only_the_matching_family(bool model)
    {
        var native = Fixed("native", null, "native-address");
        var selectedModel = Fixed("model", new("fixture.protocol", 2), "model-address");
        var oldVersion = Fixed("older", new("fixture.protocol", 1), "wrong-version");
        var differentCase = Fixed("case", new("Fixture.Protocol", 2), "wrong-key");
        var result = new MessageMaterialInterestExtractor([oldVersion, differentCase, native, selectedModel])
            .Extract(model ? Model(new("fixture.protocol", 2)) : Native());
        Assert.Equal(model ? "model-address" : "native-address", Assert.Single(result.Declarations).ReceiverAddress);
        Assert.Equal(model ? 0 : 1, native.Calls);
        Assert.Equal(model ? 1 : 0, selectedModel.Calls);
        Assert.Equal(0, oldVersion.Calls);
        Assert.Equal(0, differentCase.Calls);
    }

    [Fact]
    public void Protocol_version_mismatch_is_unavailable_without_calling_a_provider_for_other_material()
    {
        var old = Fixed("old", new("fixture.protocol", 1));
        Unavailable(() => new MessageMaterialInterestExtractor([old]).Extract(Model(new("fixture.protocol", 2))));
        Assert.Equal(0, old.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Competing_recognized_results_refuse_even_when_both_are_empty_and_order_changes(bool reverse)
    {
        var first = new Provider("first", null, _ => MessageMaterialInterestResult.Recognized([]));
        var second = new Provider("second", null, _ => MessageMaterialInterestResult.Recognized([]));
        Unavailable(() => new MessageMaterialInterestExtractor(reverse ? [second, first] : [first, second]).Extract(Native()));
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public void Unsupported_profile_strategy_can_decline_without_shadowing_the_checked_strategy()
    {
        var unsupported = new Provider("a.unsupported", new("fixture.protocol", 2),
            _ => MessageMaterialInterestResult.NotRecognized);
        var checkedProfile = Fixed("z.checked", new("fixture.protocol", 2));
        var result = new MessageMaterialInterestExtractor([checkedProfile, unsupported]).Extract(Model(new("fixture.protocol", 2)));
        Assert.Single(result.Declarations);
        Assert.Equal(1, unsupported.Calls);
        Assert.Equal(1, checkedProfile.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("argument")]
    [InlineData("format")]
    [InlineData("invalid")]
    [InlineData("unsupported")]
    [InlineData("json")]
    [InlineData("unavailable")]
    public void Unreadable_provider_result_is_unavailable_without_leaking_model_content(string failure)
    {
        var provider = new Provider("checked", null, _ => failure switch
        {
            "null" => null!,
            "argument" => throw new ArgumentException("private-material-canary"),
            "format" => throw new FormatException("private-material-canary"),
            "invalid" => throw new InvalidOperationException("private-material-canary"),
            "unsupported" => throw new NotSupportedException("private-material-canary"),
            "unavailable" => throw new RuntimeSchedulerWorkUnavailableException("private", "private-material-canary"),
            _ => throw new JsonException("private-material-canary")
        });
        var error = Unavailable(() => new MessageMaterialInterestExtractor([provider]).Extract(Native()));
        Assert.DoesNotContain("private-material-canary", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Extraction_retains_original_physical_material_without_enrolling_dependency_workers()
    {
        var material = Model(new("fixture.protocol", 2), [new("worker", Pin with { ArtifactId = "worker-artifact" })]);
        RuntimeExecutionMaterial? observed = null;
        var provider = new Provider("checked", material.Model!.Protocol, value =>
        {
            observed = value;
            return MessageMaterialInterestResult.Recognized([Declaration("owner-element", "orders")]);
        });
        var result = new MessageMaterialInterestExtractor([provider]).Extract(material);
        Assert.Same(material, observed);
        Assert.Equal(1, provider.Calls);
        Assert.Equal("owner-element", Assert.Single(result.Declarations).ReceiverAddress);
    }

    [Fact]
    public void Ordinary_node_metadata_and_unrelated_model_JSON_are_not_inferred_as_declarations()
    {
        var provider = new Provider("checked", null, _ => MessageMaterialInterestResult.Recognized([]));
        Assert.Empty(new MessageMaterialInterestExtractor([provider]).Extract(Native(
            new Dictionary<string, string> { ["messageName"] = "private-metadata", ["declaredMessageNames"] = "[\"orders\"]" })).Declarations);
        var protocol = new RuntimeExecutionProtocol("fixture.protocol", 2);
        provider.ModelProtocol = protocol;
        Assert.Empty(new MessageMaterialInterestExtractor([provider]).Extract(Model(protocol,
            definition: "{\"messageName\":\"orders\",\"declaredMessageNames\":[\"orders\"]}")).Declarations);
    }

    [Fact]
    public void Provider_registration_collection_and_selectors_are_snapshotted()
    {
        var provider = Fixed("checked", new("fixture.protocol", 2));
        var providers = new List<IMessageMaterialInterestProvider> { provider };
        var extractor = new MessageMaterialInterestExtractor(providers);
        providers.Clear();
        provider.ModelProtocol = new("fixture.protocol", 3);
        Assert.Single(extractor.Extract(Model(new("fixture.protocol", 2))).Declarations);
        Unavailable(() => extractor.Extract(Model(new("fixture.protocol", 3))));
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("unicode")]
    [InlineData("duplicate")]
    [InlineData("null-entry")]
    public void Invalid_or_ambiguous_provider_identity_refuses_composition_safely(string kind)
    {
        var id = kind == "blank" ? " \t" : kind == "unicode" ? "private-provider-canary-\ud800" : "same";
        IMessageMaterialInterestProvider[] providers = kind switch
        {
            "null-entry" => [null!],
            "duplicate" => [Fixed(id, null), Fixed(id, new("fixture.protocol", 1))],
            _ => [Fixed(id, null)]
        };
        var error = Assert.ThrowsAny<ArgumentException>(() => new MessageMaterialInterestExtractor(providers));
        Assert.DoesNotContain("private-provider-canary", error.ToString());
    }

    [Fact]
    public void Collections_detach_and_retain_exact_ordinal_names_and_addresses()
    {
        var names = new List<string> { "orders", "Orders", " orders ", "0123", "123", "é", "e\u0301" };
        var declaration = new MessageMaterialInterestDeclaration(" element ", names, canStartWorkflow: true);
        var declarations = new List<MessageMaterialInterestDeclaration> { declaration, Declaration("element", "other") };
        var result = MessageMaterialInterestResult.Recognized(declarations);
        names.Clear(); declarations.Clear();
        Assert.Equal(7, declaration.MessageNames.Count);
        Assert.Contains(" orders ", declaration.MessageNames);
        Assert.Contains("é", declaration.MessageNames);
        Assert.Contains("e\u0301", declaration.MessageNames);
        Assert.Equal([" element ", "element"], result.Declarations.Select(item => item.ReceiverAddress));
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)declaration.MessageNames).Add("changed"));
        Assert.Throws<NotSupportedException>(() => ((ICollection<MessageMaterialInterestDeclaration>)result.Declarations).Clear());
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("two-byte")]
    [InlineData("four-byte")]
    public void Name_byte_limit_accepts_the_exact_UTF8_boundary_and_refuses_one_more_byte(string encoding)
    {
        var exact = encoding == "ascii" ? new string('a', 256) : encoding == "two-byte" ? new string('é', 128) : string.Concat(Enumerable.Repeat("😀", 64));
        Assert.Equal(exact, Assert.Single(Declaration("receiver", exact).MessageNames));
        Assert.Throws<ArgumentException>(() => Declaration("receiver", exact + "a"));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("blank")]
    [InlineData("unicode")]
    [InlineData("duplicate")]
    [InlineData("count")]
    public void Invalid_name_sets_refuse_without_trimming_deduplicating_or_clipping(string kind)
    {
        string[] names = kind switch
        {
            "empty" => [], "null" => [null!], "blank" => ["\t\u2003"], "unicode" => ["private-name-canary-\ud800"],
            "duplicate" => ["orders", "orders"], _ => Enumerable.Range(0, 33).Select(i => "name-" + i).ToArray()
        };
        var error = Assert.Throws<ArgumentException>(() => new MessageMaterialInterestDeclaration("receiver", names));
        Assert.DoesNotContain("private-name-canary", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Material_pair_bound_accepts_the_complete_limit_and_refuses_additional_obligations()
    {
        var names = Enumerable.Range(0, 32).Select(i => "name-" + i).ToArray();
        var declarations = Enumerable.Range(0, 32).Select(i => new MessageMaterialInterestDeclaration("receiver-" + i, names)).ToList();
        var exact = MessageMaterialInterestResult.Recognized(declarations);
        Assert.Equal(1024, exact.Declarations.Sum(item => item.MessageNames.Count));
        declarations.Add(Declaration("additional", "orders"));
        Assert.Throws<ArgumentException>(() => MessageMaterialInterestResult.Recognized(declarations));
        Assert.Equal(1024, exact.Declarations.Sum(item => item.MessageNames.Count));
    }

    [Fact]
    public void Duplicate_receiver_addresses_refuse_even_with_different_names_or_start_roles()
    {
        Assert.Throws<ArgumentException>(() => MessageMaterialInterestResult.Recognized([
            Declaration("receiver", "orders"), new("receiver", ["payments"], canStartWorkflow: true)]));
    }

    [Fact]
    public void Null_shapes_invalid_addresses_and_oversized_addresses_refuse_safely()
    {
        Assert.Throws<ArgumentNullException>(() => new MessageMaterialInterestExtractor(null!));
        Assert.Throws<ArgumentNullException>(() => new MessageMaterialInterestExtractor([]).Extract(null!));
        Assert.Throws<ArgumentNullException>(() => new MessageMaterialInterestDeclaration("receiver", null!));
        Assert.Throws<ArgumentNullException>(() => MessageMaterialInterestResult.Recognized(null!));
        Assert.Throws<ArgumentNullException>(() => MessageMaterialInterestResult.Recognized([null!]));
        foreach (var address in new[] { null, "", "\t", "private-address-canary-" + new string('x', 1025) })
        {
            var error = Assert.ThrowsAny<ArgumentException>(() => Declaration(address!, "orders"));
            Assert.DoesNotContain("private-address-canary", error.ToString());
        }
        Assert.Single(MessageMaterialInterestResult.Recognized([Declaration(new string('x', 1024), "orders")]).Declarations);
    }

    [Fact]
    public void Opaque_native_node_addresses_preserve_distinct_exact_UTF16_code_units()
    {
        // Actual canonical material nodes accept these opaque IDs. This fixture describes their exact node
        // addresses; it is an extraction-mechanics witness, not a real Message provider or delivery witness.
        string[] addresses = [" node-\ud800 ", " node-\ud801 ", " node-\ufffd ", "node-\0", " node "];
        var declarations = new List<MessageMaterialInterestDeclaration>();
        foreach (var address in addresses)
        {
            var material = Native(nodeId: address);
            var provider = new Provider("checked", null, value => MessageMaterialInterestResult.Recognized(
                [Declaration(value.NativeWorkflow!.RootActivity.ExecutableNodeId, "orders")]));
            var declaration = Assert.Single(new MessageMaterialInterestExtractor([provider]).Extract(material).Declarations);
            Assert.Equal(address, declaration.ReceiverAddress);
            declarations.Add(declaration);
        }
        var result = MessageMaterialInterestResult.Recognized(declarations);
        Assert.Equal(addresses.Order(StringComparer.Ordinal), result.Declarations.Select(item => item.ReceiverAddress));
        Assert.Equal(addresses.Length, result.Declarations.Count);
        Assert.Throws<ArgumentException>(() => MessageMaterialInterestResult.Recognized([declarations[0], declarations[0]]));
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("two-byte")]
    [InlineData("four-byte")]
    [InlineData("unpaired")]
    public void Receiver_address_bound_counts_opaque_UTF16_code_units_without_transport_encoding(string kind)
    {
        var exact = kind switch
        {
            "ascii" => new string('a', 1024),
            "two-byte" => new string('é', 1024),
            "four-byte" => string.Concat(Enumerable.Repeat("😀", 512)),
            _ => new string('\ud800', 1024)
        };
        Assert.Equal(1024, exact.Length);
        Assert.Equal(exact, Declaration(exact, "orders").ReceiverAddress);
        Assert.Throws<ArgumentException>(() => Declaration(exact + "a", "orders"));
    }

    [Fact]
    public void Ordinary_interpolation_does_not_disclose_declaration_content()
    {
        var declaration = Declaration("private-address-canary", "private-name-canary");
        var result = MessageMaterialInterestResult.Recognized([declaration]);
        Assert.DoesNotContain("private-", $"{declaration} {result} {MessageMaterialInterestResult.NotRecognized}");
    }

    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "sha256:fixture");
    private static MessageMaterialInterestDeclaration Declaration(string address, string name) => new(address, [name]);
    private static Provider Fixed(string id, RuntimeExecutionProtocol? protocol, string address = "receiver") =>
        new(id, protocol, _ => MessageMaterialInterestResult.Recognized([Declaration(address, "orders")]));

    private static RuntimeSchedulerWorkUnavailableException Unavailable(Action action)
    {
        var error = Assert.Throws<RuntimeSchedulerWorkUnavailableException>(action);
        Assert.Equal("message.interest.unavailable", error.Code);
        Assert.Null(error.InnerException);
        return error;
    }

    private static RuntimeExecutionMaterial Native(IReadOnlyDictionary<string, string>? metadata = null, string nodeId = "root")
    {
        var node = new ExecutableNode(nodeId, nodeId, "Fixture.Activity", "1", "fixture.consumer",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), metadata ?? new Dictionary<string, string>());
        return new(new WorkflowExecutable(Pin, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference), "incarnation");
    }

    private static RuntimeExecutionMaterial Model(RuntimeExecutionProtocol protocol, IReadOnlyList<RuntimeModelDependency>? dependencies = null,
        string definition = "{}")
    {
        using var json = JsonDocument.Parse(definition);
        return new(new RuntimeModelMaterial(1, Pin, protocol, "fixture-profile", json.RootElement,
            new WorkflowExecutableInputContract(1, []), [], dependencies ?? [], []), "incarnation");
    }

    private sealed class Provider(string id, RuntimeExecutionProtocol? protocol, Func<RuntimeExecutionMaterial, MessageMaterialInterestResult> describe)
        : IMessageMaterialInterestProvider
    {
        public string ProviderId => id;
        public RuntimeExecutionProtocol? ModelProtocol { get; set; } = protocol;
        public int Calls { get; private set; }
        public MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material) { Calls++; return describe(material); }
    }
}
