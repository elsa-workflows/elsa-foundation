using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Pure material qualification only; no publication, binding capability, model input or Message delivery.</summary>
public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public void Explicit_Message_inventory_covers_start_and_receive_only_interest_before_any_work_exists()
    {
        var material = MessageMaterial();
        var definition = BpmnMessageMaterial.ReadChecked(material);
        var result = new BpmnMessageMaterialInterestProvider().Describe(new(material, "physical-incarnation"));
        Assert.True(result.IsRecognized); Assert.Equal(2, result.Declarations.Count);
        Assert.Single(result.Declarations, item => item.CanStartWorkflow && item.MessageNames.SequenceEqual(new[] { "open" }));
        Assert.Single(result.Declarations, item => !item.CanStartWorkflow && item.MessageNames.SequenceEqual(new[] { "finish" }));
        Assert.Equal(new TypeReference("String"), Assert.Single(material.InputContract.Inputs).Type);
        Assert.True(Assert.Single(material.InputContract.Inputs).IsRequired);
        Assert.Equal("CorrelationKey", definition.CorrelationInputName);
        Assert.Empty(material.Dependencies); Assert.Empty(material.Outputs);
    }

    [Fact]
    public void Receive_only_profile_has_interest_with_plain_start_but_no_Message_start_declaration()
    {
        var material = MessageMaterial(messageStart: false);
        var result = new BpmnMessageMaterialInterestProvider().Describe(new(material, "physical-incarnation"));
        Assert.False(Assert.Single(result.Declarations).CanStartWorkflow);
        Assert.Equal("finish", Assert.Single(Assert.Single(result.Declarations).MessageNames));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("orphan")]
    [InlineData("wrong-name")]
    [InlineData("wrong-role")]
    [InlineData("wrong-work")]
    [InlineData("signal")]
    [InlineData("user-task")]
    public void New_profile_requires_exact_complete_Message_binding_coverage(string variant)
    {
        var process = MessageProcess(catchType: variant == "user-task" ? BpmnElementTypes.UserTask : BpmnElementTypes.IntermediateCatchEvent,
            eventType: variant == "signal" ? BpmnEventDefinitionTypes.Signal : BpmnEventDefinitionTypes.Message);
        var bindings = MessageBindings().ToList();
        if (variant == "missing") bindings.RemoveAt(1);
        if (variant == "orphan") bindings.Add(new("unreachable", "orphan-work", BpmnMessageBinding.Catch, "extra", BpmnMessageBinding.CapturedKey, new("Json")));
        if (variant == "wrong-name") bindings[1] = new("receive", "receive-work", BpmnMessageBinding.Catch, "Finish", BpmnMessageBinding.CapturedKey, new("Json"));
        if (variant == "wrong-role") bindings[1] = new("receive", null, BpmnMessageBinding.Start, "finish", BpmnMessageBinding.AcceptedKey, new("Json"));
        if (variant == "wrong-work") bindings[1] = new("receive", "another-work", BpmnMessageBinding.Catch, "finish", BpmnMessageBinding.CapturedKey, new("Json"));
        Assert.Throws<BpmnModelValidationException>(() => BpmnMessageMaterial.Create(process, "CorrelationKey", bindings, MessageLabels()));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("start-work")]
    [InlineData("key-source")]
    [InlineData("blank-name")]
    [InlineData("byte-bound")]
    [InlineData("payload-shape")]
    [InlineData("invalid-utf8")]
    public void Fixed_binding_requires_portable_name_exact_role_key_source_and_typed_contract(string variant)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BpmnMessageBinding("start", variant == "start-work" ? "fake-work" : null,
            variant == "role" ? "future" : BpmnMessageBinding.Start,
            variant == "blank-name" ? " " : variant == "byte-bound" ? new string('é', 129) : variant == "invalid-utf8" ? new string((char)0xD800, 1) : "open",
            variant == "key-source" ? BpmnMessageBinding.CapturedKey : BpmnMessageBinding.AcceptedKey,
            new("Json", variant == "payload-shape" ? (CollectionKind)999 : CollectionKind.Single)));
    }

    [Fact]
    public void Binding_snapshots_detach_schema_and_reject_ambiguous_physical_work_inventory()
    {
        using var schema = JsonDocument.Parse("{\"type\":\"object\"}");
        var binding = new BpmnMessageBinding("receive", "receive-work", BpmnMessageBinding.Catch, "finish",
            BpmnMessageBinding.CapturedKey, new("Json", schema: schema.RootElement, schemaVersion: 1));
        var bindings = new List<BpmnMessageBinding> { MessageBindings()[0], binding };
        var material = BpmnMessageMaterial.Create(MessageProcess(), "CorrelationKey", bindings, MessageLabels());
        bindings.Clear(); schema.Dispose();
        var retained = BpmnMessageMaterial.ReadChecked(material).Bindings.Single(item => item.ElementId == "receive");
        Assert.Equal("object", retained.PayloadType.Schema!.Value.GetProperty("type").GetString());
        Assert.Equal(1, retained.PayloadType.SchemaVersion);
        Assert.Throws<ArgumentException>(() => BpmnMessageMaterial.Create(MessageProcess(), "CorrelationKey",
            [binding, new("other", "receive-work", BpmnMessageBinding.Catch, "finish", BpmnMessageBinding.CapturedKey, new("Json"))], MessageLabels()));
    }

    [Fact]
    public void Finite_inventory_bound_refuses_excess_receivers_without_clipping()
    {
        var bindings = Enumerable.Range(0, 1025).Select(index => new BpmnMessageBinding("element-" + index, "work-" + index,
            BpmnMessageBinding.Catch, "finish", BpmnMessageBinding.CapturedKey, new("Json"))).ToArray();
        Assert.Throws<ArgumentException>(() => BpmnMessageMaterial.Create(MessageProcess(), "CorrelationKey", bindings, MessageLabels()));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("string-version")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("owner")]
    [InlineData("nested-case-alias")]
    [InlineData("nested-unknown")]
    [InlineData("element-case-alias")]
    public void New_owned_codec_rejects_future_ambiguous_or_unsupported_documents(string variant)
    {
        var definition = MessageMaterial().Definition;
        if (variant == "duplicate-field")
        {
            using var duplicate = JsonDocument.Parse(definition.GetRawText().Replace("\"formatVersion\":7", "\"formatVersion\":7,\"formatVersion\":7", StringComparison.Ordinal));
            Assert.Throws<BpmnModelValidationException>(() => BpmnMessageMaterial.Decode(duplicate.RootElement)); return;
        }
        var node = JsonNode.Parse(definition.GetRawText())!.AsObject();
        if (variant == "version") node["formatVersion"] = 8;
        if (variant == "string-version") node["formatVersion"] = "7";
        if (variant == "unknown-field") node["future"] = true;
        if (variant == "owner") node["ownerPolicy"] = "u16:" + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("material-definition"));
        if (variant == "nested-case-alias") node["process"]!["Elements"] = node["process"]!["elements"]!.DeepClone();
        if (variant == "nested-unknown") node["process"]!["unrecognizedNestedBehavior"] = true;
        if (variant == "element-case-alias") node["process"]!["elements"]![0]!["ElementId"] = node["process"]!["elements"]![0]!["elementId"]!.DeepClone();
        Assert.Throws<BpmnModelValidationException>(() => BpmnMessageMaterial.Decode(JsonSerializer.SerializeToElement(node)));
    }

    [Fact]
    public void Changed_key_payload_or_fixed_name_is_hash_covered_while_source_carrier_labels_do_not_change_behavior()
    {
        var original = MessageMaterial();
        var payloadBindings = MessageBindings(); payloadBindings[1] = new("receive", "receive-work", BpmnMessageBinding.Catch,
            "finish", BpmnMessageBinding.CapturedKey, new("String", CollectionKind.Array));
        var payload = BpmnMessageMaterial.Create(MessageProcess(), "CorrelationKey", payloadBindings, MessageLabels());
        var key = BpmnMessageMaterial.Create(MessageProcess(), "OtherKey", MessageBindings(), MessageLabels());
        var name = BpmnMessageMaterial.Create(MessageProcess(catchName: "Finish"), "CorrelationKey",
            [MessageBindings()[0], new("receive", "receive-work", BpmnMessageBinding.Catch, "Finish", BpmnMessageBinding.CapturedKey, new("Json"))], MessageLabels());
        Assert.NotEqual(original.Identity.ArtifactHash, payload.Identity.ArtifactHash);
        Assert.NotEqual(original.Identity.ArtifactHash, key.Identity.ArtifactHash);
        Assert.NotEqual(original.Identity.ArtifactHash, name.Identity.ArtifactHash);
        var republished = BpmnMessageMaterial.Create(MessageProcess(), "CorrelationKey", MessageBindings(),
            MessageLabels() with { DefinitionId = "different-enclosing-definition", DefinitionVersionId = "another-version" });
        Assert.Equal(original.Identity.ArtifactHash, republished.Identity.ArtifactHash);
        Assert.Equal(original.Definition.GetRawText(), republished.Definition.GetRawText());
    }

    [Fact]
    public void Opaque_element_and_work_addresses_round_trip_losslessly_and_names_remain_exact()
    {
        var element = "receive\0" + new string((char)0xD800, 1); var work = "work:" + new string((char)0xDC00, 1);
        var material = BpmnMessageMaterial.Create(MessageProcess(catchElement: element, catchWork: work), "CorrelationKey",
            [MessageBindings()[0], new(element, work, BpmnMessageBinding.Catch, "finish", BpmnMessageBinding.CapturedKey, new("Json"))], MessageLabels());
        var binding = BpmnMessageMaterial.ReadChecked(material).Bindings.Single(item => item.Role == BpmnMessageBinding.Catch);
        Assert.Equal(element, binding.ElementId); Assert.Equal(work, binding.BindingId);
        Assert.Equal(binding.Declaration().ReceiverAddress, new BpmnMessageMaterialInterestProvider().Describe(new(material, "incarnation"))
            .Declarations.Single(item => !item.CanStartWorkflow).ReceiverAddress);
    }

    [Fact]
    public void Checked_material_rejects_incoherent_hash_or_input_contract_and_unknown_profile_is_not_claimed()
    {
        var good = MessageMaterial();
        var hash = new RuntimeModelMaterial(1, good.Identity with { ArtifactHash = "other" }, good.Protocol, good.Profile,
            good.Definition, good.InputContract, [], [], []);
        Assert.Throws<BpmnModelValidationException>(() => BpmnMessageMaterial.ReadChecked(hash));
        var wrongInput = new RuntimeModelMaterial(1, good.Identity, good.Protocol, good.Profile, good.Definition, new(1, []), [], [], []);
        wrongInput = new(1, good.Identity with { ArtifactHash = wrongInput.ComputeContentHash() }, good.Protocol, good.Profile,
            good.Definition, wrongInput.InputContract, [], [], []);
        Assert.Throws<BpmnModelValidationException>(() => BpmnMessageMaterial.ReadChecked(wrongInput));
        var unknown = new RuntimeModelMaterial(1, good.Identity, good.Protocol, "future-profile", good.Definition, good.InputContract, [], [], []);
        Assert.False(new BpmnMessageMaterialInterestProvider().Describe(new(unknown, "incarnation")).IsRecognized);
    }

    [Fact]
    public void Closed_initial_profile_refuses_native_worker_dependencies_instead_of_enrolling_them()
    {
        var good = MessageMaterial();
        var dependent = new RuntimeModelMaterial(1, good.Identity, good.Protocol, good.Profile, good.Definition,
            good.InputContract, [], [new("child-work", MessageLabels())], []);
        dependent = new(1, good.Identity with { ArtifactHash = dependent.ComputeContentHash() }, good.Protocol, good.Profile,
            good.Definition, good.InputContract, [], dependent.Dependencies, []);
        Assert.Throws<BpmnModelValidationException>(() => BpmnMessageMaterial.ReadChecked(dependent));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Legacy_formats_keep_their_public_wire_and_hash_and_contribute_no_durable_Message_interest(int format)
    {
        var material = LegacyMessageInterestMaterial(format); var before = material.Definition.GetRawText(); var hash = material.ComputeContentHash();
        var legacy = material.Definition.Deserialize<BpmnModelDefinition>(Web)!;
        Assert.Equal(format, legacy.FormatVersion);
        Assert.Equal(before, JsonSerializer.SerializeToElement(legacy, Web).GetRawText());
        var result = new BpmnMessageMaterialInterestProvider().Describe(new(material, "incarnation"));
        Assert.True(result.IsRecognized); Assert.Empty(result.Declarations);
        Assert.Equal(before, material.Definition.GetRawText()); Assert.Equal(hash, material.ComputeContentHash());
        Assert.False(material.Definition.TryGetProperty("ownerPolicy", out _));
    }

    [Fact]
    public void Existing_signal_profile_still_refuses_a_Message_keyword_without_explicit_opt_in()
    {
        Assert.Throws<NotSupportedException>(() => new BpmnMessageMaterialInterestProvider().Describe(new(CheckedLegacyInterestMaterial(SignalMaterial("message")), "incarnation")));
    }

    [Fact]
    public void Legacy_material_hash_is_independently_checked_before_recognized_empty_interest()
    {
        var canonical = LegacyMessageInterestMaterial(1);
        var corrupt = new RuntimeModelMaterial(1, canonical.Identity with { ArtifactHash = "wrong-legacy-hash" }, canonical.Protocol,
            canonical.Profile, canonical.Definition, canonical.InputContract, canonical.Outputs, canonical.Dependencies, canonical.RuntimeRequirements);
        var error = Assert.Throws<NotSupportedException>(() => new BpmnMessageMaterialInterestProvider().Describe(new(corrupt, "incarnation")));
        Assert.Null(error.InnerException);
        Assert.Empty(new BpmnMessageMaterialInterestProvider().Describe(new(canonical, "incarnation")).Declarations);
    }

    [Fact]
    public void Private_domain_graph_error_is_sanitized_through_the_actual_Runtime_extractor_boundary()
    {
        const string canary = "private-graph-element-and-name-canary";
        var process = new BpmnProcessDefinition("private-process-canary", Elements:
            [new("start", BpmnElementTypes.StartEvent), new(canary, BpmnElementTypes.IntermediateThrowEvent,
                eventDefinitions: [new(BpmnEventDefinitionTypes.Message, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = canary })]),
                new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("first", "start", canary), new("last", canary, "end")]);
        var seed = new RuntimeModelMaterial(1, MessageLabels(), new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.Profile,
            JsonSerializer.SerializeToElement(new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, Web), []), Web), new(1, []), [], [], []);
        var material = new RuntimeExecutionMaterial(CheckedLegacyInterestMaterial(seed), "incarnation");
        var provider = new BpmnMessageMaterialInterestProvider();
        var rawDomain = Assert.Throws<BpmnModelValidationException>(() => BpmnDirectExecutionDriver.ValidateLegacyInterestMaterial(material.Model!));
        Assert.Contains(canary, rawDomain.Message, StringComparison.Ordinal);
        var direct = Assert.Throws<NotSupportedException>(() => provider.Describe(material));
        Assert.Null(direct.InnerException); Assert.DoesNotContain(canary, direct.ToString(), StringComparison.Ordinal);
        // Invoke the real compiled Runtime internal type without broadening product IVT/public API for this test.
        var extractorType = typeof(RuntimeCheckpointCommitter).Assembly.GetType("Elsa.Workflows.Runtime.Messaging.MessageMaterialInterestExtractor", true)!;
        var extractor = Activator.CreateInstance(extractorType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { new IMessageMaterialInterestProvider[] { provider } }, null)!;
        var method = extractorType.GetMethod("Extract", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var invocation = Assert.Throws<TargetInvocationException>(() => method.Invoke(extractor, [material]));
        var unavailable = Assert.IsType<RuntimeSchedulerWorkUnavailableException>(invocation.InnerException);
        Assert.Equal("message.interest.unavailable", unavailable.Code); Assert.Null(unavailable.InnerException);
        Assert.DoesNotContain(canary, unavailable.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-process-canary", unavailable.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Current_runtime_driver_still_refuses_new_Message_profile_without_real_delivery_participation()
    {
        Assert.Throws<BpmnModelValidationException>(() => BpmnDirectExecutionDriver.ValidateLegacyInterestMaterial(MessageMaterial()));
    }

    private static RuntimeModelMaterial MessageMaterial(bool messageStart = true) => BpmnMessageMaterial.Create(
        MessageProcess(messageStart), "CorrelationKey", messageStart ? MessageBindings() : [MessageBindings()[1]], MessageLabels());
    private static WorkflowExecutableIdentity MessageLabels() => new("seed", "authored-definition", "authored-version", "1.0.0", "seed");
    private static BpmnMessageBinding[] MessageBindings() =>
        [new("start", null, BpmnMessageBinding.Start, "open", BpmnMessageBinding.AcceptedKey, new("Json")),
            new("receive", "receive-work", BpmnMessageBinding.Catch, "finish", BpmnMessageBinding.CapturedKey, new("Json"))];
    private static BpmnProcessDefinition MessageProcess(bool messageStart = true, string catchType = BpmnElementTypes.IntermediateCatchEvent,
        string eventType = BpmnEventDefinitionTypes.Message, string catchName = "finish", string catchElement = "receive", string catchWork = "receive-work") => new("messages",
        Elements: [new("start", BpmnElementTypes.StartEvent, eventDefinitions: messageStart ? [new(BpmnEventDefinitionTypes.Message,
                new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = "open" })] : []),
            new(catchElement, catchType, bindingRef: catchWork, eventDefinitions: [new(eventType,
                new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = catchName })]), new("end", BpmnElementTypes.EndEvent)],
        SequenceFlows: [new("to-receive", "start", catchElement), new("to-end", catchElement, "end")]);
    private static RuntimeModelMaterial LegacyMessageInterestMaterial(int format)
    {
        var material = format switch
        {
            1 => Material(), 2 => ServiceErrorMaterial("business"), 4 => TimedHumanMaterial(),
            5 => SignalMaterial(), 6 => ClaimMaterial(), _ => LegacyPolicyInterestMaterial()
        };
        return CheckedLegacyInterestMaterial(material);
    }
    private static RuntimeModelMaterial LegacyPolicyInterestMaterial()
    {
        var seed = ServiceErrorMaterial("business"); var definition = seed.Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var service = Assert.Single(definition.ServiceBindings!);
        var policy = new BpmnModelDefinition(3, definition.Process, [],
            [new(service.BindingId, service.Executable, service.BusinessErrorCode, new(1, BpmnServiceFaultDisposition.HoldForRetry, 2))]);
        return new(1, seed.Identity, seed.Protocol, seed.Profile, JsonSerializer.SerializeToElement(policy, Web), seed.InputContract, [], seed.Dependencies, []);
    }
    private static RuntimeModelMaterial CheckedLegacyInterestMaterial(RuntimeModelMaterial material) => new(1,
        material.Identity with { ArtifactHash = material.ComputeContentHash() }, material.Protocol, material.Profile, material.Definition,
        material.InputContract, material.Outputs, material.Dependencies, material.RuntimeRequirements);
}
