# Activity data selection: authoring contract

**Status:** Reviewed M2 authoring contract; approval takes effect on M2 merge. Research code is not adopted;
implementation and actual product acceptance remain required under [plan readiness](../plan.md#readiness-and-qualification).

This contract implements [spec.md](../spec.md). [binding-contract.md](binding-contract.md) owns the
authored/compiled payloads, member plans, capability/version admission, conversion and runtime failure rules.
The canonical value roles remain in the [Elsa glossary](../../../docs/glossary/elsa.md).

## Input interaction and document authority

The initial product reference is the existing activity property row and workflow-input argument picker,
using [Studio module controls](../../../Studio/docs/module-ui.md). An eligible untouched input offers
**Choose data** without first creating a literal, variable, output capture or expression. The chooser
browses available source groups and declared members. Selecting the root means the whole value; selecting
members means a read from that value. A collection root remains a collection.

One selection commits one canonical input edit through the host's existing document/history owner. Its
readable reference identifies the source and selected member labels, supports source navigation when
resolvable, and offers replace, clear and fixed-value transitions. Cancel leaves the document unchanged
and restores focus. Replacing existing authored content follows the existing destructive-mode-transition
rules; opening or rendering the chooser never coerces the value. Protected or unsupported values remain
preserved under their existing authority.

The chooser is not a second draft, expression document or history owner. It stores only transient query,
expansion and selection state until the host accepts an edit. A response cannot commit to a different
input after activity, resource, revision, backend or identity changes. Graphical save, code Apply and reload
must preserve the same canonical binding rather than turning a reference object into JSON text.

## Eligible targets and equivalent surfaces

Eligibility is resolved from the target's actual contract and installed contribution, not just its text
editor or existing authored value. Declared input metadata admits an untouched target. A catalog lookup
and the owning structure's input projection resolve stable keys, expected types and constraints.

| Surface or target | Contract |
|---|---|
| Native Sequence and Flowchart, root and nested scopes | Ordinary eligible consuming inputs use the same action, source facts and stored meaning. Flowchart availability follows control flow, never coordinates or child-list order. |
| Reusable activity-definition implementation graphs | Reuse the property contribution with the exact activity draft/provider document authority. Public outputs and immutable provider/template contracts remain their owners' contracts. |
| Activity-bearing BPMN nodes | Resolve the bound ordinary activity and use its eligible inputs. Model-owned events, gateways and service controls retain their actual model contracts. A closed published-service boundary does not acquire a native mapping API. |
| Intrinsic consuming-value input | Use its actual input key and selected target type. The variable/output destination remains a write-target selector. |
| Switch case condition | Resolve the structure-owned Boolean target and stable case identity. Persist the condition at its actual collection location; do not manufacture an ordinary input beside it. |
| Wrapped scalar, object, JSON, dictionary or collection input | Permit compatible whole/member reads with authoritative cardinality and conversion. Reuse the existing input expression contribution and fixed-value editor. |
| Unwrapped repeater element | Keep its literal-element contract. Whole-collection mapping and explicit transformation belong at an eligible owning input; do not invent per-element binding storage. |
| Secret-only, Connection, options-provider or custom control | The owning contract/contribution decides permitted sources. A generic picker cannot bypass a secret-reference-only or other constrained source contract. Explain an unavailable source choice without replacing the control. |
| Masked, denied, captured-run, source-owned or read-only document/input | Preserve protected values and actual editing authority. Authorized provenance can be inspected; controls cannot mutate the input or expose hidden source/value data. |

Audit all these rows against normal and optional-module composition. A row is non-applicable only with its
specific target/host contract reason; a visually similar but ineligible control is not a passing mapping journey.

## Source identity, members and compatibility

The source browser has activity results, workflow inputs, scoped variables and any current-item sources
explicitly exposed by the owning structure. Identity is independent of the display label:

- Activity results retain producer node, declared output/projection and structural/causal identity. The
  server returns the complete binding for each activity result the author can select; the client stores it
  unchanged and never derives a producer scope. Which bindings the server returns is the admission rule in
  the [binding contract](binding-contract.md#1-authored-wire).
- A source is selectable when its `binding` is non-null. The reason code is information, not the selection
  test: an optional read of a conditional producer comes with a binding and still carries
  `ConditionalOccurrence`.
- A source listed without a binding carries one reason code, the first that applies, availability first:
  `ConditionalOccurrence`, `AvailabilityUnknown`, `UnavailableProducer`, then `EarlierProducerRun` (the
  structure holding producer and consumer, or one around it, does not state that no earlier run of the
  producer can lead to this input, as on a Flowchart cycle or in a Sequence on a BPMN gateway loop), then
  `NoTypedResult` (the producer declares no typed whole result). Its identity stays inspectable, and
  expression authoring may still bind it as a portable parameter.
- To read a conditional producer optionally, the client sends a focused query whose source is the
  scope-free binding with `isOptional: true`, for example
  `{"expressionType":"ActivityResult","value":{"producerNodeId":"fetch","projectionKey":"$result","isOptional":true}}`.
  Where the rule admits it, the single returned source carries that binding, which the client stores
  unchanged. On a path where the producer did not run, the read resolves to no value instead of failing.
- To read one member, the client sends a focused query whose source is the whole read and whose `memberPath`
  holds the exact segments. The single returned source carries the member read, which the client stores unchanged.
  A member path is never answered with the whole read: while member reads are off the query reports
  `ActivityData/UnsupportedMemberBinding` and returns no source, and a source that has no member read (a scoped
  result read, a legacy dotted request read) reports `ActivityData/InvalidSource`. Every analysis carries
  `memberReadsEnabled`; a client offers member selection only when it is `true`, and an answer without it reads as
  `false`. The flag is meaningful only on a current `supported` or `empty` answer. Clients must not learn or
  update it from an `invalid` answer, including the activity owner's `document-invalid` analysis, which may carry
  the default `false`. A focused member response is selectable only when its canonical role, literal path and
  source identity match the request; a whole binding is never a fallback for a member choice.
- Workflow inputs retain their stable input identity; display-name edits do not rebind them.
- Variables retain declaration identity and lexical scope; same-name/shadowed variables remain distinct.
- Current item is a structure-owned projection of an existing value role with iteration scope. The
  structure supplies that role/identity; the picker does not create an ambient global current-item slot.

Whole-value selections keep existing whole-reference payloads. Member selections emit the appropriate
role-specific authored member kind defined in [binding-contract.md](binding-contract.md). This includes
nested reads from every listed source family; it grants no nested-write capability to variable/output
destinations. Tokens and cached source IDs are presentation, not a new runtime reference abstraction.

Declared member facts distinguish serialized member identity from display and declaration names. They
carry selected type, presence, nullability, cardinality, protection and known/unknown/truncated status.
Use the actual serializer/profile metadata, including explicit names such as `display_name`, ignored
members and declaration nullability. Do not guess a runtime field by lowercasing a CLR name. Provider
schemas and immutable templates must supply equivalent authoritative facts or report their limits.
For a nested choice, compatibility uses effective presence/nullability through the root and all member
prefixes; a required non-null leaf below a nullable/optional parent is not a guaranteed non-null/present read.

For an unknown or truncated object/dictionary shape, the chooser permits keeping the whole source or
entering literal member segments. Each segment names one property/key: a dot inside a key is not an
implicit separator, and segment text is never executable source. Show which checks are deferred. Known
incompatible members are diagnosed; uncertain shape is not asserted compatible. Collections stay whole
unless the author chooses an existing explicit iteration/transformation. No implicit first-item rule,
fan-out, schema inference or evaluated sample is introduced.

Rank known compatible choices and keep the reason for other choices understandable. Type and collection
kind come from authoritative metadata, including element-alias descriptors; raw type-name parsing must
not turn a collection into a scalar. Unknown type and known incompatibility remain different. Frontend
ranking is advisory: the compiler validates the selected source/member type before conversion according
to the binding contract. A failed required deferred member check belongs to consumer materialization,
preserving the committed producer.

## Language-neutral source-facts acquisition

Basic source facts are an independently advertised Workflows Design capability. Its context/search/member
relations must resolve when optional JavaScript/Liquid runtimes and expression-editor modules are absent.
Advertising a language-specific expression-tooling capability does not satisfy this contract. Route and
DTO identifiers remain implementation-owned; capability links, contract version and supported acquisition
modes must be explicit. Reuse existing Design/catalog/structure/authorization mechanisms rather than
introducing another source-fact owner.

The current expression context API is a useful baseline, not the completed source route: its frontend
`getAuthoringContext(document, _state)` sends location/revision identifiers while ignoring editor state,
and its persisted source requires an already authored/projected input state. This omits untouched inputs,
cannot analyze a changed unsaved graph and is coupled to installed expression-type tooling. Extending the
data-selection route must address all three conditions explicitly.

Every analysis request has the following identity, whether transmitted as fields or represented by an
opaque server token. These are source-analysis requirements, not additions to saved input bindings:

| Identity or input | Required server treatment |
|---|---|
| Contract version and acquisition mode | Admit the independently composed language-neutral capability and reject incompatible contracts without partial facts. |
| Resource kind, exact definition/draft identity and expected persisted base revision | Resolve the correct Workflow draft or Activity Definition draft/provider. Verify caller access and current base; a matching node ID in another resource is insufficient. |
| Client document revision and canonical authored-document hash | Echo the analyzed identity. Recompute the hash server-side over the exact accepted authored state using the resource's canonical serializer; a client hash is only a comparison claim. |
| Target node, stable input key and structure-owned location where applicable | Locate the actual eligible contract using the catalog/provider/structure projection, including untouched inputs and synthetic Switch conditions. Do not trust a client expected type or eligibility flag. |
| Optional unsaved authored snapshot | Accept only the existing resource representation: WorkflowDefinitionState or the exact Activity Definition draft/provider envelope. Validate its schema, provider identity/version and target; do not accept a client symbol list, catalog, permission set or arbitrary expected type as authority. |
| Catalog/provider/structure, permission and host-policy revisions | Resolve them server-side, include their identity in the result/context revision, and treat any supplied previous revision as a stale-check claim. Authorization is derived from the authenticated session, never the request body. |

A successful response is metadata only, bound to that request identity and a server context revision. It
contains authorized source/member identities, display information, shape/compatibility facts and supported
limits. It contains no runtime business values, evaluated expressions or producer execution. Paging/search
is bounded and permission filtering occurs before disclosure. A target/type/catalog lookup cannot expose
an otherwise unauthorized resource. Use no-store responses and bounded in-memory client metadata only;
purge that metadata on permission/session/backend changes. Log safe codes, not source paths, symbol names,
authored documents or source text.

A refused source or target is answered with state `invalid` and the analysis that refused it, so its findings
(code and consuming input path) can explain the refusal. That answer passes the same authorization, catalog and
base rechecks as a supported one before disclosure. A client may read its safe findings but must not offer sources
from an `invalid` answer, even when its analysis carries candidates. It does not use that analysis to update member
capability or target facts.

The Foundation leaf does not adapt expression-shaped host metadata filters into neutral policy. When
either `IExpressionAuthoringContextFilter` or `IExpressionAuthoringSymbolFilter` is configured, both
neutral resource endpoints return `unavailable` with `activity-data.metadata-policy-unavailable`, after
their available authentication/catalog authorization checks and before private draft or shape reads.
The legacy expression surface continues to apply those filters. This is an explicit unavailable host
composition, not permission to ignore restrictions or a claim of full neutral-filter support. A neutral
policy adaptation needs normal contract review before chooser freeze; M3–M5 restricted-host acceptance
must retain this distinction. No workflow/language identity is fabricated for an Activity Definition.

Publication and every executable-producing/test-run path independently resolve the same source facts and
validate the actual persisted/promoted candidate. A successful chooser response or matching client hash is
not publication admission. Direct references and portable expression parameters receive the same causal,
scope and static member validation. The current-server missing-producer gap identified by M1 must close
before production acceptance. The old-server/old-runtime compatibility limits in the binding contract
remain explicit; the picker cannot promise a new validation gate in an unmodified reader.

### Saved, autosaving and manual surfaces

1. When current authored content matches the acknowledged saved revision/signature, request persisted
   analysis for that exact revision/target. Ignore the result if any identity changed while it was in flight.
2. Where normal autosave is already enabled and unheld, wait for its serialized queue to acknowledge the
   matching current document. Reuse existing save status, failure and reconciliation behavior. Opening a
   chooser does not change autosave preferences, release review/operation-batch holds or call a save helper
   that would silently write manual edits.
3. Manual, held or equivalent unsaved surfaces use a non-persisting analysis request carrying the current
   canonical authored snapshot and expected base revision. The server authorizes the resource, verifies
   its base and analyzes the proposed state with the same catalog/structure rules. The snapshot lives for
   the request; no draft/store/entity/event or persisted diagnostic record is created by browsing.
4. If the host lacks snapshot analysis, show the explicit **Save and load sources** choice, identifying
   that saving covers the current document. Its cancellation preserves the original input and all edits.
   A host that cannot analyze an unsaved graph cannot label persisted facts as current. First-release
   equivalent manual surfaces must implement snapshot analysis or a deliberate save-and-load experience.
5. A new edit, provider/catalog change, save conflict/failure, target switch or authorization change
   invalidates current facts and outstanding responses. Stale base revisions are reported, not silently
   rebound or persisted. The host retains the latest document and existing reference for repair/retry.

Workflow persistence currently supports enabled/disabled autosave and review/lineage holds through
`useWorkflowPersistence`. Activity Definition drafts have their own revision-checked autosave/provider
envelope through `ActivityDefinitionDraftEditor`. Use each existing owner's acknowledged revision and
document signature; do not equate an expression sourceVersion with a persisted draft revision or introduce
an alternative save path for reusable graphs.

## Expression insertion and editor continuity

Selection in a supported text editor uses the installed syntax adapter's insertion operation. For a
nested member, bind the existing whole-source role/projection as a portable parameter and let the adapter
insert escaped member access; this does not require new portable-parameter wire fields. The basic
language-neutral member binding and explicit expression lowering remain separate contracts.

Choose a legal alias without replacing a different existing parameter. Reuse an alias only when its
binding is identical; otherwise allocate a distinct alias and preserve all existing manual source,
parameters, options and capability profile. Member names containing punctuation, quotes or reserved words
must use syntax-correct escaped access; a source label is never pasted as executable code. If an installed
adapter cannot express the requested access, explain that limit and keep the document unchanged.

Source insertion, binding addition and editor selection/cursor change form one host document operation.
One Undo restores both text and bindings, including any reused binding; Redo reproduces the same result.
Compact and expanded views share the existing editor session rather than mounting two competing owners.
Expansion/collapse preserves source, selection, pending diagnostics and manual edits. Missing modules or
unsupported portable definitions preserve exact authored content and show the existing generic/read-only
fallback permitted by the target; they never stringify, clear or silently convert it.

## Repair, graph operations and observable failure states

Renaming changes labels, not stored source/member identity. Same-label sources show sufficient scope or
producer context to select the intended one. Removing/moving/replacing a producer or changing a contract
invalidates affected facts. Keep unresolved references inspectable and link the affected input to useful
diagnostics and replacement/clear actions; source navigation is offered only when the source still resolves.

Copy/paste traverses direct member/whole bindings and portable expression parameter identities. A copied
producer/consumer pair remaps internal references to the copied producer; a consumer-only or cross-resource
copy preserves a valid permitted external identity or a repairable invalid one. Never bind by matching
labels. Scope changes and impossible external references are diagnosed. Use the existing graph/document
edit transaction so Undo/Redo restores node edits and reference remaps together.

| State | Observable behavior |
|---|---|
| Loading | Keep the current reference visible; show loading and prevent stale selection from committing. |
| Supported empty / no matches | Explain absence versus the current search; preserve the reference and allow search recovery. |
| Incompatible / unknown / truncated | Explain the known mismatch or metadata limit separately; offer supported whole/literal-path options without inventing guarantees. |
| Save/analysis failure or unavailable capability | Preserve input/document, show safe reason and Retry or explicit save-and-load when valid. No replacement with a literal/default. |
| Unauthorized or revoked | Clear cached restricted metadata, cancel reads, preserve protected stored state and show only authorized feedback. No hidden field/editor contains revoked data. |
| Stale/canceled/late response | Do not replace current facts or mutate the input. A canceled chooser restores focus; a current revision can be explicitly retried. |
| Known invalid source/member | Show the affected input and working repair. Publication fails before artifact/publication/activation writes on supporting servers. |
| Deferred member absence/null/type failure | Attribute failure before consumer activity construction/invocation or intrinsic assignment under the binding contract; preserve the completed producer. Runtime target deserialization is distinct from metadata-only discovery. |

All introduced actions work with keyboard/touch and retain accessible names, native focus and cancel
behavior. References wrap/read at narrow widths. Independent acceptance inspects actual composed states
in light/dark, with a focused 320px capture, under [Studio UX acceptance](../../../docs/contributing/studio-ux-acceptance.md).
The finite executable journeys and retained evidence are in [quickstart.md](../quickstart.md).
