using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Serialization.Core;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Materializes explicit setup without authorizing a start or creating execution history.</summary>
internal static class RuntimeTestContextMaterializer
{
    internal const string FramePrefix = "test-context:";

    // This data-only boundary is shared with inspection. It does not resolve or manufacture authority.
    internal static async ValueTask<(RuntimeTestContextState Context, VariableFrameState RootFrame)> CreateFromSeedAsync(
        WorkflowExecutable executable, string executableNodeId, string executionId, VariableFrameState root,
        RuntimeTestContextSeed seed, IInlineEventPublisher? publisher,
        IReadOnlyDictionary<string, JsonElement> workflowInputs, IReadOnlyDictionary<string, JsonElement> rootVariableSeeds,
        IWorkflowExecutableInputValidator? valueValidator, IWellKnownTypeRegistry? types, IPortableExpressionEvaluator? evaluator, CancellationToken cancellationToken)
    {

        if (executable.InputContract is { } requestContract)
        {
            foreach (var declaration in requestContract.Inputs)
            {
                var value = workflowInputs.TryGetValue(declaration.Name, out var supplied) ? supplied : declaration.DefaultValue;
                if (value is not { } payload) continue;
                var type = new ValueTypeDescriptor(declaration.Type.Alias, declaration.Type.CollectionKind);
                ValidateSeedValue(payload.ValueKind == JsonValueKind.Null
                        ? ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline)
                        : ValueEnvelope.Inline(type, payload, ValueProtectionPolicy.InstanceInline),
                    type, ValueProtectionPolicy.InstanceInline, valueValidator, types);
            }
            if (!(valueValidator ?? throw Refused("The test request payload validator is unavailable."))
                .Validate(requestContract, workflowInputs).IsValid)
                throw Refused("The supplied workflow request differs from its pinned input contract.");
        }
        else if (workflowInputs.Count > 0)
            throw Refused("Supplied workflow request data requires a qualified pinned input contract.");

        var path = FindPath(executable.RootActivity, executableNodeId)
            ?? throw Refused("The selected test node is absent from the exact executable.");
        var owned = Descendants(path[^1]).Select(x => x.ExecutableNodeId).ToArray();
        var ancestors = path.Take(path.Count - 1).Select(x => x.ExecutableNodeId).ToArray();
        var requiredClosed = path.Take(path.Count - 1).LastOrDefault(x =>
            x.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityExecutionScope) == RuntimeMetadataKeys.ActivityExecutionScopeClosed);
        if (requiredClosed is not null && !seed.Frames.Any(x => x.ScopeId == requiredClosed.ExecutableNodeId &&
                x.Kind == VariableFrameKind.Root && x.IsClosedBoundary))
            throw Refused("The selected occurrence requires its actual closed enclosing context before outer fallback is available.");
        var previousIndex = -1;
        var seenFrames = new HashSet<(string, VariableFrameKind)>();
        var frames = new List<RuntimeTestContextFrame>();
        var parent = root;
        foreach (var definition in seed.Frames)
        {
            var index = Array.IndexOf(ancestors, definition.ScopeId);
            if (index < 0 || index < previousIndex)
                throw Refused("A supplied frame is not an enclosing scope of the selected occurrence.");
            previousIndex = index;
            if (!seenFrames.Add((definition.ScopeId, definition.Kind)))
                throw Refused("An enclosing test scope cannot supply duplicate frames of one kind.");
            var contribution = new RuntimeTestScopeContractsCollecting(executable, path[index], definition.Kind);
            await (publisher ?? throw Refused("Model-owned test scope discovery is unavailable."))
                .Publish(contribution, cancellationToken);
            if (contribution.Contracts.Count != 1)
                throw Refused("An enclosing test scope requires exactly one qualified model contract.");
            var contract = contribution.Contracts.Single();
            if (contract.ScopeId != definition.ScopeId || contract.Kind != definition.Kind ||
                contract.IsClosedBoundary != definition.IsClosedBoundary ||
                contract.IsClosedBoundary != (definition.Kind == VariableFrameKind.Root))
                throw Refused("The supplied scope kind differs from its model-owned capability.");
            var values = definition.Values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (var (key, value) in values)
            {
                if (!contract.Variables.TryGetValue(key, out var declaration))
                    throw Refused("A supplied scope value has no captured declaration.");
                Validate(value, declaration.Type, declaration.Policy);
            }
            foreach (var (key, declaration) in contract.Variables.Where(x => !values.ContainsKey(x.Key)))
                if (declaration.InitialBinding is { Source: RuntimeInputBindingSource.Literal, Literal: { } literal })
                {
                    Validate(literal, declaration.Type, declaration.Policy);
                    values.Add(key, literal);
                }
                else if (declaration.InitialBinding is null)
                    // A declared write-only slot is real metadata, not an invented null value. Reads still refuse Absent.
                    values.Add(key, ValueEnvelope.Absent(declaration.Type, declaration.Policy));
                else
                    throw Refused("An enclosing initial expression requires an explicitly supplied materialized value.");
            var inputs = (definition.Inputs ?? new Dictionary<string, ValueEnvelope>()).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (var (key, value) in inputs)
            {
                if (!contract.Inputs.TryGetValue(key, out var input))
                    throw Refused("A supplied closed input has no captured declaration.");
                if (value.Presence == ValuePresence.ExplicitNull && !input.IsNullable)
                    throw Refused("A supplied closed input is null despite its non-nullable pinned member contract.");
                Validate(value, input.Type, ClosedInputPolicy(input));
            }
            foreach (var input in contract.Inputs.Values.Where(x => !inputs.ContainsKey(x.Key)))
            {
                if (input.HasDefault && input.DefaultValue is { } defaultValue)
                {
                    if (defaultValue.ValueKind == JsonValueKind.Null && !input.IsNullable)
                        throw Refused("A closed input default is null despite its non-nullable pinned member contract.");
                    var policy = ClosedInputPolicy(input);
                    var value = defaultValue.ValueKind == JsonValueKind.Null ? ValueEnvelope.Null(input.Type, policy) : ValueEnvelope.Inline(input.Type, defaultValue, policy);
                    Validate(value, input.Type, policy);
                    inputs.Add(input.Key, value);
                }
                else if (input.IsRequired) throw Refused("A required closed-scope input is missing from test setup.");
            }
            var names = contract.Variables.ToDictionary(x => x.Key, x => x.Value.Name, StringComparer.Ordinal);
            if (definition.VariableNames?.Any(x => names.GetValueOrDefault(x.Key) != x.Value) == true)
                throw Refused("Supplied variable names differ from the captured scope declarations.");
            var frameId = $"{FramePrefix}{seed.ContextId.Length}:{seed.ContextId}:{frames.Count}:{definition.ScopeId}";
            var frame = new VariableFrameState(frameId, definition.ScopeId, seed.ContextId,
                definition.IsClosedBoundary ? null : parent.FrameId, definition.Kind, values);
            frames.Add(new(frame, definition.IsClosedBoundary,
                definition.Writable && contract.Writable && definition.Kind != VariableFrameKind.Iteration, inputs, names));
            parent = frame;
        }

        var rootDeclarations = new RuntimeVariableDeclarationProjector().ProjectDeclarations(executable.WorkflowVariables);
        foreach (var (rawKey, rawValue) in rootVariableSeeds)
        {
            var declarations = rootDeclarations.Values.Where(x => x.VariableKey == rawKey || x.Name == rawKey).ToArray();
            if (declarations.Length != 1)
                throw Refused("A raw root-variable seed must name one captured workflow declaration.");
            var declaration = declarations[0];
            if (rootVariableSeeds.TryGetValue(declaration.VariableKey, out var byKey) &&
                rootVariableSeeds.TryGetValue(declaration.Name, out var byName) && !JsonElement.DeepEquals(byKey, byName))
                throw Refused("Root-variable key and name seeds conflict.");
            var envelope = rawValue.ValueKind == JsonValueKind.Null
                ? ValueEnvelope.Null(declaration.Type, declaration.Policy)
                : ValueEnvelope.Inline(declaration.Type, rawValue, declaration.Policy);
            Validate(envelope, declaration.Type, declaration.Policy);
        }
        foreach (var (key, value) in seed.RootVariables ?? new Dictionary<string, ValueEnvelope>())
        {
            RequireDurable(value);
            if (!root.Values.TryGetValue(key, out var declared) || !SameType(declared.Type, value.Type) ||
                !value.Policy.Satisfies(declared.Policy) || value.HoldsValueRequiringEncryption(declared.Policy))
                throw Refused("A supplied root variable must match its captured declaration.");
            Validate(value, declared.Type, declared.Policy);
            var declaration = rootDeclarations[key];
            if ((rootVariableSeeds.ContainsKey(key) || rootVariableSeeds.ContainsKey(declaration.Name)) &&
                (declared.Presence != value.Presence || declared.InlineValue is { } raw &&
                    (value.InlineValue is not { } supplied || !JsonElement.DeepEquals(raw, supplied))))
                throw Refused("Trusted and raw root-variable seeds conflict; the canonical owner cannot choose a winner.");
            root = root.Set(key, value, root.Revision);
        }
        foreach (var value in root.Values.Values.Where(x => x.Presence != ValuePresence.Absent))
            Validate(value, value.Type, value.Policy);
        var results = new List<RuntimeTestResultFixture>();
        if (seed.Results.GroupBy(x => (x.ProducerNodeId, x.ProducerScopeId)).Any(x => x.Count() != 1))
            throw Refused("Prior-result fixture identities must be unique within the supplied test context.");
        foreach (var fixture in seed.Results)
        {
            RequireDurable(fixture.Result);
            if (owned.Contains(fixture.ProducerNodeId, StringComparer.Ordinal) ||
                !executable.NodesById.TryGetValue(fixture.ProducerNodeId, out var producer) ||
                producer.ActivityContract is not { } contract || !SameType(contract.Result.Type, fixture.Result.Type))
                throw Refused("A prior-result fixture must name an outside producer with its exact result type.");
            if (contract.Result.IsRequired && fixture.Result.Presence == ValuePresence.ExplicitNull)
                throw Refused("An outside result fixture cannot represent a null required producer result.");
            ValueProtectionPolicy policy;
            try
            {
                var completePolicy = contract.Result.Projections.Values.Aggregate(contract.Result.Policy,
                    (minimum, projection) => ValuePolicyCombiner.Combine(minimum, projection.Policy, "Atomic prior-result fixture"));
                policy = ValuePolicyCombiner.RequireMinimum(fixture.Result.Policy, completePolicy, "Prior-result test fixture");
            }
            catch (InvalidOperationException exception)
            {
                throw new RuntimeTestContextUnavailableException("runtime.test-context.fixture-policy-unavailable",
                    "The atomic fixture result requires an unavailable persistence or protection profile.", innerException: exception);
            }
            if (contract.Result.Projections.Count != 0)
                throw new RuntimeTestContextUnavailableException("runtime.test-context.result-projection-unavailable",
                    "Outside result projections require a qualified completion-equivalent payload validator.");
            Validate(fixture.Result, contract.Result.Type, policy);
            if (fixture.Result.HoldsValueRequiringEncryption(policy))
                throw Refused("A protected prior-result fixture cannot persist unencrypted material.");
            results.Add(fixture with { Result = new(fixture.Result.Type, fixture.Result.Presence, fixture.Result.InlineValue,
                fixture.Result.ExternalReference, policy, fixture.Result.WithheldValue) });
        }
        foreach (var node in Descendants(path[^1]))
        {
            ValidateConfiguredInputs(node);
            if (node.IntrinsicKind is WorkflowIntrinsicKind.Set or WorkflowIntrinsicKind.Merge or WorkflowIntrinsicKind.Reduce &&
                node.IntrinsicVariable is { } target && !owned.Contains(target.DeclaringScopeId, StringComparer.Ordinal))
            {
                var declared = RequireWriteTarget(node, target.DeclaringScopeId, target.VariableKey);
                if (!node.InputBindings.TryGetValue(WorkflowIntrinsicInputKeys.Value, out var value) ||
                    declared.Type.Alias != "Elsa.Any" && !SameType(declared.Type, value.TargetType))
                    throw Refused("An enclosing intrinsic write target differs from its pinned value type.");
                if (!value.EffectivePolicy.Satisfies(declared.Policy))
                    throw Refused("An enclosing intrinsic write would weaken its captured protection policy.");
            }
            foreach (var capture in node.OutputCaptures.Values.Where(x => x.CaptureOnSuccessfulCompletion))
            {
                if (capture.Metadata.TryGetValue(RuntimeMetadataKeys.CaptureTargetScopeId, out var scope) &&
                    !owned.Contains(scope, StringComparer.Ordinal))
                {
                    var owner = VisibleFrames().LastOrDefault(x => x.IsClosedBoundary);
                    var key = capture.Metadata.GetValueOrDefault(RuntimeMetadataKeys.TargetVariableReferenceKey);
                    if (owner is null || owner.Frame.ScopeId != scope || !owner.Writable || key is null ||
                        HasOwnedClosedAncestor(node) || !owner.Frame.Values.TryGetValue(key, out var declaration))
                        throw Refused("Selected work requires a qualified writable enclosing capture owner before execution.");
                    ValidateCapture(capture, node, declaration, writesCanonicalRoot: false);
                }
                else if (!capture.Metadata.ContainsKey(RuntimeMetadataKeys.CaptureTargetScopeId) &&
                         capture.ValueId.StartsWith(RuntimeWorkflowStateSeed.VariableValueIdPrefix, StringComparison.Ordinal))
                {
                    var key = capture.Metadata.GetValueOrDefault(RuntimeMetadataKeys.TargetVariableReferenceKey)
                        ?? throw Refused("A workflow-variable capture is missing its captured declaration key.");
                    ValidateCapture(capture, node, RequireWriteTarget(node, Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId, key),
                        writesCanonicalRoot: true);
                }
            }
            foreach (var binding in node.InputBindings.Values)
            {
                if (binding.ActivityResult is { } reference)
                {
                    RequireFixture(reference.ProducerExecutableNodeId, reference.ProducerScopeId);
                    if (!owned.Contains(reference.ProducerExecutableNodeId, StringComparer.Ordinal))
                        ValidateSuppliedInput(binding, node, results.Single(x => x.ProducerNodeId == reference.ProducerExecutableNodeId &&
                            (reference.ProducerScopeId is null || x.ProducerScopeId == reference.ProducerScopeId)).Result);
                }
                if (binding.Expression is { } expression)
                {
                    var definition = new ExpressionDefinition(expression.Language, expression.Expression,
                        binding.TargetType.ToTypeReference(), expression.Parameters, expression.Options, expression.CapabilityProfile, expression.Metadata);
                    if (evaluator is not IPortableExpressionCapabilities capabilities || !capabilities.CanEvaluate(definition))
                        throw Refused("The selected portable evaluator has no qualified routing capability for this expression.");
                    foreach (var parameter in expression.Parameters.Values)
                        switch (parameter)
                        {
                            case Elsa.Expressions.Core.Models.ActivityResultExpressionParameterBinding result: RequireFixture(result.ProducerNodeId, null); break;
                            case Elsa.Expressions.Core.Models.VariableExpressionParameterBinding variable: RequireVariable(variable.DeclaringScopeNodeId, variable.VariableKey, node); break;
                            case Elsa.Expressions.Core.Models.WorkflowRequestExpressionParameterBinding request when !HasOwnedClosedAncestor(node): RequireRequest(request.MemberKey); break;
                        }
                    await RequireCompleteAmbientContextAsync(node);
                }
                if (binding.Variable is { } variableReference)
                {
                    RequireVariable(variableReference.DeclaringScopeId, variableReference.VariableKey, node);
                    if (!owned.Contains(variableReference.DeclaringScopeId, StringComparer.Ordinal))
                    {
                        var visible = VisibleFrames();
                        var value = variableReference.DeclaringScopeId == VariableReference.WorkflowScopeId ? visible[0].Frame.Values[variableReference.VariableKey] :
                            visible.Last(x => x.Frame.ScopeId == variableReference.DeclaringScopeId).Frame.Values[variableReference.VariableKey];
                        ValidateSuppliedInput(binding, node, value);
                    }
                }
                if (binding.WorkflowRequest is { } requestReference && !HasOwnedClosedAncestor(node))
                {
                    RequireRequest(requestReference.MemberKey);
                    var closed = frames.LastOrDefault(x => x.IsClosedBoundary);
                    if (closed is not null) ValidateSuppliedInput(binding, node, closed.Inputs[requestReference.MemberKey]);
                    else
                    {
                        var raw = workflowInputs.TryGetValue(requestReference.MemberKey, out var supplied) ? supplied :
                            executable.InputContract!.Inputs.Single(x => x.Name == requestReference.MemberKey).DefaultValue!.Value;
                        ValidateSuppliedInput(binding, node, raw.ValueKind == JsonValueKind.Null ? ValueEnvelope.Null(binding.TargetType, binding.EffectivePolicy) :
                            ValueEnvelope.Inline(binding.TargetType, raw, binding.EffectivePolicy));
                    }
                }
            }
        }
        return (new(seed.ContextId, seed.ContextFingerprint, frames, results, owned), root);

        void ValidateSuppliedInput(RuntimeInputBinding binding, ExecutableNode node, ValueEnvelope value)
        {
            if (value.Presence == ValuePresence.ExplicitNull && node.ActivityContract?.Inputs.GetValueOrDefault(binding.InputKey) is { IsNullable: false })
                throw Refused("A supplied setup value is null despite the selected input's pinned non-nullable contract.");
            if (binding.ConversionPlan is not null)
                throw new RuntimeTestContextUnavailableException("runtime.test-context.conversion-unavailable",
                    "Supplied context conversions require a qualified constructor-free preflight.", memberKey: binding.InputKey);
            Validate(value, binding.TargetType, binding.EffectivePolicy);
        }

        ValueProtectionPolicy ClosedInputPolicy(Elsa.Activities.Runtime.Core.Models.ActivityInputContract input)
        {
            try { return ValuePolicyCombiner.RequireMinimum(ValueProtectionPolicy.InstanceInline, input.Policy, "Test scope input"); }
            catch (InvalidOperationException exception)
            {
                throw new RuntimeTestContextUnavailableException("runtime.test-context.input-policy-unavailable",
                    "The captured closed input requires an unavailable persistence or protection profile.", memberKey: input.Key, innerException: exception);
            }
        }

        void RequireFixture(string producer, string? scope)
        {
            if (owned.Contains(producer, StringComparer.Ordinal)) return;
            var matches = seed.Results.Where(x => x.ProducerNodeId == producer && (scope is null || x.ProducerScopeId == scope)).ToArray();
            if (matches.Length != 1 || matches[0].Result.Presence == ValuePresence.Withheld)
                throw Refused("Selected work requires one explicit outside prior-result fixture before execution.");
        }

        void Validate(ValueEnvelope value, Elsa.Primitives.Models.ValueTypeDescriptor type, ValueProtectionPolicy policy) =>
            ValidateSeedValue(value, type, policy, valueValidator, types);

        void ValidateConfiguredInputs(ExecutableNode node)
        {
            // Declaration/JSON inspection must precede any owned child. No activity constructor,
            // expression evaluation, secret resolution or external payload read is allowed here.
            foreach (var binding in node.InputBindings.Values)
            {
                if (binding.ConversionPlan is not null || binding.Expression?.ParameterConversionPlans.Count > 0)
                    throw new RuntimeTestContextUnavailableException("runtime.test-context.conversion-unavailable",
                        "Context conversions require a qualified constructor-free preflight.", memberKey: binding.InputKey);
                if (!string.IsNullOrWhiteSpace(binding.WorkflowRequest?.Path) ||
                    binding.Expression?.Parameters.Values.OfType<WorkflowRequestExpressionParameterBinding>().Any(x => !string.IsNullOrWhiteSpace(x.Path)) == true)
                    throw new RuntimeTestContextUnavailableException("runtime.test-context.legacy-path-unavailable",
                        "Legacy request paths require a qualified pure projection preflight.", memberKey: binding.InputKey);
                if (binding.ActivityResult is { ProjectionKey: not "$result" } ||
                    binding.Expression?.Parameters.Values.OfType<ActivityResultExpressionParameterBinding>().Any(x => x.ProjectionKey != "$result") == true)
                    throw new RuntimeTestContextUnavailableException("runtime.test-context.result-projection-unavailable",
                        "Named result projections require a qualified pure projection preflight.", memberKey: binding.InputKey);
                if (binding.Selection is not null)
                    throw new RuntimeTestContextUnavailableException("runtime.test-context.member-selection-unavailable",
                        "Member setup requires a qualified pure selection and destination-fit preflight.", memberKey: binding.InputKey);
                if (node.ActivityContract?.Inputs.GetValueOrDefault(binding.InputKey)?.IsStructureInput == true) continue;
                ValidateType(binding.TargetType, types);
                if (binding.Literal is { Presence: ValuePresence.Absent } absent)
                {
                    var input = node.ActivityContract?.Inputs.GetValueOrDefault(binding.InputKey);
                    if (input is null || input.IsRequired || !input.IsNullable || !SameType(absent.Type, binding.TargetType) ||
                        !absent.Policy.Satisfies(binding.EffectivePolicy))
                        throw Refused("The configured absent test input has no qualified optional nullable member contract.");
                }
                else if (binding.Literal is { } literal) Validate(literal, binding.TargetType, binding.EffectivePolicy);
            }
            if (node.ActivityContract is not { } activityContract) return;
            foreach (var key in node.InputBindings.Keys)
                if (!activityContract.Inputs.ContainsKey(key))
                    throw Refused("A configured test input has no pinned member contract.");
            if (activityContract.Inputs.Keys.Any(key => !node.InputBindings.ContainsKey(key)))
                throw Refused("The pinned activity inputs do not have a complete normalized binding set.");
            foreach (var input in activityContract.Inputs.Values.Where(x => !x.IsStructureInput))
            {
                ValidateType(input.Type, types);
                if (node.InputBindings.TryGetValue(input.Key, out var binding))
                {
                    if (!SameType(input.Type, binding.TargetType) ||
                        !binding.EffectivePolicy.Satisfies(ValuePolicyCombiner.ToProtectionPolicy(input.Policy)))
                        throw Refused("A configured test input differs from its pinned member type or protection contract.");
                    if (binding.Literal is { Presence: ValuePresence.ExplicitNull } && !input.IsNullable)
                        throw Refused("A configured null test input is forbidden by its pinned member contract.");
                }
            }
        }

        bool HasOwnedClosedAncestor(ExecutableNode node) => FindPath(path[^1], node.ExecutableNodeId)!
            .SkipLast(1).Any(x => x.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityExecutionScope) == RuntimeMetadataKeys.ActivityExecutionScopeClosed);

        void RequireVariable(string scope, string key, ExecutableNode? reader = null)
        {
            if (owned.Contains(scope, StringComparer.Ordinal)) return;
            var visible = VisibleFrames().Select(x => x.Frame).ToArray();
            var envelope = scope == Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId
                ? visible[0].Values.GetValueOrDefault(key)
                : visible.LastOrDefault(x => x.ScopeId == scope)?.Values.GetValueOrDefault(key);
            if (reader is not null && HasOwnedClosedAncestor(reader) ||
                envelope is null || envelope.Presence is ValuePresence.Absent or ValuePresence.Withheld)
                throw Refused("Selected work requires an explicit visible variable value before execution.");
        }

        void RequireRequest(string key)
        {
            var closed = frames.LastOrDefault(x => x.IsClosedBoundary);
            if (closed is not null ? !closed.Inputs.ContainsKey(key) : !workflowInputs.ContainsKey(key) &&
                executable.InputContract?.Inputs.Any(x => x.Name == key && x.DefaultValue.HasValue) != true)
                throw Refused("Selected work requires an explicit request input before execution.");
        }

        IReadOnlyList<RuntimeTestContextFrame> VisibleFrames()
        {
            var visible = new List<RuntimeTestContextFrame>
            { new(root, false, true, new Dictionary<string, ValueEnvelope>(), new Dictionary<string, string>()) };
            foreach (var supplied in frames) { if (supplied.IsClosedBoundary) visible.Clear(); visible.Add(supplied); }
            return visible;
        }

        ValueEnvelope RequireWriteTarget(ExecutableNode node, string scope, string key)
        {
            var visible = VisibleFrames();
            var owner = scope == Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId ? visible[0] :
                visible.LastOrDefault(x => x.Frame.ScopeId == scope);
            if (HasOwnedClosedAncestor(node) || owner is null || !owner.Writable || owner.Frame.Kind == VariableFrameKind.Iteration ||
                scope == Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId && owner.Frame.FrameId != root.FrameId ||
                !owner.Frame.Values.TryGetValue(key, out var value))
                throw Refused("Selected work requires a declared writable enclosing variable owner before execution.");
            return value;
        }

        void ValidateCapture(RuntimeOutputCapture capture, ExecutableNode node, ValueEnvelope declaration, bool writesCanonicalRoot)
        {
            var projection = node.ActivityContract?.Result.Projections.GetValueOrDefault(capture.OutputName);
            var resultType = capture.ConversionPlan?.TargetType ?? projection?.Type;
            if (projection is null || resultType is null || !SameType(declaration.Type, resultType) || capture.Type.Kind != resultType.Alias ||
                capture.StorageDriverKey != WellKnownRuntimeDurableValueStorageDrivers.Json ||
                capture.Storage != DurableValueStorage.Custom && capture.Storage != declaration.Policy.Storage ||
                capture.Lifecycle != declaration.Policy.Lifecycle || declaration.Policy.RequiresEncryption)
                throw Refused("An enclosing capture target has an unavailable type, storage or protection contract.");
            // Completion first protects the entire atomic result, including sibling projections. The
            // capture owner then rebases those protection fields onto its destination lifecycle/storage.
            // A normal Result lifetime is not a demand that an Instance variable have that same lifetime.
            var contract = node.ActivityContract!.Result;
            Elsa.Activities.Runtime.Core.Models.ActivityValuePolicy sourcePolicy;
            try
            {
                sourcePolicy = contract.Projections.Values.Aggregate(contract.Policy,
                    (policy, member) => ValuePolicyCombiner.Combine(policy, member.Policy, "Test capture atomic result"));
                sourcePolicy = ValuePolicyCombiner.Combine(sourcePolicy, projection.Policy, "Test capture projection");
            }
            catch (InvalidOperationException exception)
            {
                throw new RuntimeTestContextUnavailableException("runtime.test-context.capture-policy-unavailable",
                    "The atomic captured result has incompatible protection requirements.", memberKey: capture.OutputName, innerException: exception);
            }
            if (!sourcePolicy.IsPersistable || sourcePolicy.RequiresEncryption || sourcePolicy.Storage != Elsa.Activities.Runtime.Core.Models.ActivityValueStorage.Inline ||
                !string.IsNullOrWhiteSpace(sourcePolicy.StorageProfile) ||
                (capture.ConversionPlan?.SourceRepresentation ?? projection.EffectiveSourceRepresentation) == Elsa.Primitives.Models.ValueRepresentation.TransientResource)
                throw Refused("The captured result requires an unavailable persistence or protection profile.");
            var minimum = ValuePolicyCombiner.ToProtectionPolicy(sourcePolicy);
            var effective = new ValueProtectionPolicy(capture.Lifecycle, declaration.Policy.Storage, minimum.IsSensitive,
                minimum.RequiresEncryption, minimum.RedactionMode, minimum.RetentionPolicy, minimum.Metadata);
            // Canonical root writeback retains the declared destination policy. Supplied-frame writeback
            // retains the rebased source policy. Validate the protection that the actual writer will persist.
            if (writesCanonicalRoot ? !declaration.Policy.Satisfies(effective) : !effective.Satisfies(declaration.Policy))
                throw Refused("The actual capture destination would weaken its required protection policy.");
        }

        async ValueTask RequireCompleteAmbientContextAsync(ExecutableNode node)
        {
            // The engine exposes names and computed access. Without an engine-owned dependency manifest we
            // conservatively require every captured enclosing declaration, regardless of evaluator language alias.
            if (HasOwnedClosedAncestor(node)) return;
            var lastClosedIndex = frames.FindLastIndex(x => x.IsClosedBoundary);
            var visibleAncestors = lastClosedIndex < 0 ? path.Take(path.Count - 1) :
                path.SkipWhile(x => x.ExecutableNodeId != frames[lastClosedIndex].Frame.ScopeId).TakeWhile(x => x.ExecutableNodeId != executableNodeId);
            foreach (var ancestor in visibleAncestors)
            {
                var kind = ancestor.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityExecutionScope) == RuntimeMetadataKeys.ActivityExecutionScopeClosed
                    ? VariableFrameKind.Root : VariableFrameKind.Container;
                var contribution = new RuntimeTestScopeContractsCollecting(executable, ancestor, kind);
                await (publisher ?? throw Refused("Ambient context discovery is unavailable.")).Publish(contribution, cancellationToken);
                if (contribution.Contracts.Count != 1)
                    throw Refused("The expression ambient context requires one complete model-owned enclosing contract.");
                var contract = contribution.Contracts.Single();
                foreach (var declaration in contract.Variables.Values)
                    RequireVariable(contract.ScopeId, declaration.VariableKey);
                if (contract.IsClosedBoundary)
                    foreach (var input in contract.Inputs.Values) RequireRequest(input.Key);
            }
            if (lastClosedIndex < 0)
                foreach (var declaration in rootDeclarations.Values)
                    RequireVariable(Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId, declaration.VariableKey);
        }
    }

    private static void RequireDurable(ValueEnvelope value)
    {
        if (value.Presence is ValuePresence.Absent or ValuePresence.Withheld || value.Policy.Lifecycle == DurableValueLifecycle.None ||
            value.TransientResource is not null || value.HoldsValueRequiringEncryption())
            throw Refused("Test context requires explicit durable values; missing or transient fixtures are unavailable.");
    }

    private static bool SameType(Elsa.Primitives.Models.ValueTypeDescriptor left, Elsa.Primitives.Models.ValueTypeDescriptor right) =>
        left.Alias == right.Alias && left.CollectionKind == right.CollectionKind && left.SchemaVersion == right.SchemaVersion &&
        left.Schema?.GetRawText() == right.Schema?.GetRawText();

    private static void ValidateSeedValue(ValueEnvelope value, Elsa.Primitives.Models.ValueTypeDescriptor type, ValueProtectionPolicy policy,
        IWorkflowExecutableInputValidator? validator, IWellKnownTypeRegistry? types)
    {
        RequireDurable(value);
        if (!SameType(value.Type, type) || !value.Policy.Satisfies(policy) || value.HoldsValueRequiringEncryption(policy))
            throw Refused("The supplied context value differs from its captured type or protection policy.");
        if (value.ExternalReference is not null)
            throw Refused("External test context payloads require a separately qualified validation boundary.");
        var alias = ValidateType(type, types);
        var contract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion,
            [new("value", new Elsa.Primitives.Models.TypeReference(alias, type.CollectionKind), true)]);
        var payload = value.Presence == ValuePresence.ExplicitNull ? JsonSerializer.SerializeToElement<object?>(null) : value.InlineValue!.Value;
        var validation = (validator ?? throw Refused("The test context payload validator is unavailable."))
            .Validate(contract, [new KeyValuePair<string, JsonElement>("value", payload)]);
        if (!validation.IsValid)
            throw Refused("The supplied test context payload is incompatible with its declared type.");
    }

    private static string ValidateType(ValueTypeDescriptor type, IWellKnownTypeRegistry? types)
    {
        if (type.Schema.HasValue)
            throw Refused("The test context schema has no qualified preflight validator.");
        var alias = type.Alias == "Elsa.Any" ? TypeAliasConvention.CanonicalAlias(typeof(object)) : type.Alias;
        if (types is null || !types.TryGetTypeOrDefault(alias, out var elementType))
            throw Refused("The supplied test context type is unavailable.");
        var safeType = Nullable.GetUnderlyingType(elementType) ?? elementType;
        if (safeType.IsDefined(typeof(JsonConverterAttribute), inherit: true))
            throw Refused("Custom-converted test context types require a qualified constructor-free payload validator.");
        if (safeType == typeof(void) || safeType == typeof(IntPtr) || safeType == typeof(UIntPtr) ||
            !safeType.IsPrimitive && !safeType.IsEnum && safeType != typeof(string) && safeType != typeof(decimal) &&
            safeType != typeof(Guid) && safeType != typeof(DateTime) && safeType != typeof(DateTimeOffset) &&
            safeType != typeof(TimeSpan) && safeType != typeof(JsonElement) && safeType != typeof(object))
            throw Refused("The test context CLR type has no qualified constructor-free payload validator.");
        return alias;
    }

    private static RuntimeTestContextUnavailableException Refused(string message) =>
        new("runtime.test-context.setup-unavailable", message);

    private static IReadOnlyList<ExecutableNode>? FindPath(ExecutableNode root, string target)
    {
        if (root.ExecutableNodeId == target) return [root];
        foreach (var child in root.ChildSlots.SelectMany(x => x.Activities))
            if (FindPath(child, target) is { } path) return new[] { root }.Concat(path).ToArray();
        return null;
    }

    private static IEnumerable<ExecutableNode> Descendants(ExecutableNode root)
    {
        yield return root;
        foreach (var child in root.ChildSlots.SelectMany(x => x.Activities))
        foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
