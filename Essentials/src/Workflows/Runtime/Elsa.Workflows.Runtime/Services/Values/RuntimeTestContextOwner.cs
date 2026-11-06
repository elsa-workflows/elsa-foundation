using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Serialization.Core;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Owns supplied lexical state without manufacturing activity invocations.</summary>
internal sealed class RuntimeTestContextOwner(
    IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities) : IRuntimeTestContextFrameAccess
{
    internal const string FramePrefix = "test-context:";

    internal static async ValueTask<(RuntimeTestContextState Context, VariableFrameState RootFrame)> CreateAsync(
        WorkflowTestEntry entry, WorkflowExecutable executable, string executionId, VariableFrameState root,
        IEnumerable<IRuntimeTestContextSeedSource> sources, IInlineEventPublisher? publisher,
        IReadOnlyDictionary<string, JsonElement> workflowInputs, IReadOnlyDictionary<string, JsonElement> rootVariableSeeds,
        IWorkflowExecutableInputValidator? valueValidator,
        IWellKnownTypeRegistry? types, CancellationToken cancellationToken)
    {
        var providers = sources.ToArray();
        if (providers.Length != 1)
            throw Refused("A selected test context requires exactly one admitted setup source.");
        var seed = await providers[0].ResolveAsync(entry, executable, executionId, cancellationToken)
            ?? throw Refused("The admitted test setup is unavailable.");
        if (seed.ContextId != entry.ContextId || seed.ContextFingerprint != entry.ContextFingerprint ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(entry.Executable, executable.Identity))
            throw Refused("The test setup does not match its admitted identity.");

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

        var path = FindPath(executable.RootActivity, entry.ExecutableNodeId)
            ?? throw Refused("The selected test node is absent from the exact executable.");
        var owned = Descendants(path[^1]).Select(x => x.ExecutableNodeId).ToArray();
        var ancestors = path.Take(path.Count - 1).Select(x => x.ExecutableNodeId).ToArray();
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
                Validate(value, input.Type, ValuePolicyCombiner.RequireMinimum(ValueProtectionPolicy.InstanceInline, input.Policy, "Test scope input"));
            }
            foreach (var input in contract.Inputs.Values.Where(x => !inputs.ContainsKey(x.Key)))
            {
                if (input.HasDefault && input.DefaultValue is { } defaultValue)
                {
                    var policy = ValuePolicyCombiner.RequireMinimum(ValueProtectionPolicy.InstanceInline, input.Policy, "Test scope input default");
                    var value = defaultValue.ValueKind == JsonValueKind.Null ? ValueEnvelope.Null(input.Type, policy) : ValueEnvelope.Inline(input.Type, defaultValue, policy);
                    Validate(value, input.Type, policy);
                    inputs.Add(input.Key, value);
                }
                else if (input.IsRequired) throw Refused("A required closed-scope input is missing from test setup.");
            }
            var names = contract.Variables.ToDictionary(x => x.Key, x => x.Value.Name, StringComparer.Ordinal);
            if (definition.VariableNames?.Any(x => names.GetValueOrDefault(x.Key) != x.Value) == true)
                throw Refused("Supplied variable names differ from the captured scope declarations.");
            var frameId = $"{FramePrefix}{entry.ContextId.Length}:{entry.ContextId}:{frames.Count}:{definition.ScopeId}";
            var frame = new VariableFrameState(frameId, definition.ScopeId, entry.ContextId,
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
        foreach (var fixture in seed.Results)
        {
            RequireDurable(fixture.Result);
            if (owned.Contains(fixture.ProducerNodeId, StringComparer.Ordinal) ||
                !executable.NodesById.TryGetValue(fixture.ProducerNodeId, out var producer) ||
                producer.ActivityContract is not { } contract || !SameType(contract.Result.Type, fixture.Result.Type))
                throw Refused("A prior-result fixture must name an outside producer with its exact result type.");
            var policy = ValuePolicyCombiner.RequireMinimum(fixture.Result.Policy, contract.Result.Policy, "Prior-result test fixture");
            Validate(fixture.Result, contract.Result.Type, policy);
            if (fixture.Result.HoldsValueRequiringEncryption(policy))
                throw Refused("A protected prior-result fixture cannot persist unencrypted material.");
            results.Add(fixture with { Result = new(fixture.Result.Type, fixture.Result.Presence, fixture.Result.InlineValue,
                fixture.Result.ExternalReference, policy, fixture.Result.WithheldValue) });
        }
        foreach (var node in Descendants(path[^1]))
        {
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
                    ValidateCapture(capture, node, declaration);
                }
                else if (!capture.Metadata.ContainsKey(RuntimeMetadataKeys.CaptureTargetScopeId) &&
                         capture.ValueId.StartsWith(RuntimeWorkflowStateSeed.VariableValueIdPrefix, StringComparison.Ordinal))
                {
                    var key = capture.Metadata.GetValueOrDefault(RuntimeMetadataKeys.TargetVariableReferenceKey)
                        ?? throw Refused("A workflow-variable capture is missing its captured declaration key.");
                    ValidateCapture(capture, node, RequireWriteTarget(node, Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId, key));
                }
            }
            foreach (var binding in node.InputBindings.Values)
            {
                if (binding.ActivityResult is { } reference)
                    RequireFixture(reference.ProducerExecutableNodeId, reference.ProducerScopeId);
                if (binding.Expression is { } expression)
                {
                    foreach (var parameter in expression.Parameters.Values)
                        switch (parameter)
                        {
                            case Elsa.Expressions.Core.Models.ActivityResultExpressionParameterBinding result: RequireFixture(result.ProducerNodeId, null); break;
                            case Elsa.Expressions.Core.Models.VariableExpressionParameterBinding variable: RequireVariable(variable.DeclaringScopeNodeId, variable.VariableKey, node); break;
                            case Elsa.Expressions.Core.Models.WorkflowRequestExpressionParameterBinding request when !HasOwnedClosedAncestor(node): RequireRequest(request.MemberKey); break;
                        }
                    if (StringComparer.OrdinalIgnoreCase.Equals(expression.Language, "JavaScript"))
                        await RequireCompleteAmbientContextAsync(node);
                }
                if (binding.Variable is { } variableReference) RequireVariable(variableReference.DeclaringScopeId, variableReference.VariableKey, node);
                if (binding.WorkflowRequest is { } requestReference && !HasOwnedClosedAncestor(node)) RequireRequest(requestReference.MemberKey);
            }
        }
        return (new(entry.ContextId, entry.ContextFingerprint, frames, results, owned), root);

        void RequireFixture(string producer, string? scope)
        {
            if (owned.Contains(producer, StringComparer.Ordinal)) return;
            var matches = seed.Results.Where(x => x.ProducerNodeId == producer && (scope is null || x.ProducerScopeId == scope)).ToArray();
            if (matches.Length != 1 || matches[0].Result.Presence == ValuePresence.Withheld)
                throw Refused("Selected work requires one explicit outside prior-result fixture before execution.");
        }

        void Validate(ValueEnvelope value, Elsa.Primitives.Models.ValueTypeDescriptor type, ValueProtectionPolicy policy) =>
            ValidateSeedValue(value, type, policy, valueValidator, types);

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

        void ValidateCapture(RuntimeOutputCapture capture, ExecutableNode node, ValueEnvelope declaration)
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
            if (!effective.Satisfies(declaration.Policy))
                throw Refused("An enclosing capture would weaken its captured protection policy.");
        }

        async ValueTask RequireCompleteAmbientContextAsync(ExecutableNode node)
        {
            // The engine exposes names and computed access. Without an engine-owned dependency manifest we
            // conservatively require every captured enclosing declaration; Runtime never parses JavaScript.
            if (HasOwnedClosedAncestor(node)) return;
            var lastClosedIndex = frames.FindLastIndex(x => x.IsClosedBoundary);
            var visibleAncestors = lastClosedIndex < 0 ? path.Take(path.Count - 1) :
                path.SkipWhile(x => x.ExecutableNodeId != frames[lastClosedIndex].Frame.ScopeId).TakeWhile(x => x.ExecutableNodeId != entry.ExecutableNodeId);
            foreach (var ancestor in visibleAncestors)
            {
                var kind = ancestor.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityExecutionScope) == RuntimeMetadataKeys.ActivityExecutionScopeClosed
                    ? VariableFrameKind.Root : VariableFrameKind.Container;
                var contribution = new RuntimeTestScopeContractsCollecting(executable, ancestor, kind);
                await (publisher ?? throw Refused("Ambient context discovery is unavailable.")).Publish(contribution, cancellationToken);
                if (contribution.Contracts.Count != 1)
                    throw Refused("The JavaScript ambient context requires one complete model-owned enclosing contract.");
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

    internal static RuntimeTestContextState? RequireContext(WorkflowExecutionState workflow)
    {
        var context = workflow.TestContext;
        if (context is null) return null;
        if (workflow.TestEntry is not { } entry || context.ContextId != entry.ContextId ||
            context.ContextFingerprint != entry.ContextFingerprint || workflow.RunKind != WorkflowRunKind.TestRun ||
            !WorkflowTestScope.ContextEquals(workflow.TestScope, entry.Scope))
            throw new InvalidOperationException("The persisted test frame owner differs from its admitted entry.");
        return context;
    }

    internal static IReadOnlyList<VariableFrameState> EnclosingFrames(WorkflowExecutionState workflow,
        VariableFrameState root, out RuntimeTestContextFrame? closed)
    {
        var context = RequireContext(workflow);
        var frames = new List<VariableFrameState> { root };
        closed = null;
        foreach (var supplied in context?.Frames ?? [])
        {
            if (supplied.Frame.Status != VariableFrameStatus.Active)
                throw new InvalidOperationException("The selected test context is closed.");
            if (supplied.IsClosedBoundary) { frames.Clear(); closed = supplied; }
            frames.Add(supplied.Frame);
        }
        return frames;
    }

    public async ValueTask<RuntimeTestCaptureTarget?> FindCaptureTargetAsync(string workflowExecutionId,
        string activityExecutionId, string scopeId, CancellationToken cancellationToken = default)
    {
        var workflow = await workflows.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null || RequireContext(workflow) is not { } context) return null;
        var activity = await activities.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken)
            ?? throw new InvalidOperationException("The capture producer is unavailable.");
        if (!context.OwnedNodeIds.Contains(activity.Execution.ExecutableNodeId, StringComparer.Ordinal))
            throw new InvalidOperationException("The capture producer is outside selected owned work.");
        var owner = context.Frames.LastOrDefault(x => x.IsClosedBoundary);
        if (owner is null || owner.Frame.ScopeId != scopeId) return null;
        return new(context.ContextId, owner.Frame.FrameId, owner.Frame.ScopeId, owner.Frame.Revision);
    }

    public WorkflowExecutionState ApplyWrites(WorkflowExecutionState workflow, IReadOnlyCollection<RuntimeTestFrameWrite> writes)
    {
        if (writes.Count == 0) return workflow;
        var context = RequireContext(workflow) ?? throw new InvalidOperationException("The test capture owner is unavailable.");
        foreach (var group in writes.GroupBy(x => x.Target.FrameId, StringComparer.Ordinal))
        {
            var owner = context.Frames.Single(x => x.Frame.FrameId == group.Key);
            if (group.Any(x => x.Target.ContextId != context.ContextId || x.Target.ScopeId != owner.Frame.ScopeId ||
                               x.Target.FrameRevision != owner.Frame.Revision))
                throw new InvalidOperationException("The test capture frame changed before checkpoint preparation.");
            foreach (var write in group)
            {
                var current = context.Frames.Single(x => x.Frame.FrameId == group.Key).Frame;
                context = context.Set(group.Key, write.VariableKey, write.Value, current.Revision);
            }
        }
        return workflow with { TestContext = context };
    }

    private static void RequireDurable(ValueEnvelope value)
    {
        if (value.Presence == ValuePresence.Absent || value.Policy.Lifecycle == DurableValueLifecycle.None ||
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
        if (type.Schema.HasValue)
            throw Refused("The test context schema has no qualified preflight validator.");
        if (value.Presence != ValuePresence.Present) return;
        var alias = type.Alias == "Elsa.Any" ? TypeAliasConvention.CanonicalAlias(typeof(object)) : type.Alias;
        if (types is null || !types.TryGetTypeOrDefault(alias, out var elementType))
            throw Refused("The supplied test context type is unavailable.");
        var safeType = Nullable.GetUnderlyingType(elementType) ?? elementType;
        if (safeType == typeof(void) || safeType == typeof(IntPtr) || safeType == typeof(UIntPtr))
            throw Refused("The test context CLR type has no qualified JSON payload validator.");
        if (!safeType.IsPrimitive && !safeType.IsEnum && safeType != typeof(string) && safeType != typeof(decimal) &&
            safeType != typeof(Guid) && safeType != typeof(DateTime) && safeType != typeof(DateTimeOffset) &&
            safeType != typeof(TimeSpan) && safeType != typeof(JsonElement) && safeType != typeof(object))
            throw Refused("The test context CLR type has no qualified constructor-free payload validator.");
        var contract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion,
            [new("value", new Elsa.Primitives.Models.TypeReference(alias, type.CollectionKind), true)]);
        var validation = (validator ?? throw Refused("The test context payload validator is unavailable."))
            .Validate(contract, [new KeyValuePair<string, JsonElement>("value", value.InlineValue!.Value)]);
        if (!validation.IsValid)
            throw Refused("The supplied test context payload is incompatible with its declared type.");
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
