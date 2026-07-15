# Contract: Provider, Publishing, and Runtime Seams

These are backend contract shapes, not final implementation bodies. Design provider contracts and Runtime consumer contracts are separate per framework §2.6.4. Publishing is the bridge that can reference both Core seams; Runtime never references Design or Publishing implementations.

## 1. Stable identifiers

| Identifier | Example | Owner |
|---|---|---|
| Design provider key | `elsa.activity-graph` | Authoring/compilation provider |
| Provider manifest schema | `1` | Design provider |
| Runtime consumer key | `elsa.graph-activity` | Runtime feature |
| Runtime descriptor schema | `1` | Runtime consumer |
| Storage driver key | `elsa.json` | Runtime durable-value feature |

Keys are lower-case namespaced strings. They are persisted/wire identities and therefore follow their own compatibility policy; CLR namespace/type names are not used as substitutes.

## 2. Design provider strategy

`IActivityProvider` is a provider-keyed strategy, registered into an `IActivityProviderRegistry` through the existing Registry + StartUp Task pattern. One provider owns one `ProviderKey`; duplicate keys fail startup.

Illustrative contract:

```csharp
public interface IActivityProvider
{
    string ProviderKey { get; }
    IReadOnlySet<string> SupportedManifestSchemas { get; }

    ValueTask<ActivityContractProposal> ProposeContractAsync(
        ActivityProviderManifest manifest,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(
        ActivityProviderManifest manifest,
        ActivityContract contract,
        CancellationToken cancellationToken);

    ValueTask<ActivityTemplateCompilation> CompileAsync(
        ActivityTemplateCompilationRequest request,
        CancellationToken cancellationToken);

    ValueTask<ActivityManifestMigration> MigrateAsync(
        ActivityManifestMigrationRequest request,
        CancellationToken cancellationToken);
}
```

Rules:

- Contract proposals never mutate the authoritative draft contract.
- Validation and compilation are deterministic for the same canonical request, provider fingerprint, and dependency set.
- Providers return results/diagnostics and do not persist versions, templates, edges, heads, or Source References.
- Infrastructure failures are wrapped as provider-scoped domain failures before leaving the provider feature.
- A provider may support reading/executing old schemas while declining to author or migrate them.

## 3. Compilation request and result

### `ActivityTemplateCompilationRequest`

```text
DefinitionId
DraftId + Revision
CandidateVersion
Authoritative ActivityContract
ActivityProviderManifest
ResolvedDirectDependencies[]
Canonical ProviderFingerprint
CancellationToken
```

Each resolved dependency includes exact definition/version/template identities, public contract, tenant/lifecycle authorization result, and authored occurrence origin. No “latest” resolver is available to a compiler.

### `ActivityTemplateCompilation`

```text
Root ExecutableNode
TemplateLocalResumeTargets
DirectDependencies[]
RuntimeRequirements[]
ResourceMeasurements
ProviderFingerprint
ProviderCompatibilityChanges[]
Diagnostics[]
```

Rules:

- Any error diagnostic means no publishable template result.
- Publishing independently canonicalizes/hashes the result and verifies a provider-reported hash when supplied.
- `RuntimeRequirements` is the exact stable consumer/schema set required to activate the returned nodes.
- `ResourceMeasurements` is evidence for admission policy, not a Foundation limit.

## 4. Graph provider manifest schema 1

The first provider's opaque payload is documented so its own API/Studio consumer can interoperate:

```json
{
  "rootActivity": {
    "nodeId": "graph-root",
    "activityVersionId": "activity-ver-sequence-1",
    "inputs": [],
    "outputs": [],
    "structure": null
  },
  "variables": [
    {
      "referenceKey": "running-total",
      "name": "RunningTotal",
      "type": { "alias": "decimal", "collectionKind": "None" },
      "storageDriverKey": "elsa.json",
      "initialValue": {
        "syntax": "Literal",
        "value": 0
      }
    }
  ],
  "outputMappings": [
    {
      "outputReferenceKey": "total",
      "source": {
        "syntax": "JavaScript",
        "value": "getVariable('RunningTotal')"
      }
    }
  ]
}
```

Rules:

- `rootActivity` uses the existing provider-neutral authored `ActivityNode` shape and exact activity version ids.
- The graph provider owns traversal of its graph/root structure and its output mapping schema.
- Public inputs are not duplicated in the manifest; the authoritative contract owns them and they are seeded into the isolated graph scope.
- Graph-local variables are initialized once at boundary entry and are not visible through the caller's user-variable chain.
- Every required public output must have one valid boundary mapping.
- Trigger entry and workflow-root mutation are invalid in schema 1.
- Natural completion maps to public outcome reference key `done`.

## 5. Runtime descriptor and construction

`ExecutableNode` evolves from `(DescriptorType, DescriptorPayload)` to one `RuntimeActivityDescriptor`:

```json
{
  "consumerKey": "elsa.graph-activity",
  "schemaVersion": "1",
  "payload": {
    "definitionId": "activity-def-1",
    "definitionVersionId": "activity-ver-2",
    "version": "2.0.0",
    "templateHash": "sha256-template-2",
    "entryNodeId": "template-node-entry",
    "requiredInputReferenceKeys": ["order"],
    "requiredOutputReferenceKeys": ["total"]
  }
}
```

The payload contains Runtime execution facts only. It does not contain a Design provider manifest or require a Design store.

`IActivityConstructor` becomes stable-key/schema driven:

```csharp
public interface IActivityConstructor
{
    string ConsumerKey { get; }
    IReadOnlySet<string> SupportedSchemaVersions { get; }

    ValueTask<IActivity> ConstructAsync(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument> inputs,
        IReadOnlyDictionary<string, OutputArgument> outputs,
        CancellationToken cancellationToken);
}
```

The registry key is `(ConsumerKey, SchemaVersion)`. Duplicate claims fail startup. Unknown/unsupported consumers become `ActivityResolutionException`-family domain failures that Runtime classifies as artifact-activation incidents.

## 6. `GraphActivity` runtime behavior

`GraphActivity` is an ordinary `IActivity` and uses existing execution-context operations. It does not load a child workflow or synchronously execute a nested object graph.

### Entry

1. Confirm the descriptor/template identity matches the pinned executable node.
2. Materialize only absent caller bindings from compiled defaults.
3. Evaluate every effective input once.
4. Capture every input into Durable Values owned by the outer `ActivityExecutionId`; fail before descendant scheduling if capture fails.
5. Initialize graph-local durable variables once.
6. Mark/defer the outer activity and schedule the template entry node with `ExecutionScopeId = outer ActivityExecutionId`.
7. Commit state changes and first scheduler intent atomically.

### Descendant operation

- Descendants are normal scheduler work and normal activity executions.
- The nearest activity execution scope supplies read-only public inputs and graph-local variables.
- Ambient runtime identity/services/tracing/time/cancellation remain available.
- Workflow-root mutations and trigger-entry operations are rejected by scope capability policy.
- Descendant bookmarks/timers/incidents retain native ownership.

### Exit

1. Detect natural graph completion through ordinary child completion.
2. Evaluate compiled boundary output mappings against durable internal values.
3. Validate and durably capture every required output.
4. Record `Done`, terminalize the outer activity, and enqueue parent continuation in one checkpoint.

### Fault/cancellation/retry

- Preserve original inner incidents and record a causal outer boundary incident.
- Cancellation fences new descendant scheduling, removes descendant bookmarks/timers/pending work, then terminalizes the outer only after cleanup commits.
- Retry creates a new outer activity execution and descendants while preserving template identity and the effective captured input snapshot through explicit retry input provenance.

## 7. Placement and identity contract

Publishing places a template under an invocation-origin path composed from length-framed segments:

```text
(kind byte, byte-length, UTF-8 identity bytes)*
```

Examples of segment kinds: workflow root, authored placement node, template boundary, nested placement occurrence. The full SHA-256 of the canonical byte sequence is used as the namespace component for:

- placed executable node ids,
- placed resume-target ids,
- boundary layout segment ids.

Readable `InvocationOrigin` segments are retained separately for diagnostics/inspection. Hash collisions are rejected loudly; there is no truncation or fallback identifier.

Subtree stability requirements:

- identical source + same invocation origin -> identical placed ids;
- same template at a different occurrence -> different placed ids;
- changing one subtree origin changes only that subtree's placed ids;
- unrelated subtree ids remain stable;
- traversal is iterative and cancellation-aware.

## 8. Admission policy

Compilation emits measurements such as local node count, closed node count, dependency count, maximum observed authored depth, descriptor bytes, layout bytes, and estimated durable boundary slots.

An `IActivityTemplateAdmissionPolicy` replacement contract evaluates these measurements for host/tenant context. The default Foundation policy records measurements and accepts; hosts may reject with structured `activity.admission.rejected` diagnostics. This is policy, not executable identity, and does not change behavior hashes.

## 9. Preflight and activation incidents

Publishing records exact `RuntimeRequirement` pairs on templates and workflow artifacts. Preflight joins active retained artifacts to the current Runtime consumer registry and reports `Available`, `Missing`, or `UnsupportedSchema`.

If execution still encounters a missing requirement:

- the outer/affected activity records an artifact-activation incident with artifact, node, consumer key, and schema;
- ordinary retry policy does not retry it as an activity failure;
- the workflow remains recoverable after deployment correction;
- no Design provider or source is loaded as fallback.

## 10. Architecture boundaries

- `Elsa.Activities.Design.*`: provider-neutral authoring contracts, provider manifests, drafts/versions, validation, persistence, catalog API.
- Graph Design provider: manifest schema, contract proposals, validation, compilation, migration.
- `Elsa.Workflows.Publishing.*`: the only bridge that reads Design contracts/templates and creates Runtime executable material/source references.
- `Elsa.Activities.Runtime.Core`: stable Runtime descriptors, constructor/consumer registry contracts, activity contracts.
- Runtime graph feature: `GraphActivity` and its stable Runtime consumer; no Design or Publishing reference.
- `Elsa.Workflows.Runtime.*`: scheduler, pipeline, checkpoint, stores, artifacts, source references, execution, and inspection.

Architecture tests reject every Runtime -> Activity Design, Workflow Design, or Publishing implementation reference and specifically inspect the graph consumer/construction path.
