using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Services;

/// <summary>The scoped authority for canonical source identities, causal availability and metadata acquisition.</summary>
internal sealed class ActivityDataSelectionAnalyzer(
    IActivityStructureService structures,
    CatalogVersionResolver catalog,
    ScopedVariablePicker variables,
    ScopedVariableResolver variableResolver,
    IEnumerable<IActivitySerializedShapeSource> shapeSources,
    IInlineEventPublisher events,
    IOptions<WorkflowDesignValidatorOptions> options) : IActivityDataSelectionAnalyzer
{
    public async ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(
        ActivityDataSelectionDocument document,
        ActivityDataSelectionTarget? target = null,
        ActivityDataSelectionQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<ValidationError>();
        var graph = new ActivityDataSelectionGraph(structures);
        graph.Build(document.RootActivity, options.Value.MaxRecursionDepth, findings, cancellationToken, document.CompiledBoundaryInputs);
        if (findings.Count > 0) return Result(document, null, [], findings);
        var context = new Request(document, graph, options.Value.MaxRecursionDepth);

        if (target is null)
        {
            context.WholeVisibility = variableResolver.Resolve(document.Variables, document.RootActivity, context.MaxDepth);
            if (query is not null)
                findings.Add(new("$workflow", "ActivityData/InvalidQuery", "A source query requires a consuming input."));
            foreach (var node in graph.Nodes)
            foreach (var input in node.Inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var facts = await Target(context, new(node.Node.NodeId, input.ReferenceKey), cancellationToken);
                if (facts?.IsCredential == true && (CanonicalReferenceState(input, CredentialInputBinding.SecretExpressionType) is not { } secretState ||
                    !CredentialInputBinding.IsAccepted(true, secretState)))
                    findings.Add(new(Path(node, input.ReferenceKey), CredentialInputBinding.RuleId, "A credential input accepts only a secret reference."));
                if (context.ConnectionTargets.Contains(new(node.Node.NodeId, input.ReferenceKey)) &&
                    (CanonicalReferenceState(input, ConnectionInputBinding.ConnectionExpressionType) is not { } connectionState || !ConnectionInputBinding.IsAccepted(connectionState)))
                    findings.Add(new(Path(node, input.ReferenceKey), ConnectionInputBinding.RuleId, "A connection input accepts only a connection reference."));
                if (facts?.Constraint is { } constraint && input.IsBound() &&
                    !constraint.AllowedExpressionTypes.Contains(ConstraintRole(input.Value.ExpressionType), StringComparer.OrdinalIgnoreCase))
                    findings.Add(new(Path(node, input.ReferenceKey), constraint.ReasonCode, constraint.Message));
                foreach (var binding in ActivityDataReadBinding.Read(input.Value))
                {
                    var path = Path(node, input.ReferenceKey);
                    var source = await Resolve(context, node, binding, path, findings, cancellationToken);
                    // Publication can honour an explicit conversion only from a member whose contract it knows, and only a
                    // typed result member is known; say so here, before publishing, rather than only when it refuses.
                    if (source is not null && binding.IsMemberRead && input.Conversion is { Mode: not AuthoredValueConversionMode.Auto } &&
                        !context.MemberReads.Any(facts => facts.Path == path && facts.IsKnown))
                        findings.Add(new(path, "ActivityData/SelectedMemberContractUnknown",
                            "This member read's member is not typed at publication, so only the default conversion can be honoured."));
                }
            }
            return Result(document, null, [], findings) with { MemberReads = [.. context.MemberReads] };
        }

        var consumer = string.IsNullOrWhiteSpace(target.NodeId) ? null : graph.Find(target.NodeId);
        var targetFacts = consumer is null ? null : await Target(context, target, cancellationToken);
        if (consumer is null || targetFacts is null)
        {
            findings.Add(new($"{target.NodeId}/inputs/{target.InputKey}", "ActivityData/UnknownTarget", "The consuming input is not declared by this canonical candidate and its catalog."));
            return Result(document, null, [], findings);
        }
        query ??= new();
        if (!Valid(query))
        {
            findings.Add(new(Path(consumer, target.InputKey), "ActivityData/InvalidQuery", "Source query bounds or literal member path are invalid."));
            return Result(document, targetFacts, [], findings);
        }
        if (!query.IncludeSources)
        {
            if (query.Source is not null || !query.MemberPath.IsEmpty)
                findings.Add(new(Path(consumer, target.InputKey), "ActivityData/InvalidQuery", "Target-only analysis does not select a source."));
            return Result(document, targetFacts, [], findings);
        }
        if (query.Source is null && !query.MemberPath.IsDefaultOrEmpty)
        {
            findings.Add(new(Path(consumer, target.InputKey), "ActivityData/InvalidQuery", "A literal member path requires a focused source."));
            return Result(document, targetFacts, [], findings);
        }

        var candidates = new List<Source>();
        if (query.Source is not null)
        {
            var binding = ActivityDataReadBinding.Direct(query.Source.ExpressionType, query.Source.Value);
            // A member of a result or variable the whole read names is offered as the member read itself, decided by
            // the same admission that publication applies to a stored one.
            var memberRole = binding switch
            {
                { Role: "ActivityResult", IsDirect: true, Scope: null, Error: null } => ActivityDataReadRoles.ActivityResultMember,
                { Role: "Variable", Error: null } => ActivityDataReadRoles.VariableMember,
                { Role: "WorkflowRequest", Path: null, Error: null } => ActivityDataReadRoles.WorkflowRequestMember,
                { Role: "Input", Error: null } => ActivityDataReadRoles.InputMember,
                _ => null
            };
            // A member path always asks for a member read, so it is never answered with the whole read: with member
            // reads off the member read is refused as a stored one is, and a source with no member read is invalid.
            if (!query.MemberPath.IsDefaultOrEmpty)
                binding = memberRole is null
                    ? binding.Error is null ? binding with { Error = "InvalidSource" } : binding
                    : ActivityDataMemberPath.IsWithinBounds(query.MemberPath)
                        ? binding with { Role = memberRole, MemberPath = query.MemberPath }
                        : binding with { Role = memberRole, Error = "InvalidMemberPath" };
            var source = await Resolve(context, consumer, binding, Path(consumer, target.InputKey), findings, cancellationToken);
            if (source is not null)
            {
                // Return only canonical identity fields. Ignored authored/client fields are never metadata.
                candidates.Add(source);
            }
        }
        else
        {
            foreach (var input in document.Inputs)
            {
                var source = await Resolve(context, consumer, new("Input", input.ReferenceKey), Path(consumer, target.InputKey), findings, cancellationToken);
                if (source is not null) candidates.Add(source);
            }
            foreach (var visible in Visible(context, consumer))
            {
                var scope = AuthoredScope(document, visible.ScopeId);
                var source = await Resolve(context, consumer, new("Variable", visible.Variable.ReferenceKey, scope), Path(consumer, target.InputKey), findings, cancellationToken);
                if (source is not null) candidates.Add(source);
            }
            foreach (var producer in graph.Nodes.OrderBy(node => graph.Rank(node, consumer)))
            {
                if (ReferenceEquals(producer, consumer) || ActivityDataSelectionGraph.CrossesOutOfScope(producer, consumer) ||
                    graph.IsBackEdge(producer, consumer) || RequiresReturn(producer) && !graph.HasExplicitReturn(producer)) continue;
                var availability = graph.Assess(producer, consumer).Availability;
                if (availability == ActivityChildResultAvailability.Unavailable) continue;
                var version = await catalog.Find(producer.Node.ActivityVersionId, cancellationToken);
                if (version is null) continue;
                foreach (var projection in new[] { "$result" }.Concat(version.Outputs.Select(output => output.ReferenceKey)).Distinct(StringComparer.Ordinal))
                {
                    // No scope is invented: static order facts do not establish one. The source carries a binding
                    // without a scope when publication would admit it, and stays inspectable with a reason when it
                    // would not; portable identity is separate.
                    var source = await Resolve(context, consumer,
                        new("ActivityResult", producer.Node.NodeId, Projection: projection) { IsDirect = true },
                        Path(consumer, target.InputKey), findings, cancellationToken, admitOccurrence: false);
                    if (source is not null) candidates.Add(source);
                }
            }
        }

        var allowed = candidates.Where(source => targetFacts.Constraint is not { } constraint ||
            (source.Binding is { } binding
                ? constraint.AllowedExpressionTypes.Contains(ConstraintRole(binding.ExpressionType), StringComparer.OrdinalIgnoreCase)
                : source.ActivityResultSource is not null &&
                  constraint.AllowedExpressionTypes.Contains("ActivityResult", StringComparer.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(query.Search))
            allowed = allowed.Where(source => source.DisplayName.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || source.Id.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        var page = allowed.Skip(query.Skip).Take(query.Take + 1).ToArray();
        var budget = new ShapeBudget(query.ShapeLimits.MaximumMembers, query.ShapeLimits.MaximumDepth);
        var factsBuilder = ImmutableArray.CreateBuilder<ActivityDataSelectionSourceFacts>();
        foreach (var source in page.Take(query.Take))
        {
            var shape = await Shape(source, document, query.MemberPath, new(query.ShapeLimits.MaximumDepth, budget.Remaining), cancellationToken);
            shape = budget.Bound(shape);
            factsBuilder.Add(new(source.Id, source.DisplayName, source.ScopeId, source.Binding, source.Availability, shape, source.ReasonCode) { ActivityResultSource = source.ActivityResultSource });
        }
        if (query.Source is not null && candidates.Count > 0 && !factsBuilder.Any())
            findings.Add(new(Path(consumer, target.InputKey), "ActivityData/SourceRestricted", "The owning input contract does not accept this source role."));
        return Result(document, targetFacts, factsBuilder.ToImmutable(), findings, page.Length > query.Take);
    }

    private async ValueTask<ActivityDataSelectionTargetFacts?> Target(Request context, ActivityDataSelectionTarget target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Targets.TryGetValue(target, out var cached)) return cached;
        var location = context.Graph.Find(target.NodeId);
        if (location is null) return null;
        var projection = location.Projection.Inputs.Where(input => input.ReferenceKey == target.InputKey).ToArray();
        if (projection.Length > 1) return null;
        var version = location.Node.Intrinsic is null ? await catalog.Find(location.Node.ActivityVersionId, ct) : null;
        var definitions = projection.FirstOrDefault()?.Definition is { } projected ? new[] { projected }
            : version?.Inputs.Where(input => input.ReferenceKey == target.InputKey).ToArray() ?? [];
        if (projection.Length == 0 && location.Projection.ConsumedInputKeys.Contains(target.InputKey, StringComparer.Ordinal)) return null;
        InputDefinition? definition = definitions.Length == 1 ? definitions[0] : null;
        if (location.Node.Intrinsic is { ValueType: { } valueType } intrinsic && target.InputKey == "value")
        {
            var type = intrinsic.Variable is { } variable
                ? Visible(context, location).FirstOrDefault(item => item.Variable.ReferenceKey == variable.ReferenceKey &&
                    item.ScopeId == NormalizeScope(context.Document, variable.DeclaringScopeId))?.Variable.Type ?? valueType
                : valueType;
            definition = new("value", "Value", type, StorageDriverType: null, DisplayName: "Value", Category: null,
                IsNullable: false, IsRequired: true);
        }
        if (definition is null) return null;
        var constraints = new List<ActivityDataInputConstraint>();
        if (definition.IsCredential == true)
            constraints.Add(new("Credential", [CredentialInputBinding.SecretExpressionType], CredentialInputBinding.RuleId,
                "A credential input accepts only a secret reference."));
        // Match the existing compiler's canonical connection declaration without adding a Design-to-Runtime dependency.
        if (!string.IsNullOrWhiteSpace(definition.ConnectionType) || definition.Type.Alias == "Elsa.Activities.Runtime.Core.Models.ConnectionReference")
        {
            context.ConnectionTargets.Add(target);
            constraints.Add(new("Connection", [ConnectionInputBinding.ConnectionExpressionType], ConnectionInputBinding.RuleId,
                "A connection input accepts only a connection reference."));
        }
        if (version is not null)
        {
            var collecting = new InputConstraintsCollecting(version, target.InputKey);
            // IInlineEventPublisher selects Sequential and awaits the canonical event pipeline.
            await events.Publish(collecting, ct);
            constraints.AddRange(collecting.Constraints);
        }
        var merged = Intersect(constraints);
        return context.Targets[target] = new(target, definition.DisplayName, definition.Type, definition.IsNullable,
            definition.IsRequired, projection.Length > 0, merged, definition.IsSensitive, definition.IsCredential);
    }

    private async ValueTask<Source?> Resolve(Request context, ActivityDataSelectionGraph.Location consumer,
        ActivityDataReadBinding binding, string path, ICollection<ValidationError> findings, CancellationToken ct, bool admitOccurrence = true)
    {
        ct.ThrowIfCancellationRequested();
        var memberRead = binding.Role == ActivityDataReadRoles.ActivityResultMember;
        if (binding.IsMemberRead && !options.Value.MemberReads)
        {
            findings.Add(new(path, "ActivityData/UnsupportedMemberBinding", "The source binding is malformed or uses a member-read contract not supported by this server."));
            return null;
        }
        if (binding.Error is not null)
        {
            findings.Add(new(path, $"ActivityData/{binding.Error}", "The source binding is malformed or uses a member-read contract not supported by this server."));
            return null;
        }
        if (binding.Role is "Input" or "WorkflowRequest" or ActivityDataReadRoles.InputMember or ActivityDataReadRoles.WorkflowRequestMember)
        {
            // A member read of a request member or a declared input is admitted exactly as the whole read of it, and
            // never typed: a request can be seeded by any caller, so the runtime checks the member when it reads it.
            var requestRead = binding.Role is "WorkflowRequest" or ActivityDataReadRoles.WorkflowRequestMember;
            var memberOfInput = binding.IsMemberRead;
            var input = requestRead
                ? context.RequestInputs.GetValueOrDefault(binding.Key) is [var requested] ? requested : null
                : context.InputIndex.TryFind(binding.Key, out var declared) ? declared : null;
            if (input is null)
            {
                findings.Add(new(path, "ActivityData/UnknownInput", context.WorkflowBoundary
                    ? "The referenced workflow input is missing or ambiguous. Declare the input before reading its request value."
                    : "The referenced boundary input is missing or ambiguous."));
                return null;
            }
            // A workflow's inputs are its request members, keyed by name, and publication lowers a stored Input or
            // InputMember read to that request read; so a workflow offers the request read itself. A reusable body,
            // whose inputs are its boundary's, keeps offering the Input or InputMember read.
            var value = binding.Role is "Input" or ActivityDataReadRoles.InputMember && !context.WorkflowBoundary
                ? memberOfInput
                    ? new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = input.ReferenceKey, memberPath = binding.MemberPath }), ActivityDataReadRoles.InputMember)
                    : new ArgumentValue(new VariableReference(input.ReferenceKey), "Input")
                : RequestRead(context.WorkflowBoundary ? input.Name : input.ReferenceKey, binding, memberOfInput);
            return new($"input:{input.ReferenceKey}", input.DisplayName, null, value,
                ActivityChildResultAvailability.Definite, input.Type, null, null, input.IsNullable, input.IsSensitive, input.IsCredential,
                binding.Path is null ? null : "LegacyRequestPath");
        }
        if (binding.Role is "Variable" or ActivityDataReadRoles.VariableMember)
        {
            // A member read of a variable is admitted exactly as the whole read of it; its member is never typed here,
            // because a variable can hold JSON from any writer, so the runtime checks the member when it reads it.
            var scope = NormalizeScope(context.Document, binding.Scope);
            var matches = Visible(context, consumer).Where(item => item.ScopeId == scope && item.Variable.ReferenceKey == binding.Key).ToArray();
            if (matches.Length != 1)
            {
                findings.Add(new(path, "Variable/UnresolvableReference", "The referenced variable is missing, ambiguous or outside the consuming scope."));
                return null;
            }
            var variable = matches[0].Variable;
            var authoredScope = AuthoredScope(context.Document, scope);
            ArgumentValue read = binding.Role == ActivityDataReadRoles.VariableMember
                ? new(JsonSerializer.SerializeToElement(new { referenceKey = variable.ReferenceKey, declaringScopeId = authoredScope, memberPath = binding.MemberPath }), ActivityDataReadRoles.VariableMember)
                : new(new VariableReference(variable.ReferenceKey, authoredScope), "Variable");
            return new($"variable:{authoredScope}:{variable.ReferenceKey}", variable.Name, scope,
                read, ActivityChildResultAvailability.Definite, variable.Type, null, null, null, null, null);
        }
        if (binding.Role != "ActivityResult" && !memberRead)
        {
            findings.Add(new(path, "ActivityData/InvalidSource", "The requested source role is not a readable data source."));
            return null;
        }
        var producer = context.Graph.Find(binding.Key);
        if (producer is null || context.Graph.IsAmbiguous(binding.Key))
        {
            findings.Add(new(path, "ValueFlow/UnavailableProducer", $"Result producer '{binding.Key}' is not present unambiguously in the canonical graph."));
            return null;
        }
        if (ActivityDataSelectionGraph.CrossesOutOfScope(producer, consumer) || RequiresReturn(producer) && !context.Graph.HasExplicitReturn(producer))
        {
            findings.Add(new(path, "ValueFlow/ScopeBoundary", "This result crosses a structured scope without an explicit typed return, collection, merge or reduction."));
            return null;
        }
        if (context.Graph.IsBackEdge(producer, consumer))
        {
            findings.Add(new(path, "ValueFlow/CyclicBackEdge", "This result lookup crosses a cyclic back-edge; transfer iteration state explicitly."));
            return null;
        }
        var availability = context.Graph.Assess(producer, consumer).Availability;
        var notDefinite = !binding.IsOptional && availability != ActivityChildResultAvailability.Definite;
        if (admitOccurrence && notDefinite)
            findings.Add(new(path, "ValueFlow/UnavailableProducer", "The required result producer is not causally available on every path to this input."));
        // A direct binding without a scope names only the producer. It is sound only where the owning structure and
        // every structure above it say that no earlier run of the producer can also lead to this input; one that has
        // not said is not trusted. Browsing and publication both decide here, so what is offered is what is admitted.
        // Bindings that declare a scope and expression parameters keep their existing rules (#4056).
        var declaresNoScope = binding.IsDirect && binding.Scope is null;
        var earlierRun = declaresNoScope && context.Graph.EarlierProducerRunCanReachConsumer(producer, consumer);
        if (admitOccurrence && earlierRun && !notDefinite)
            findings.Add(new(path, "ValueFlow/EarlierProducerRun", "The owning structure, or a structure around it, does not establish that only one run of this producer can lead to this input, so a result binding without a scope cannot tell its runs apart. Carry the value in a variable instead."));
        var version = await catalog.Find(producer.Node.ActivityVersionId, ct);
        if (version is null)
        {
            findings.Add(new(path, "ActivityData/UnknownProducerContract", "The referenced producer's catalog contract is unavailable."));
            return null;
        }
        var outputs = version.Outputs.Where(output => output.ReferenceKey == binding.Projection).ToArray();
        if (binding.Projection != "$result" && outputs.Length != 1)
        {
            findings.Add(new(path, "ActivityData/UnknownProjection", "The referenced result projection is not declared unambiguously by the actual producer contract."));
            return null;
        }
        // Publication refuses a whole result the producer declares no type for, so a browsed one gets no binding either.
        var noTypedResult = binding.Projection == "$result" && await HasNoTypedResult(context, version, ct);
        if (admitOccurrence && noTypedResult)
        {
            findings.Add(new(path, "ActivityData/UnknownProjection", "The admitted producer contract declares no typed whole result."));
            return null;
        }
        var output = binding.Projection != "$result" && outputs.Length == 1 ? outputs[0] : null;
        var admitted = declaresNoScope && !notDefinite && !earlierRun && !noTypedResult;
        // A member read is admitted exactly as the whole read of the same result, and then by its path; a read the
        // whole-read rules refuse is not typed, so it reports that refusal alone.
        var member = memberRead && admitted ? await Member(context, binding, version, path, findings, ct) : null;
        // A declared scope is returned as supplied. Without one, the binding is returned only when the checks above
        // admit it, so what a client stores unchanged is what publication accepts.
        ArgumentValue? direct = memberRead
            ? admitted && member!.Refusal is null
                ? new(JsonSerializer.SerializeToElement(new { producerNodeId = producer.Node.NodeId, projectionKey = binding.Projection,
                    isOptional = binding.IsOptional, memberPath = binding.MemberPath }), ActivityDataReadRoles.ActivityResultMember)
                : null
            : binding.Scope is not null
                ? new(JsonSerializer.SerializeToElement(new { producerNodeId = producer.Node.NodeId, projectionKey = binding.Projection,
                    producerScopeId = binding.Scope, isOptional = binding.IsOptional }), "ActivityResult")
                : admitted
                    ? new(JsonSerializer.SerializeToElement(new { producerNodeId = producer.Node.NodeId, projectionKey = binding.Projection,
                        isOptional = binding.IsOptional }), "ActivityResult")
                    : null;
        if (member is { Refusal: null } && admitOccurrence && admitted)
            context.MemberReads.Add(member.Facts with { Path = path, ProducerNodeId = producer.Node.NodeId, ProjectionKey = binding.Projection! });
        return new($"activity:{producer.Node.NodeId}:{binding.Projection}",
            output is null ? producer.Node.NodeId : $"{producer.Node.NodeId} · {output.DisplayName}", producer.Parent?.Node.NodeId,
            direct, availability, output?.Type, version, binding.Projection, output?.IsNullable, null, null,
            availability switch {
                ActivityChildResultAvailability.Conditional => "ConditionalOccurrence",
                ActivityChildResultAvailability.Unknown => "AvailabilityUnknown",
                ActivityChildResultAvailability.Unavailable => "UnavailableProducer",
                _ when earlierRun => "EarlierProducerRun",
                _ when noTypedResult => "NoTypedResult",
                _ when member?.Refusal is { } refusal => refusal,
                _ => null
            }) { ActivityResultSource = new(producer.Node.NodeId, binding.Projection!, binding.IsOptional) };
    }

    /// <summary>
    /// Types a member read from the producer's serialized shape. A member the shape proves absent, or one looked up in
    /// a value that has no members, is refused here; a member whose type the shape establishes is known; anything the
    /// shape cannot establish is left for the runtime to check when it reads the member.
    /// </summary>
    private async ValueTask<MemberReadAnalysis> Member(Request context, ActivityDataReadBinding binding, IActivityDefinitionVersion version,
        string path, ICollection<ValidationError> findings, CancellationToken ct)
    {
        var deferred = new ActivityDataMemberReadFacts(path, binding.Key, binding.Projection!, binding.MemberPath, IsKnown: false);
        ActivitySerializedShape? owned = null;
        foreach (var provider in shapeSources)
        {
            var candidate = await provider.DescribeResultAsync(version, binding.Projection!, binding.MemberPath, new(0, 0), ct);
            if (candidate is null) continue;
            if (owned is not null) throw new InvalidOperationException("More than one serialized-shape source owns the admitted data contract.");
            owned = candidate;
        }
        switch (owned?.ReasonCode)
        {
            case "activity.shape.member-not-declared":
                findings.Add(new(path, "ActivityData/UnknownMember", "The producer's result declares no member at this path."));
                return new(deferred, "UnknownMember");
            case "activity.shape.member-parent-not-object":
                findings.Add(new(path, "ActivityData/MemberParentNotObject", "A member on this path is looked up in a value that has no members."));
                return new(deferred, "MemberParentNotObject");
        }
        if (owned is { State: ActivitySerializedShapeState.Known or ActivitySerializedShapeState.Truncated, Type: { } type } &&
            !string.IsNullOrWhiteSpace(owned.ProfileRevision))
            return new(deferred with
            {
                IsKnown = true,
                SelectedType = type,
                MayBeAbsent = owned.Presence switch
                {
                    ActivitySerializedPresence.Required => false,
                    ActivitySerializedPresence.Optional => true,
                    _ => null
                },
                MayBeNull = owned.IsNullable,
                Representation = owned.Representation,
                ProfileRevision = owned.ProfileRevision
            }, null);
        return new(deferred, null);
    }

    private sealed record MemberReadAnalysis(ActivityDataMemberReadFacts Facts, string? Refusal);

    // The request read of one member, or of the whole member when the read selects none.
    private static ArgumentValue RequestRead(string memberKey, ActivityDataReadBinding binding, bool member) => member
        ? new(JsonSerializer.SerializeToElement(new { memberKey, memberPath = binding.MemberPath }), ActivityDataReadRoles.WorkflowRequestMember)
        : new(JsonSerializer.SerializeToElement(new { memberKey, path = binding.Path }), "WorkflowRequest");

    // A member read is admitted wherever the whole read of its source is.
    private static string? ConstraintRole(string? expressionType) => expressionType switch
    {
        _ when StringComparer.OrdinalIgnoreCase.Equals(expressionType, ActivityDataReadRoles.ActivityResultMember) => "ActivityResult",
        _ when StringComparer.OrdinalIgnoreCase.Equals(expressionType, ActivityDataReadRoles.VariableMember) => "Variable",
        _ when StringComparer.OrdinalIgnoreCase.Equals(expressionType, ActivityDataReadRoles.WorkflowRequestMember) => "WorkflowRequest",
        _ when StringComparer.OrdinalIgnoreCase.Equals(expressionType, ActivityDataReadRoles.InputMember) => "Input",
        _ => expressionType
    };

    private async ValueTask<ActivitySerializedShape> Shape(Source source, ActivityDataSelectionDocument document,
        ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (source.ReasonCode == "LegacyRequestPath") return Unknown(null, document.ContractRevision, "LegacyRequestPath");
        ActivitySerializedShape? owned = null;
        foreach (var provider in shapeSources)
        {
            var candidate = source.Version is not null
                ? await provider.DescribeResultAsync(source.Version, source.Projection!, memberPath, limits, ct)
                : source.Type is not null
                    ? await provider.DescribeDeclaredTypeAsync(source.Type, $"{document.ContractRevision}/{source.Id}", memberPath, limits, ct)
                    : null;
            if (candidate is null) continue;
            if (owned is not null) throw new InvalidOperationException("More than one serialized-shape source owns the admitted data contract.");
            owned = candidate;
        }
        var shape = owned ?? Unknown(memberPath.IsDefaultOrEmpty ? source.Type : null, document.ContractRevision, "NoMetadataOwner");
        return shape with { IsNullable = memberPath.IsDefaultOrEmpty ? source.IsNullable ?? shape.IsNullable : shape.IsNullable,
            IsSensitive = source.IsSensitive == true ? true : shape.IsSensitive,
            IsCredential = source.IsCredential == true ? true : shape.IsCredential };
    }

    private async ValueTask<bool> HasNoTypedResult(Request context, IActivityDefinitionVersion version, CancellationToken ct)
    {
        if (context.NoTypedResults.TryGetValue(version.Id, out var cached)) return cached;
        ActivitySerializedShape? owned = null;
        foreach (var provider in shapeSources)
        {
            var candidate = await provider.DescribeResultAsync(version, "$result", [], new(0, 0), ct);
            if (candidate is null) continue;
            if (owned is not null) throw new InvalidOperationException("More than one serialized-shape source owns the admitted data contract.");
            owned = candidate;
        }
        // Only the exact, source-bound absence result is a static refusal. Missing assets/profiles stay uncertain.
        return context.NoTypedResults[version.Id] = owned is { State: ActivitySerializedShapeState.Unavailable, ReasonCode: "activity.shape.no-typed-result" };
    }

    private IReadOnlyList<VisibleVariable> Visible(Request context, ActivityDataSelectionGraph.Location consumer)
    {
        if (!context.Variables.TryGetValue(consumer.Node.NodeId, out var visible))
            context.Variables[consumer.Node.NodeId] = visible = context.WholeVisibility is { } visibility
                ? visibility.GetVisibleVariables(consumer.Node.NodeId)
                : variables.GetVisibleVariables(context.Document.Variables, context.Document.RootActivity, consumer.Node.NodeId, context.MaxDepth);
        return visible;
    }

    private static string NormalizeScope(ActivityDataSelectionDocument document, string? scope) =>
        string.IsNullOrWhiteSpace(scope) || scope == "workflow" || document.ResourceKind == "ActivityDefinition" && scope == "activity-graph"
            ? VariableReference.WorkflowScopeId : scope;
    private static string AuthoredScope(ActivityDataSelectionDocument document, string scope) =>
        document.ResourceKind == "ActivityDefinition" && scope == VariableReference.WorkflowScopeId ? "activity-graph" : scope;
    private static bool RequiresReturn(ActivityDataSelectionGraph.Location producer) => producer.Node.Structure?.Kind is
        "elsa.sequence.structure" or "elsa.parallel.structure" or "elsa.flowchart.structure";
    private static string Path(ActivityDataSelectionGraph.Location location, string key) => $"{location.Node.NodeId}/inputs/{key}";
    private static ArgumentState? CanonicalReferenceState(ArgumentState state, string role)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(state.Value?.ExpressionType, role) || !state.IsBound()) return state;
        // Existing acceptance predicates serialize non-JSON objects. Never hand them an arbitrary CLR instance.
        object? payload = state.Value.Value switch {
            JsonElement json => json,
            SecretReferenceMembers secret when role == CredentialInputBinding.SecretExpressionType =>
                JsonSerializer.SerializeToElement(new { name = secret.Name, typeName = secret.TypeName, scope = secret.Scope }),
            ConnectionReferenceMembers connection when role == ConnectionInputBinding.ConnectionExpressionType =>
                JsonSerializer.SerializeToElement(new { connection = connection.Name, type = connection.Type }),
            _ => null
        };
        return payload is null ? null : state with { Value = state.Value with { Value = payload } };
    }
    private static bool Valid(ActivityDataSelectionQuery query) => query.Skip is >= 0 and <= 10000 && query.Take is >= 1 and <= 100
        && (query.Search?.Length ?? 0) <= 256 && !query.MemberPath.IsDefault && query.MemberPath.Length <= 64 && query.MemberPath.All(segment => segment is not null)
        && query.ShapeLimits is { MaximumDepth: >= 0 and <= 16, MaximumMembers: >= 0 and <= 512 };
    private static ActivitySerializedShape Unknown(TypeReference? type, string revision, string reason) =>
        new(ActivitySerializedShapeState.Unknown, ActivitySerializedValueKind.Unknown, type, revision, "", "") { ReasonCode = reason };
    private static ActivityDataInputConstraint? Intersect(IReadOnlyList<ActivityDataInputConstraint> constraints)
    {
        if (constraints.Count == 0) return null;
        var allowed = constraints[0].AllowedExpressionTypes.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var constraint in constraints.Skip(1)) allowed = allowed.Intersect(constraint.AllowedExpressionTypes, StringComparer.OrdinalIgnoreCase);
        return constraints.Count == 1 ? constraints[0] : new(string.Join("+", constraints.Select(item => item.Owner)),
            [.. allowed], "ActivityData/SourceRestricted", "The selected source does not satisfy all owning input restrictions.");
    }
    private ActivityDataSelectionAnalysis Result(ActivityDataSelectionDocument document, ActivityDataSelectionTargetFacts? target,
        ImmutableArray<ActivityDataSelectionSourceFacts> sources, IEnumerable<ValidationError> findings, bool hasMore = false) =>
        new(document.ContractRevision, target, sources, [.. findings.DistinctBy(finding => (finding.Path, finding.Type))], hasMore)
        {
            MemberReadsEnabled = options.Value.MemberReads
        };
    private sealed record Source(string Id, string DisplayName, string? ScopeId, ArgumentValue? Binding,
        ActivityChildResultAvailability Availability, TypeReference? Type, IActivityDefinitionVersion? Version, string? Projection,
        bool? IsNullable, bool? IsSensitive, bool? IsCredential, string? ReasonCode = null)
    {
        public ActivityResultExpressionParameterBinding? ActivityResultSource { get; init; }
    }
    private sealed class Request(ActivityDataSelectionDocument document, ActivityDataSelectionGraph graph, int maxDepth)
    {
        public ActivityDataSelectionDocument Document { get; } = document;
        public ActivityDataSelectionGraph Graph { get; } = graph;
        public int MaxDepth { get; } = maxDepth;
        public Dictionary<ActivityDataSelectionTarget, ActivityDataSelectionTargetFacts?> Targets { get; } = [];
        public Dictionary<string, IReadOnlyList<VisibleVariable>> Variables { get; } = new(StringComparer.Ordinal);
        public ScopedVariableVisibility? WholeVisibility { get; set; }
        public HashSet<ActivityDataSelectionTarget> ConnectionTargets { get; } = [];
        public DeclaredInputIndex InputIndex { get; } = new(document.Inputs);
        public bool WorkflowBoundary { get; } = document.ResourceKind is "Workflow" or "WorkflowDefinition";
        public Dictionary<string, InputDefinition[]> RequestInputs { get; } = document.Inputs
            .GroupBy(input => document.ResourceKind is "Workflow" or "WorkflowDefinition" ? input.Name : input.ReferenceKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        public Dictionary<string, bool> NoTypedResults { get; } = new(StringComparer.Ordinal);
        public List<ActivityDataMemberReadFacts> MemberReads { get; } = [];
    }

    /// <summary>Defends the single response budget even when a contribution returns more than requested.</summary>
    private sealed class ShapeBudget(int remaining, int maximumDepth)
    {
        public int Remaining { get; private set; } = remaining;
        public ActivitySerializedShape Bound(ActivitySerializedShape shape, int depth = 0)
        {
            var members = ImmutableArray.CreateBuilder<ActivitySerializedMember>();
            var truncated = false;
            foreach (var member in shape.Members)
            {
                if (depth >= maximumDepth || Remaining == 0) { truncated = true; break; }
                Remaining--;
                members.Add(member with { Shape = Bound(member.Shape, depth + 1) });
            }
            ActivitySerializedShape? Child(ActivitySerializedShape? child)
            {
                if (child is null) return null;
                if (depth >= maximumDepth || Remaining == 0) { truncated = true; return null; }
                Remaining--;
                return Bound(child, depth + 1);
            }
            var element = Child(shape.Element);
            var mapValue = Child(shape.MapValue);
            return shape with { Members = members.ToImmutable(), Element = element, MapValue = mapValue,
                State = truncated ? ActivitySerializedShapeState.Truncated : shape.State,
                ReasonCode = truncated ? "ResponseLimit" : shape.ReasonCode };
        }
    }
}
