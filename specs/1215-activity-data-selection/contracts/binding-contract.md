# Activity Data Selection: binding contract

**Status:** Reviewed M2 binding contract; semantic wire approval takes effect on M2 merge. Package API
declarations, implementation and production acceptance remain future delivery work.
**Baseline:** shared spec commit `46b1a830dfb9c218e8388252d2a81baba53ef070`.

This contract implements [the product requirements](../spec.md), uses the [data model](../data-model.md)
and preserves [ADR 0045](../../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md).
It covers source-role-specific immutable **reads**, not destinations or new workflow state.

## 1. Authored wire

Use new reference discriminants for member reads. Adding a path to a recognized old whole-value kind
is forbidden because older compilers/readers can silently ignore such a field.

| `expressionType` | Required `value` fields |
|---|---|
| `InputMember` | `referenceKey`, `memberPath`; lowered at publication to `WorkflowRequestMember` (see below) |
| `WorkflowRequestMember` | `memberKey`, `memberPath`; a legacy dotted `path` is refused, not combined |
| `VariableMember` | `referenceKey`, `declaringScopeId`, `memberPath` |
| `ActivityResultMember` | `producerNodeId`, `projectionKey`, `memberPath`; existing producer `isOptional` defaults to false |

An `InputMember` read names a declared input by its reference key and means the request member read of that
input: at a workflow boundary the request member is the input's name, in a reusable body the boundary input's
reference key. Publication lowers it to `WorkflowRequestMember { memberKey, memberPath }` with the member path
unchanged; a reference key that names no single declared input is refused as `ActivityData/UnknownInput`. A whole
`Input` read is lowered the same way to `WorkflowRequest { memberKey }`. Variable, request and input member reads are
never typed at publication, so an explicit, non-Auto conversion on them is refused.

`producerScopeId` is optional and legacy on `ActivityResult` only. `ActivityResultMember` declares
none, and one that does is refused (`ActivityData/InvalidSource`), at authoring, publication and in the
compiled binding. The data-selection analyzer never writes it: the binding it returns for a browsed producer names only the
producer and its projection. The code-first builder still writes one (#4056). A document that declares a
scope keeps it, its bytes and its hash, and a declared scope must still be a nonblank string.

**Admission of a scope-free direct binding.** Without a scope the producer is identified by causal lineage
and branch and iteration frames alone. The analyzer makes one decision per read, used both to offer a
binding and to admit a stored one, so a binding the client stores unchanged is one publication accepts.
Beside the rules every activity-result read has (producer present and unambiguous, no crossing out of a
structured scope without an explicit return, no cyclic back-edge, a declared projection), it is admitted
only when all of these hold:

1. The producer is definitely available at the consumer, unless the binding says `isOptional: true`.
2. A whole `$result` read names a producer that declares a typed result.
3. The structure that holds both producer and consumer as direct-child subtrees states
   `EarlierProducerRunCanReachConsumer = false` for that pair of children.
4. Every structure above it states `EarlierProducerRunCanReachConsumer = false` for its child that holds
   both, asked with that child as producer and consumer. Repeating that child would bring an earlier
   producer run into the consumer's lineage, as a Sequence on a Flowchart or BPMN loop shows.

A structure that leaves the fact unset counts as "an earlier run can reach the consumer", so a third-party
structure, a Flowchart whose analysis cannot be built and a BPMN process with a gateway all refuse. When 3 or
4 fails, a stored binding is refused as `ValueFlow/EarlierProducerRun` before any publication write; when 1
fails, as `ValueFlow/UnavailableProducer`; when 2 fails, as `ActivityData/UnknownProjection`. Bindings that
declare a scope, and portable expression parameters, keep their existing rules (#4056).

Authored input example under this contract, effective on M2 approval/merge:

```json
{
  "referenceKey": "text",
  "value": {
    "expressionType": "ActivityResultMember",
    "value": {
      "producerNodeId": "get-customer",
      "projectionKey": "Customer",
      "isOptional": false,
      "memberPath": ["customerId"]
    }
  }
}
```

Identity fields retain their existing validation and lexical/causal meanings. The picker always emits
an explicit variable declaring scope, including the workflow sentinel, and stores an activity-result binding
exactly as the server returned it, without a producer scope. Existing legacy whole-variable
documents continue to use their historical default-scope rule.

`memberPath` must contain at least one string segment. Preserve each exact serialized member name;
do not trim, case-normalize, split dots, parse syntax or interpret a numeric name as an array index.
Empty-string member names are permitted when they name an actual object/dictionary member. Bound
path count, total encoded size and traversal depth using the existing supported document/JSON limits;
reject excess explicitly rather than truncate the stored selection. An empty path is represented by
the corresponding old whole-value reference, not a degenerate Member binding.

The server rejects incompatible role fields, client-supplied executable plans, invalid source identity,
an inadmissible target or a Member kind without a valid path. It rejects `memberPath`/selection on a
recognized old whole-value reference rather than quietly treating it as whole-value. These new validation
rules are not retroactive claims about how baseline servers behave.

Code-view Apply and ordinary save preserve new reference objects as objects. In particular,
[activityInputWire](../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/activityInputWire.ts)
must not serialize them as escaped JSON source text. Existing unknown/unsupported definitions remain
losslessly inspectable; inability to edit or execute is reported explicitly.

## 2. Resolve the source, then select

| Source role | Resolution before member traversal |
|---|---|
| Input/request | Resolve the owning input declaration; map its stable identity to the request member of that workflow or closed reusable boundary. |
| Variable/current item | Resolve the existing variable key and declaring lexical frame at consumer input materialization. Current item is the owning structure's existing scoped variable. |
| Activity result | Resolve the existing unique committed producer in the consumer's causal/structural boundary, then its declared named projection or `$result`. |

`InputMember` lowers to `WorkflowRequestMember` when the boundary input identity is resolved. Template
placement must preserve the discriminant and all segments; it must not pass through the current
identity-only `Input` → `WorkflowRequest` lowering that loses additional fields.

New literal member traversal is ordinal over serialized names. Legacy dotted `WorkflowRequest.Path`
and declared output projection paths retain their existing literal-precedence/case behavior; do not
silently rewrite those old contracts. First resolve the declared projection under that existing rule,
then apply the new exact-segment read relative to the projected value.

No source is resolved by display name, canvas coordinates, newest node execution, another iteration or
an arbitrary live object. Cross-scope access still requires the existing explicit optional/return/merge
mechanism. Current-item selection does not change loop cardinality or introduce nested writes.

## 3. Compile the consumer instruction

Append these enum members after existing `RuntimeInputBindingSource.SecretRead` without changing the
old ordinals:

- `WorkflowRequestMember` carries only the existing workflow-request identity payload.
- `VariableMember` carries only the existing variable identity payload.
- `ActivityResultMember` carries only the existing activity-result identity payload.

Each new kind also requires a nonempty immutable read-selection plan described in the data model.
The constructor validates the pair of source kind and identity payload, exactly one role payload, the
plan schema and nonempty path. Old source kinds cannot carry a plan; Member kinds cannot omit it.
The plan is not another source payload and has no mutable/reference/write API.

Retain existing public constructor signatures through forwarding overloads where required; a new optional
parameter alone is not binary compatibility. Track the reviewed Core API changes without changing old
enum ordinals or old serialized defaults.

Compile authoritative source identity, selected member contract and protection before conversion and
hashing. Client metadata is advisory. The closure must cover ordinary workflow compilation, direct
test runs, reusable activity/provider templates and placed template occurrences. A Member operation
on an intrinsic input is the same consumer instruction, not an activity-specific expression shortcut.

Known-member compilation pins leaf type, representation and collection kind, with effective presence and
nullability composed from the root and every member prefix as specified in the data model. A nullable or
optional parent cannot become guaranteed non-null/present merely because its leaf is required. Preserve
per-segment facts or equivalent composed constraints in the immutable plan/hash. Discovery, conversion
planning and runtime admission use that effective selected contract. The producer's declared
result projections and completion policy are not extended with derived fields. Invalid declared paths
or known incompatible contracts fail publication at the consuming input location.

### Deferred fit for unknown or truncated shapes

An unknown shape is not a known missing member. Accept a literal object/dictionary path with clearly
deferred checks, while pinning its identity, source protection, destination contract and allowed
conversion intent. Do not invent a CLR source alias or choose a profile from the eventual value.

For the ordinary Auto mapping, select the JSON value and apply the existing accepted target-fit and
structural JSON admission rules before consumer activation. This includes supported target
deserialization such as Guid/date representations where the existing target contract permits it;
it does not add string-to-number convenience conversion or new coercion. Unknown source facts remain
unknown in inspection even when a target-fit constraint is pinned.

A deferred source shape does not grant arbitrary target-codec support. Ordinary DTO target fit uses the
unchanged activity-value profile. If a custom converter/codec makes the target opaque, its admitted
target-fit capability/profile must explicitly support this boundary; otherwise report unsupported at
publication rather than inventing a reflected DTO shape or coercion. A new general codec registry is
not implied by this contract.

If explicit conversion intent requires a source contract/profile that cannot be pinned safely, reject
that combination with a source/target explanation; do not select a conversion profile at runtime.
Known typed selections continue to use existing explicit conversion settings normally. This does not
adopt the broader proposed ADR 0046 taxonomy.

The existing seams are distinct and require a bounded integration:

- [RuntimeActivityInputMaterializer](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeActivityInputMaterializer.cs)
  materializes and validates presence before returning the input snapshot; it currently lacks a general
  pre-snapshot payload-fit check for these new deferred reads.
- [WorkflowExecutableInputValidator](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowExecutableInputValidator.cs)
  resolves type aliases and validates JSON against declared types. Its current deserialization options
  are not automatically the activity-value profile.
- [ActivityInputHydrator](../../../Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/ActivityInputHydrator.cs)
  applies the current activity profile and CLR nullable-property checks, but runs after constructing the
  activity. It cannot by itself establish the new pre-activation guarantee.
- `WorkflowIntrinsicExecutor.ValidateAssignmentPayload` already uses executable-input validation for
  intrinsic destinations; its source read/selection must use the same pinned admission semantics.

Factor/reuse a bounded internal Workflows Runtime target-fit helper at the materialization boundary using
the unchanged activity-value profile and pinned target/nullability facts, as fixed in the
[technical plan](../plan.md#implementation-seams) for M2 review. Invoke it before a new selected input
snapshot is committed and before constructing/invoking the consumer, including intrinsic inputs. Keep
the ordinary hydrator's existing checks as defense in depth. This is new integration, not a claim that
the existing baseline already performs those checks at the required point.

The pre-activation guarantee concerns the consumer activity's construction/invocation and an intrinsic's
assignment, not arbitrary code in target DTO deserialization. Runtime fit under the admitted activity
profile can invoke DTO constructors/setters or configured converters as existing hydration does. Do not
claim this is side-effect-free metadata inspection. Discovery remains metadata-only and must not perform
that deserialization; unsupported opaque target handling remains the publication rule above.

## 4. Presence, policies and execution order

Materialization follows this order:

1. Validate supported binding/selection requirements on the admitting runtime.
2. Resolve the existing request, variable/frame or causal-result identity.
3. Preserve withheld/transient-source restrictions; combine actual source protection with the pinned minimum.
4. Resolve any existing declared output projection, then traverse exact literal members.
5. Preserve absent versus explicit-null outcomes and the source of absence. A missing optional producer
   follows existing producer-optional semantics; it does not authorize arbitrary missing members.
6. Validate selected presence/nullability/type or deferred target fit, then execute the pinned conversion.
7. Apply existing destination storage/protection and commit the existing immutable input snapshot.
8. Activate the consumer using that snapshot; retries/resumptions retain its existing identity and meaning.

A present object lacking a segment yields `Absent` with segment provenance. An explicit-null leaf
remains `ExplicitNull`. A null encountered before the last segment stops traversal with explicit-null
origin; the composed effective path contract and destination admission decide acceptance rather than fabricating
an object or converting it to a missing key. A non-object/non-map intermediate value fails the read.
Absent required inputs and non-nullable null/absence fail before the consumer is activated. Existing
permitted optional/null and explicit-default semantics remain; this feature adds no implicit fallback.

The producer's successfully committed completion remains intact after consumer selection failure. It
is not rerun to rediscover a field, and the consumer never substitutes another producer occurrence.

Inline and external storage must have equivalent selected values and failures. For an external source,
read the existing protected payload through its store, apply the declared projection and literal plan,
then convert and use existing destination externalization rules. Do not only relabel the whole external
reference as the selected type. Preserve existing storage-profile, retention, redaction and encryption
constraints. Read/missing-payload failures occur before consumer activation and do not weaken protection.

Both `RuntimeActivityInputMaterializer`'s external projection path and
[WorkflowIntrinsicExecutor](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs)
must consume the new plan; changing only the synchronous binding resolver is incomplete. Runtime remains
artifact-only: it resolves no Design store or live schema/provider while traversing a pinned selection.

## 5. Serialization and metadata authority

Use the unchanged activity-value profile extraction and Core dependency boundary in the data model.
Resolve actual serialized names through that profile's contract metadata, respecting `JsonPropertyName`,
`JsonIgnore`, conditional omissions and converter ownership. Nullable metadata is not the same as
presence. A custom converter, recursive bound or unavailable schema yields honest unknown/truncated facts;
it does not expose fictitious reflected fields.

Persisted activity/type contracts remain authoritative for visibility and exact source versions.
Metadata-only discovery, draft diagnostics and compilation consume the same member contract facts.
Discovery must not serialize an example instance, invoke a getter or call user code to infer fields.
No Design project references `Elsa.Activities.Runtime` implementation merely to access its currently
internal options. The proposed extraction adds no general serializer baseline and does not adopt S-9/#3805.

## 6. Hash and compatibility contract

No-path documents and artifacts preserve the old whole-value discriminants, bytes and behavioral hash
rendering. New member kinds and every behavior-affecting plan field participate in the canonical hash.
The existing hasher enumerates source fields explicitly; merely serializing a new property is insufficient.
Use a deterministic framed/structured plan fingerprint so member names containing delimiters cannot
collide. Source labels, editor generation, authorization receipts and diagnostic examples are excluded.
Fingerprint the same selection data in prepared-input caches and template identity calculations.

Declare the engine requirement `elsa.runtime.input-member-selection` schema `1` whenever a
compiled selection exists. This key/version is the reviewed engine-support marker, not a second activity descriptor.
Advertise it through the existing `IRuntimeActivityConsumerCapability` registry beside the implementation,
as the intrinsic engine capability already does. Propagate it through template closure, placement,
retained dependencies, export/import and runnable requirement checks. Old artifacts without a selection
must not acquire the marker merely because a newer runtime supports it.

Import, preflight and distributed placement already use `IRuntimeRequirementChecker`. Add the same
required admission to new local start, resume/recovery and retained-dependency execution paths before
execution/user code; absence of the guard's service cannot mean supported. Preserve the existing keyed
redelivery and pinned-input retry contracts when placing the guard. Reuse one checker rather than
inventing incompatible parallel capability registries.

The new compiled Source enum provides a second, concrete old-reader boundary. In baseline `e8476790`:

- `RuntimeArtifactJson` uses `JsonStringEnumConverter`, so unknown named Source values fail deserialization.
- A future numeric Source can parse as an enum value, but `RuntimeInputBinding`'s JSON constructor calls
  `ValidateCanonical`; its unknown-source default is false and throws.
- The old binding resolver also throws for unsupported Source values.

Thus new Member artifacts cannot silently execute as old whole-value bindings even in an old local
runtime lacking a requirement guard. An older runtime may report a generic unreadable artifact rather
than the newer friendly capability explanation; that is refusal, not a successful mapping.

The authored boundary is weaker and must be reported accurately. An old compiler does not recognize
`*Member`: it falls through to an unknown-language expression, and the old expression semantic validator
can skip an unknown descriptor. Such a server can publish an unusable expression artifact. Its absent
language handler refuses execution; it does not perform a whole-value read. Capability-aware Studio
must not offer authoring against such a server, and the new compiler/publication guard must reject
unsupported/malformed member schemas explicitly. Do not claim that the old server rejects all new
authored documents during publication.

## 7. Existing expressions and graph operations

Headless expression callers supply plain source text, an owned `ExpressionDefinition`, or a `JsonElement`
representing the portable contract. Structured CLR payloads (including dictionaries, anonymous objects
and `JsonObject`) are explicitly refused before executable production: metadata admission cannot safely
serialize arbitrary authored CLR values or invoke their getters to discover hidden portable parameters.
Callers needing JSON submit it as `JsonElement`; literal/object business values retain their existing
value-writing behavior. Every admitted portable parameter still receives the same static source checks.

For explicit transformations, retain existing whole-source portable parameters and let the installed
syntax adapter insert escaped member access. Direct selection does not become generated JavaScript,
and new portable parameter discriminants are not required. Validate every parameter's source identity,
projection and availability even when the syntax provider is installed. Source insertion and its
bindings form one document/history edit; absent syntax modules preserve both.

Rename changes labels only. Copying producer and consumer together remaps their internal producer/scope
identities; copying only the consumer retains a valid external reference or exposes a repairable invalid
one. Member segments never change because a label changes. Reusable placement remaps source identity,
retains the exact selection and requirement, and does not modify immutable source templates. Reads
from variables/current items retain lexical scope; no write target inherits member-read semantics.

## 8. Focused acceptance for this contract

Before adopting implementation, require behavioral evidence for:

- Typed Customer ID and explicit `display_name`, ignored members, nullable/optional intermediate parents
  with required non-null leaves, required-parent controls, unknown suffixes, literal-dot and empty-key
  paths; known absent fields reject statically, unknown literal fields defer checks honestly.
- Whole-value no-change byte/hash controls and distinct hashes for distinct member paths/contracts.
- Three new compiled role kinds, exactly-one payload validation, absent/empty plans and selection attached
  to a legacy kind; no nested write destination is accepted.
- Lossless object wire through save, code Apply and reopen, including InputMember boundary lowering.
- Inline/external and ordinary/intrinsic reads, target fit including supported Guid/date representations,
  strict numeric behavior, supported/unsupported target codec cases and unchanged explicit conversions.
- Constructor-counter and snapshot-write controls: invalid deferred input is refused before constructing
  the consumer or committing its input snapshot, while the valid control activates and runs normally.
- Missing producer versus absent member versus explicit null; consumer failure leaves producer committed.
- Real baseline string-enum and numeric-enum reader controls, unsupported capability refusal through
  local start/resume as well as distributed placement/import, and no optional-language dependence.
- Reusable template placement and pair/consumer-only copy remapping, causal loop/branch isolation and
  persistent restart without producer rerun.

These are future implementation/qualification obligations. Wire approval on M2 merge does not claim a
new test result, implemented package API, merged production behavior or product acceptance.
