# Activity Data Selection: data model

**Status:** Reviewed M2 design; approval takes effect on M2 merge. The semantic model is specified here;
API declarations and implementation still require normal code review and production acceptance.
**Baseline:** `46b1a830dfb9c218e8388252d2a81baba53ef070`; source findings carried from `e8476790` and
the merged [M1 report](../../docs/reports/activity-data-selection-m1.md).

This model implements [spec 1215](spec.md). Wire rules and execution ordering are in the
[binding contract](contracts/binding-contract.md). Canonical value roles remain those of
[ADR 0045](../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md)
and the [Elsa glossary](../../docs/glossary/elsa.md); this document does not redefine them.

## Ownership and relationships

| Entity | Owner | Relationship |
|---|---|---|
| Authored member read | Existing workflow/activity-definition document | One eligible consuming input selects one source role and a nonempty sequence of literal members. |
| Source facts | Design-owned analysis | Metadata for an authorized document snapshot and target; never business values or independent workflow state. |
| Serialized member shape | Activity/type contract metadata | Describes the actual serialized representation of an already admitted source. |
| Compiled read selection | `Elsa.Workflows.Runtime.Core` input-binding model | Immutable consumer instruction attached only to the three new member-read source kinds. |
| Runtime admission requirement | Executable/template | Declares the engine support needed to interpret the selection instruction. |
| Selected envelope | Existing input materialization | A temporary read result used to create the existing committed input snapshot; no new durable output slot. |

The same narrow immutable selection model and traversal implementation may serve the three read roles.
There is no public universal `ValueSource`, addressable reference, memory register, getter/setter or
write-through view. `VariableReference` and `RuntimeVariableReference` remain identity/target contracts;
using them for an input read does not extend capture or intrinsic write destinations.

## Authored member reads

The enclosing existing `ArgumentValue` keeps `expressionType` and `value`; conversion intent stays in
its existing argument-level location. New names are reserved reference kinds, not scripting languages.

| Authored kind | Identity payload | Additional authored read data | Compiled source |
|---|---|---|---|
| `InputMember` | `referenceKey` identifying the owning document's input declaration | `memberPath` | `WorkflowRequestMember`, lowered at publication: `memberKey` is the input's name at a workflow boundary and its reference key in a reusable body |
| `WorkflowRequestMember` | `memberKey` identifying an admitted request member, open requests included; no legacy `path` | `memberPath` | `WorkflowRequestMember` |
| `VariableMember` | `referenceKey`, `declaringScopeId` using existing lexical identity rules | `memberPath` | `VariableMember` |
| `ActivityResultMember` | `producerNodeId`, `projectionKey`, existing `isOptional` producer-occurrence flag; no `producerScopeId` ([admission](contracts/binding-contract.md#1-authored-wire)) | `memberPath` | `ActivityResultMember` |

`memberPath` is a nonempty ordered array of JSON strings. Each segment is one exact serialized
object property or string-keyed dictionary key. Empty or punctuation-bearing member names are literal
names when the source supports them; segments are not trimmed or split. An empty array uses the
existing whole-value kind instead. There is no wildcard, expression, implicit array index, fan-out,
method call or CLR property invocation in this syntax. A dictionary key `"0"` remains an object key.

Display labels, source breadcrumbs and expanded-tree state are presentation. They do not replace the
identity payload or enter the executable's behavioral hash. New authored reads carry no trusted client
type, policy, presence guarantee or conversion plan; authoritative facts are resolved on the server.

`InputMember` must survive document/code round trips until the owning boundary resolves its stable
input identity. Reusable placement lowers it to `WorkflowRequestMember` and retains its path. Existing
`Input` and legacy `WorkflowRequest.Path` documents keep their current semantics; the new member kind
does not reuse their ambiguous dotted path representation.

## Source facts and serialized members

Source analysis is shared with the discovery contract; it contains these logical facts without
prescribing a second document store or a second source-identity format:

| Fact | Required meaning |
|---|---|
| Snapshot identity | Resource/draft, expected base revision, client edit generation, canonical snapshot hash and target node/input. |
| Authority revisions | Catalog/type-contract, authorization and host-policy revisions used for this result. |
| Source identity | One of the existing input, scoped-variable/current-item or activity-result identities. |
| Availability | Definitely available, conditionally available under existing explicit optional rules, unavailable or unknown, with an actionable reason where permitted. |
| Shape state | Known, unknown or truncated; a truncation is not proof that another member does not exist. |
| Value shape | Type alias/schema identity, collection kind and representation; object members or dictionary value shape where authoritative. |
| Member | Exact serialized name, presentation label, known presence, nullability, cardinality and protection constraints. |

The catalog remains the source of activity/output visibility. Shape reflection augments an authorized
persisted contract; it never discovers additional activities from loaded assemblies. Runtime-only or
policy-hidden information does not appear as a sample or placeholder field. Runtime examples are outside
this metadata model and require their separately authorized future increment.

### CLR serialization and nullability boundary

M1 proves that reflecting `PropertyInfo.Name` is insufficient: `CustomerId` becomes `customerId`, while
`[JsonPropertyName("display_name")]` overrides default naming. The current activity-value profile is
defined by internal
[ActivityValueJson](../../Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/ActivityValueJson.cs):
Web JSON defaults, camelCase property naming and strict number handling. Completion and hydration both
use it. It is not the general `IPayloadSerializer` profile, whose null omission/converters differ.

Propose a bounded extraction of that **unchanged activity-value profile definition** into a thin domain
helper in the existing `Elsa.Activities.Runtime.Core` package. Existing runtime implementations retain
their options/cache ownership and call that helper. CLR metadata extraction uses the same profile to
resolve serialization metadata without referencing `Elsa.Activities.Runtime` implementation. No new
project or general serializer baseline is introduced. S-9/#3805 remains unadopted by this specification.

The dependency changes are bounded and explicit:

- `Elsa.Activities.Runtime` already references `Elsa.Activities.Runtime.Core`.
- `Elsa.Activities.Design.Reconciliation.Clr` already references `Elsa.Activities.Design.Core` and
  `Elsa.Activities.Runtime.Core`; it is the CLR-specific contract extraction implementation.
- `Elsa.Workflows.Publishing` already references both Activities Core packages and Runtime Core.
- `Elsa.Workflows.Runtime.Core` already references `Elsa.Activities.Runtime.Core` and `Elsa.Serialization.Core`.
- If `Elsa.Workflows.Design.Api` needs the profile helper directly, add a direct reference to the existing
  `Elsa.Activities.Runtime.Core` package; never add Design → Runtime implementation. Prefer consuming
  persisted/provider contract shape facts through the existing Activities Design Core seam.

The [technical plan](plan.md#implementation-seams) fixes the proposed package boundary for M2 review:
serialized-shape fact contracts in Activities Design Core, the thin profile definition in Activities
Runtime Core, and shared source-analysis contracts in Workflows Design Validations Core. Reflection,
provider work, analysis and caches stay in the owning implementations. Keep internal by default; expose
only the cross-package contract/thin definition that module authors need. Cross-domain
`InternalsVisibleTo` is not a substitute. Update architecture/maps evidence for actual new edges; no
heavy package or implementation-shaped service is added to Core.

CLR extraction follows effective serializer metadata, including explicit names, ignored/readable
members, applicable ignore conditions and converter ownership. A custom converter whose shape cannot
be established makes that portion unknown. Do not infer its fields by executing serialization on an
example object or by reflecting a shape the converter replaces. Non-nullable reference declarations use
nullable metadata where available; lack of metadata remains unknown rather than universally nullable.
Nullability, required presence and conditional omission are separate facts. Existing getter/converter
behavior is not executed to populate discovery.

## Compiled read selection

The logical name is `RuntimeReadSelection`, owned by Workflows Runtime Core as fixed in the technical
plan. Its semantic fields are fixed here; the concrete API declaration receives normal code review:

| Field | Meaning |
|---|---|
| Selection schema | Version `1` for this literal object/dictionary read operation. |
| Segments | Immutable copied literal member segments, in order. |
| Validation mode | `Known` for an authoritative resolved member; `DeferredFit` for an admissible unknown/truncated suffix. |
| Selected contract | Leaf type/schema, cardinality and representation plus effective presence/nullability composed from the root and every path segment in Known mode. Unknown facts stay unknown in DeferredFit mode. |
| Deferred target constraint | Pinned target-fit contract, profile/codec support facts and existing conversion intent used to validate a deferred selection; it does not assert an invented source type. |
| Protection minimum | The source/output/member minimum established at publication, combined with the actual source envelope at materialization. |
| Contract identity | Deterministic identity of the profile/source contract facts used for this selection; no labels, run values or CLR object instances. |

`RuntimeInputBinding.Source` gains `WorkflowRequestMember`, `VariableMember` and `ActivityResultMember`,
appended after existing `SecretRead` so existing enum ordinals remain unchanged. Every new source must
carry exactly one corresponding existing role identity payload **and** a nonempty immutable selection.
Old whole-value kinds reject an attached selection instead of discarding it. New member kinds reject
a missing/empty/malformed selection. No constructor accepts arbitrary mutually competing source payloads.

Known selections pin the effective selected contract **before** the existing conversion planner runs. A
Customer object is not presented as the source type of its String customer ID. Required presence holds
only when the root and every traversed member are guaranteed present. A nullable root or intermediate
member can terminate the read with ExplicitNull even if the leaf declaration is non-nullable: nullable
`address` plus non-nullable `street` therefore yields a possibly-null `address.street`. An optional prefix
makes the effective read possibly absent. Unknown prefix/suffix facts remain unknown; they never become
required/non-null because a leaf is known. Preserve per-segment facts or equivalent deterministic composed
constraints sufficient for these checks and provenance, and include their behavior in the hash.
Discovery ranking, compilation/conversion and runtime fit use this same effective contract. Existing
destination null/default rules decide whether the actual absent/null outcome is admissible; the path
adds no fallback. Deferred selections pin
the destination's accepted fit checks before execution; the runtime does not choose a conversion mode
or profile from observed business data. See the binding contract for the deferred-fit boundary.

The new plan is behavioral content. Its ordered segments, validation mode, selected or deferred
contract, representation, protection and conversion intent affect the executable hash. Snapshot IDs,
editor generation, labels, display order and API paging do not. No-selection artifacts retain their
existing bytes and canonical hash rendering; an empty plan is not persisted as a new representation
of an old whole-value read.

## Materialized value and presence

Selection produces an existing protected `ValueEnvelope` before the consumer input snapshot is
committed. It preserves source protection and reports distinct causes:

| Source/selection observation | Consumer behavior |
|---|---|
| Required causal producer unavailable | Existing causal-source failure; no substitute occurrence or implicit rerun. |
| Explicitly optional producer unavailable | Preserve existing producer-optional behavior, with producer-absence provenance; do not conflate it with member optionality. |
| Object/dictionary member absent | Preserve `Absent` and the failed path segment; destination presence rules decide admissibility. |
| Selected member explicitly null | Preserve `ExplicitNull`; destination nullability rules decide admissibility. |
| Intermediate value is null | Stop traversal and retain explicit-null origin; use the composed effective path contract and destination admission, never continue by fabricating an object. |
| Intermediate value is a non-object/non-map scalar or array | Selection shape failure; no implicit indexing, coercion or fan-out. |
| Present selected value | Validate the pinned contract/target fit, convert under pinned settings and commit the existing consumer snapshot. |

Required member failures belong to the consumer. The producer's immutable declared completion/projection
contract, fingerprint, result policy and committed execution are unchanged. An accepted null or absence
does not invent a default; existing explicit destination rules remain authoritative. New source
provenance is diagnostic metadata, not an additional durable workflow value role.

## Existing portable expression parameters

The direct member-read schemas do not require new portable parameter kinds. Expression insertion can
bind an existing whole source with `WorkflowRequestExpressionParameterBinding`,
`VariableExpressionParameterBinding` or `ActivityResultExpressionParameterBinding`, then have the installed
syntax adapter insert escaped literal member access. `args.customer["display_name"]` is one JavaScript
example, not an ambient activity-name API or shared syntax rule. The parameter binding and source edit
form one existing document/history operation.

Missing modules preserve existing source and parameters. Static validation still checks the bound
source's identity, projection, scope and availability. No optional language is required for direct
selection, and a generated expression is not the persisted representation of basic member mapping.

## Persistence and compatibility

There is no new database, draft store, result table or independent output persistence record. Existing
document persistence and immutable executable/template storage carry the new authored/compiled data.

Artifacts/templates containing a compiled member-read instruction declare the engine support marker
specified by the binding contract. Requirements propagate through reusable template closure and placement.
New local start, resume/recovery and retained-dependency admission check the same requirements as
distributed placement and import. The new compiled enum discriminants also cause the baseline reader
to reject these instructions before interpreting them as old whole-value reads.

Portable expression parameter contracts and old whole-value source kinds remain unchanged. Literal
member selections are remapped only where source identities move; member names are never rewritten
because a display name changes. Selecting/copying data never changes variable or output write semantics.
