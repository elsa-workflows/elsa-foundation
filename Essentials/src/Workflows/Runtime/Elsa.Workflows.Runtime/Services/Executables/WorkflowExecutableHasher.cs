using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// Computes the content-addressable identity of a compiled workflow executable: the deterministic SHA-256
/// <c>ArtifactHash</c> over a canonical rendering of the executable node tree, and the derived
/// <c>ArtifactId</c>. Extracted from <see cref="WorkflowExecutableCompiler"/> (#418) so hashing and
/// artifact-id formatting can change independently of activity-tree compilation.
/// </summary>
/// <remarks>
/// The canonical payload shape is wire-significant: any change here changes every artifact hash and id. The
/// characterization goldens pin both. The payload is <b>behavioral-only</b>: it covers the
/// canonical node tree (root node id plus the flattened, ordinally-ordered node renderings) and carries no
/// source identity, so equal hash ⇔ equal behavior in both directions and executables are content-addressed.
/// </remarks>
public sealed class WorkflowExecutableHasher : IWorkflowExecutableHasher
{
    private const string ArtifactHashPrefix = "sha256:";
    private const int ArtifactIdHashLength = 12;

    public string ComputeHash(ExecutableNode rootActivity)
    {
        return Hash(NodePayload(rootActivity));
    }

    public string ComputeHash(
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies,
        PersistenceMode? persistenceMode = null,
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables = null,
        IncidentStrategyReference? incidentStrategy = null) =>
        ComputeBehavioralHash(rootActivity, inputContract, dependencies, persistenceMode, workflowVariables,
            incidentStrategy);

    public bool MatchesHash(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        if (executable.InputContract is not { } contract)
            return false;

        var current = ComputeHash(executable.RootActivity, contract, executable.Dependencies,
            executable.PersistenceMode, executable.WorkflowVariables, executable.IncidentStrategy);
        return StringComparer.Ordinal.Equals(current, executable.Identity.ArtifactHash);
    }

    private static string ComputeBehavioralHash(
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies,
        PersistenceMode? persistenceMode,
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables,
        IncidentStrategyReference? incidentStrategy)
    {
        ArgumentNullException.ThrowIfNull(rootActivity);
        ArgumentNullException.ThrowIfNull(inputContract);
        ArgumentNullException.ThrowIfNull(dependencies);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteBehavioralPayload(
                writer,
                rootActivity,
                inputContract,
                dependencies,
                persistenceMode,
                workflowVariables,
                incidentStrategy ?? IncidentStrategyBuiltIns.FaultReference);

        return Hash(stream.ToArray());
    }

    private static string NodePayload(ExecutableNode rootActivity)
    {
        ArgumentNullException.ThrowIfNull(rootActivity);
        var nodes = FlattenExecutableActivities(rootActivity).ToArray();
        return string.Join(
            '\n',
            rootActivity.ExecutableNodeId,
            string.Join('|', nodes.OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal)
                .Select(FormatNode)));
    }

    private static string Hash(string payload)
    {
        return ArtifactHashPrefix + Elsa.Serialization.Core.JsonHashInput.HashFramed(Encoding.UTF8.GetBytes(payload));
    }

    private static string Hash(byte[] payload)
    {
        return ArtifactHashPrefix + Elsa.Serialization.Core.JsonHashInput.HashUtf8(payload);
    }

    public string CreateArtifactId(string artifactIdPrefix, string artifactHash)
    {
        if (!artifactHash.StartsWith(ArtifactHashPrefix, StringComparison.Ordinal) ||
            artifactHash.Length < ArtifactHashPrefix.Length + ArtifactIdHashLength)
            throw new ArgumentException($"Artifact hash '{artifactHash}' does not use the expected '{ArtifactHashPrefix}' format.", nameof(artifactHash));

        return $"{artifactIdPrefix}{artifactHash[ArtifactHashPrefix.Length..(ArtifactHashPrefix.Length + ArtifactIdHashLength)]}";
    }

    private static string FormatInputBinding(KeyValuePair<string, RuntimeInputBinding> input)
    {
        var metadata = string.Join(',', input.Value.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));

        var payload = input.Value.Source switch
        {
            RuntimeInputBindingSource.Literal when input.Value.Literal is not null => FormatEnvelope(input.Value.Literal),
            RuntimeInputBindingSource.WorkflowRequest =>
                $"request:{input.Value.WorkflowRequest?.MemberKey}:{input.Value.WorkflowRequest?.Path}",
            RuntimeInputBindingSource.VariableRead =>
                $"variable:{input.Value.Variable?.DeclaringScopeId}:{input.Value.Variable?.VariableKey}",
            RuntimeInputBindingSource.ActivityResult =>
                $"result:{input.Value.ActivityResult?.ProducerScopeId}:{input.Value.ActivityResult?.ProducerExecutableNodeId}:{input.Value.ActivityResult?.ProjectionKey}:{input.Value.ActivityResult?.IsOptional}",
            RuntimeInputBindingSource.Expression => FormatExpression(input.Value.Expression),
            RuntimeInputBindingSource.SecretRead => FormatSecret(input.Value.Secret),
            RuntimeInputBindingSource.WorkflowRequestMember =>
                $"request-member:{Framed(input.Value.WorkflowRequest!.MemberKey)}:{FormatSelection(input.Value.Selection!)}",
            RuntimeInputBindingSource.VariableMember =>
                $"variable-member:{Framed(input.Value.Variable!.DeclaringScopeId)}:{Framed(input.Value.Variable.VariableKey)}:{FormatSelection(input.Value.Selection!)}",
            RuntimeInputBindingSource.ActivityResultMember =>
                $"result-member:{Framed(input.Value.ActivityResult!.ProducerExecutableNodeId)}:{Framed(input.Value.ActivityResult.ProjectionKey)}:{input.Value.ActivityResult.IsOptional}:{FormatSelection(input.Value.Selection!)}",
            // Every source has an arm. A source this hasher does not know must not hash as an absent literal, where
            // two different bindings would share one identity.
            _ => throw new ArgumentOutOfRangeException(nameof(input), input.Value.Source,
                $"Input '{input.Key}' uses runtime input binding source '{input.Value.Source}', which the executable hasher does not render.")
        };

        var conversion = input.Value.ConversionPlan is null ? string.Empty : $":conversion={input.Value.ConversionPlan.Fingerprint}";
        return $"{input.Key}:{FormatType(input.Value.TargetType)}:{FormatPolicy(input.Value.EffectivePolicy)}={payload}[{metadata}]{conversion}";
    }

    // Every variable-length field of a member read is framed as {length}#{value}, so no member key, scope, producer,
    // projection or segment can contain a delimiter that makes two different reads render alike.
    private static string Framed(string? value) =>
        value is null ? "-" : $"{value.Length}#{value}";

    internal static string FormatSelection(RuntimeReadSelection selection) =>
        $"sel:{selection.SchemaVersion}:{selection.Mode}:{selection.Segments.Count}:{string.Concat(selection.Segments.Select(Framed))}" +
        $":{Framed(selection.SelectedType is null ? null : FormatType(selection.SelectedType))}:{selection.MayBeAbsent}:{selection.MayBeNull}" +
        $":{selection.Representation}:{Framed(selection.ProfileRevision)}";

    private static string FormatEnvelope(ValueEnvelope envelope)
    {
        var externalMetadata = envelope.ExternalReference is null
            ? string.Empty
            : string.Join(',', envelope.ExternalReference.Metadata
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{item.Key}={item.Value}"));
        var payload = envelope.WithheldValue is { } withheld
            ? FormatWithheld(withheld)
            : envelope.InlineValue.HasValue
                ? CanonicalJson(envelope.InlineValue)
                : envelope.ExternalReference is null
                    ? string.Empty
                    : $"{envelope.ExternalReference.StorageProfile}:{envelope.ExternalReference.Locator}[{externalMetadata}]";

        return $"{envelope.Presence}:{FormatType(envelope.Type)}:{FormatPolicy(envelope.Policy)}:{payload}";
    }

    // A withheld envelope has no value; its marker's kind and reference are what stand in for it, so two envelopes
    // that withhold different secrets hash apart. Only a withheld envelope carries a marker, so no other hash moves.
    private static string FormatWithheld(WithheldValue withheld) =>
        $"withheld:{withheld.Kind}:{FormatSecret(withheld.Secret)}";

    // A secret read hashes by reference: the binding holds no value, and the reference is what changes behavior.
    private static string FormatSecret(RuntimeSecretReference? secret) =>
        secret is null ? string.Empty : $"secret:{secret.Name}:{secret.TypeName}:{secret.Scope}";

    private static string FormatExpression(RuntimeExpressionBinding? expression)
    {
        if (expression is null)
            return string.Empty;

        var metadata = string.Join(',', expression.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));
        var resultType = expression.ResultType is null
            ? string.Empty
            : $"{expression.ResultType.Kind}:{expression.ResultType.Id}:{CanonicalJson(expression.ResultType.Schema)}";
        var parameters = string.Join(',', expression.Parameters
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={CanonicalJson(JsonSerializer.SerializeToElement(item.Value, typeof(Elsa.Expressions.Core.Models.ExpressionParameterBinding)))}"));
        // The fallback marker changes what the runtime loads and records (spec 1222), so it is behavior; it is appended
        // only when present, so an expression without one hashes as before.
        var fallback = expression.VariableReadFallback is { } marker
            ? $":fallback({string.Join(',', marker.Candidates.Select(candidate => $"{candidate.Name}={candidate.DeclaringScopeNodeId}/{candidate.VariableKey}"))})"
            : string.Empty;
        return $"expression:{expression.Language}:{expression.Expression}:{resultType}:{expression.CapabilityProfile}:{CanonicalJson(expression.Options)}:({parameters})[{metadata}]{fallback}";
    }

    private static string FormatType(Elsa.Primitives.Models.ValueTypeDescriptor type) =>
        $"{type.Alias}:{type.CollectionKind}:{type.SchemaVersion}:{CanonicalJson(type.Schema)}" +
        (type.Element is null ? string.Empty : $":element:{type.Element.ToDisplayString()}");

    private static string FormatPolicy(ValueProtectionPolicy policy)
    {
        var metadata = string.Join(',', policy.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));
        return $"{policy.Lifecycle}:{policy.Storage}:{policy.IsSensitive}:{policy.RequiresEncryption}:{policy.RedactionMode}:{policy.RetentionPolicy}[{metadata}]";
    }

    private static string FormatOutputCapture(KeyValuePair<string, RuntimeOutputCapture> output)
    {
        var capture = output.Value;
        var metadata = string.Join(',', capture.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));
        var schema = CanonicalJson(capture.Type.Schema);
        var conversion = capture.ConversionPlan is null ? string.Empty : $":conversion={capture.ConversionPlan.Fingerprint}";
        return $"{output.Key}={capture.OutputName}:{capture.ValueId}:{capture.Type.Kind}:{capture.Type.Id}:{schema}:" +
               $"{capture.Lifecycle}:{capture.Storage}:{capture.StorageDriverKey}:{capture.CaptureOnSuccessfulCompletion}[{metadata}]{conversion}";
    }

    private static string FormatNode(ExecutableNode node)
    {
        var childSlots = string.Join(',', node.ChildSlots
            .OrderBy(slot => slot.Name, StringComparer.Ordinal)
            .Select(slot =>
            {
                var activities = string.Join(';', slot.Activities.Select(activity => activity.ExecutableNodeId).Order(StringComparer.Ordinal));
                var capability = slot.OperatorSchedulingCapability is null
                    ? string.Empty
                    : $":operator={slot.OperatorSchedulingCapability.PolicyKey}:{slot.OperatorSchedulingCapability.SchemaVersion}:{CanonicalJson(slot.OperatorSchedulingCapability.Configuration)}";
                return $"{slot.Name}({activities}){capability}";
            }));
        var structure = node.Structure is null
            ? string.Empty
            : $"{node.Structure.Kind}:{node.Structure.SchemaVersion}:{CanonicalJson(node.Structure.Payload)}";
        var intrinsic = node.IntrinsicKind is null
            ? string.Empty
            : $"intrinsic:{node.IntrinsicKind}:{node.IntrinsicVariable?.DeclaringScopeId}:{node.IntrinsicVariable?.VariableKey}";
        var outputCaptures = string.Join(',', node.OutputCaptures
            .OrderBy(output => output.Key, StringComparer.Ordinal)
            .Select(FormatOutputCapture));
        var outputCapturePayload = outputCaptures.Length == 0 ? string.Empty : $":outputs={outputCaptures}";
        var executionPlacement = node.ExecutionPlacement == ActivityExecutionPlacement.Inline
            ? string.Empty
            : $":executionPlacement={node.ExecutionPlacement}";
        var legacyShape = $"{node.ExecutableNodeId}:{node.ActivityType}:{node.ActivityTypeVersion}:{node.Descriptor.ConsumerKey}:{node.Descriptor.SchemaVersion}:{CanonicalJson(node.Descriptor.Payload)}:{structure}:{string.Join(',', node.InputBindings.OrderBy(input => input.Key, StringComparer.Ordinal).Select(FormatInputBinding))}:{childSlots}{outputCapturePayload}{executionPlacement}";
        var nodeShape = intrinsic.Length == 0 ? legacyShape : $"{legacyShape}:{intrinsic}";
        return node.ActivityContract is null
            ? nodeShape
            : $"{nodeShape}:contract:{node.ActivityContract.SchemaFingerprint}";
    }

    private static void WriteBehavioralPayload(
        Utf8JsonWriter writer,
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies,
        PersistenceMode? persistenceMode,
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables,
        IncidentStrategyReference incidentStrategy)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 2);
        writer.WriteString("rootNodeId", rootActivity.ExecutableNodeId);

        writer.WriteStartObject("incidentStrategy");
        writer.WriteString("alias", incidentStrategy.Alias);
        writer.WriteString("version", incidentStrategy.Version);
        writer.WriteEndObject();

        // The chosen persistence mode is behavioral content: it changes when the run's state becomes durable, so equal
        // hash must imply equal mode. Written only when the workflow chose one, so a workflow that did not hashes
        // byte-identically to one compiled before the field existed.
        if (persistenceMode is { } mode)
            writer.WriteString("persistenceMode", mode.ToString());

        // Workflow-scope variable declarations are behavioral content (#972): they define the root variable
        // frame's key set, types, and defaults, so equal-hash must imply an equal workflow scope. Written only
        // when declared, so a workflow without state.Variables hashes byte-identically to before this field
        // existed — existing artifact ids and goldens for variable-less workflows are stable.
        if (workflowVariables is { Count: > 0 })
        {
            writer.WriteStartArray("workflowVariables");
            foreach (var variable in workflowVariables.OrderBy(variable => variable.VariableKey, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("variableKey", variable.VariableKey);
                writer.WriteString("name", variable.Name);
                writer.WriteString("type", FormatType(variable.Type));
                writer.WriteString("policy", FormatPolicy(variable.Policy));
                writer.WriteString("initialBinding", variable.InitialBinding is { Literal: { } literal }
                    ? FormatEnvelope(literal)
                    : string.Empty);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteStartArray("nodes");
        foreach (var node in FlattenExecutableActivities(rootActivity).OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal))
            WriteNode(writer, node);
        writer.WriteEndArray();

        writer.WriteStartObject("inputContract");
        writer.WriteNumber("version", inputContract.Version);
        writer.WriteStartArray("inputs");
        foreach (var input in inputContract.Inputs.OrderBy(input => input.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", input.Name);
            writer.WriteString("typeAlias", input.Type.Alias);
            writer.WriteString("collectionKind", input.Type.CollectionKind.ToString());
            writer.WriteBoolean("isRequired", input.IsRequired);
            writer.WriteBoolean("hasDefaultValue", input.DefaultValue.HasValue);
            writer.WritePropertyName("defaultValue");
            if (input.DefaultValue is { } defaultValue)
                WriteCanonicalJson(writer, defaultValue);
            else
                writer.WriteNullValue();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartArray("dependencies");
        foreach (var dependency in dependencies
                     .OrderBy(dependency => dependency.ArtifactId, StringComparer.Ordinal)
                     .ThenBy(dependency => dependency.ArtifactHash, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("artifactId", dependency.ArtifactId);
            writer.WriteString("artifactHash", dependency.ArtifactHash);
            writer.WriteStartArray("dispatchNodeIds");
            foreach (var nodeId in dependency.DispatchNodeIds.Order(StringComparer.Ordinal))
                writer.WriteStringValue(nodeId);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteNode(Utf8JsonWriter writer, ExecutableNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("nodeId", node.ExecutableNodeId);
        writer.WriteString("activityType", node.ActivityType);
        writer.WriteString("activityTypeVersion", node.ActivityTypeVersion);
        if (node.ExecutionPlacement != ActivityExecutionPlacement.Inline)
            writer.WriteString("executionPlacement", node.ExecutionPlacement.ToString());
        if (node.ActivityContract is { } activityContract)
            writer.WriteString("sideEffectProfile", activityContract.SideEffectProfile.ToString());
        writer.WriteStartObject("descriptor");
        writer.WriteString("consumerKey", node.Descriptor.ConsumerKey);
        writer.WriteString("schemaVersion", node.Descriptor.SchemaVersion);
        writer.WritePropertyName("payload");
        WriteCanonicalJson(writer, node.Descriptor.Payload);
        writer.WriteEndObject();

        writer.WritePropertyName("structure");
        if (node.Structure is { } structure)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", structure.Kind);
            writer.WriteString("schemaVersion", structure.SchemaVersion);
            writer.WritePropertyName("payload");
            WriteCanonicalJson(writer, structure.Payload);
            writer.WriteEndObject();
        }
        else
            writer.WriteNullValue();

        writer.WriteStartArray("inputBindings");
        foreach (var (inputName, binding) in node.InputBindings.OrderBy(input => input.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", inputName);

            // Canonical structured JSON covers the complete binding, including destination type,
            // policy, source reference and portable expression contracts. Explicit metadata keeps
            // this independent of host serializer options and available without reflection.
            writer.WritePropertyName("contract");
            WriteCanonicalJson(writer, Elsa.Serialization.Core.JsonHashInput.SerializeToElement(binding));
            writer.WriteString("source", binding.Source.ToString());
            writer.WritePropertyName("payload");
            if (binding.Source == RuntimeInputBindingSource.Expression)
            {
                writer.WriteStartObject();
                writer.WriteString("language", binding.Expression?.Language);
                writer.WriteString("expression", binding.Expression?.Expression);
                writer.WriteEndObject();
            }
            else if (binding.Source == RuntimeInputBindingSource.SecretRead)
            {
                // Written only for secret reads, so the payload of every other binding is unchanged.
                writer.WriteStartObject();
                writer.WriteString("secretName", binding.Secret?.Name);
                writer.WriteString("secretTypeName", binding.Secret?.TypeName);
                writer.WriteString("secretScope", binding.Secret?.Scope);
                writer.WriteEndObject();
            }
            else if (binding.LiteralValue is { } literalValue)
                WriteCanonicalJson(writer, literalValue);
            else if (binding.Literal?.WithheldValue is { } withheld)
                writer.WriteStringValue(FormatWithheld(withheld));
            else
                writer.WriteNullValue();

            writer.WriteStartObject("metadata");
            foreach (var (key, value) in binding.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
                writer.WriteString(key, value);
            writer.WriteEndObject();
            if (binding.ConversionPlan is not null)
                writer.WriteString("conversionPlanFingerprint", binding.ConversionPlan.Fingerprint);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        var outputConversionPlans = node.OutputCaptures
            .Where(output => output.Value.ConversionPlan is not null)
            .OrderBy(output => output.Key, StringComparer.Ordinal)
            .ToArray();
        if (outputConversionPlans.Length > 0)
        {
            writer.WriteStartArray("outputConversionPlans");
            foreach (var (outputName, capture) in outputConversionPlans)
            {
                writer.WriteStartObject();
                writer.WriteString("name", outputName);
                writer.WriteString("fingerprint", capture.ConversionPlan!.Fingerprint);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteStartArray("childSlots");
        foreach (var slot in node.ChildSlots.OrderBy(slot => slot.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", slot.Name);
            writer.WriteStartArray("nodeIds");
            foreach (var activity in slot.Activities.OrderBy(activity => activity.ExecutableNodeId, StringComparer.Ordinal))
                writer.WriteStringValue(activity.ExecutableNodeId);
            writer.WriteEndArray();
            if (slot.OperatorSchedulingCapability is { } capability)
            {
                writer.WriteStartObject("operatorSchedulingCapability");
                writer.WriteString("policyKey", capability.PolicyKey);
                writer.WriteNumber("schemaVersion", capability.SchemaVersion);
                writer.WritePropertyName("configuration");
                WriteCanonicalJson(writer, capability.Configuration);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        writer.WriteRawValue(Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(element), skipInputValidation: true);
    }

    private static string CanonicalJson(JsonElement? element)
    {
        return element.HasValue ? Encoding.UTF8.GetString(Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(element.Value)) : string.Empty;
    }

    private static IEnumerable<ExecutableNode> FlattenExecutableActivities(ExecutableNode rootActivity)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
