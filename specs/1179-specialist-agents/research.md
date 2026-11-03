# Research and decisions: Specialist agents

**Status**: M1 design evidence; production recommendations remain subject to packet review.
Sources were inspected on 31 October 2026. M0 receipts remain bounded research rather than product acceptance.

## R1 — Publication ownership and unpublished discovery

**Decision**: Reuse activity definitions/drafts/publications. Add a narrow temporal provider-membership
query correction rather than another catalog or a client-filtered collection.

**Evidence**: `IActivityProvider` and `IActivityTemplateProviderCompiler` are exercised by the
[M0 package witness](../../tools/agents/publication/README.md). The real reviewed publisher/EF
command persisted an immutable specialist and its exact template. The native package consumer
executed it without Design dependencies; the separate BPMN closure exposed existing coupling.

`Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/Stores/EfActivityDesignStores.cs`
filters definition provider membership using published head/recommendation before count/paging.
`EfActivityManagementProjectionWriter.cs` populates those fields from published versions, so a new
unpublished specialist is omitted. Existing draft projection rows already carry DefinitionId,
ProviderKey, status and validity sequences (`Entities/ActivityManagementProjectionEntities.cs`).
The writer updates parent/draft projection revisions and watermark in the same transaction.

**Implementation consequence**: Correlate a read-visible active draft at the same snapshot and exact
owner tenant/physical scope. Use existing validity intervals and retention markers, never the live
draft table or merely current rows. Apply this before count/page. Global and tenant definitions may
share an ID; visibility alone is not correct owner correlation. Test mixed providers and cursor
retention across all four EF translations. Matching recommendation/head and draft are selected by
their actual exact identities; unrelated published provider metadata is not an agent summary.

**Alternative rejected**: Parallel agent catalog duplicates publication authority; client-side page
filtering loses unpublished rows and correct totals; a new membership store duplicates existing
temporal facts. Source-only providers do not establish mutable specialist CRUD.

The current generic lifecycle retires exact versions; ActivityDefinition has no archive flag.
Initial archive behavior therefore uses explicit version retirement/recommendation, preserving history
and bindings. Definition-wide archive was an ambiguous draft phrase, not a reason to add another store.

## R2 — Domain, packages and MAF dependency graph

**Decision**: Use `Elsa.Agent.Specialists` within `Extensions/Agent`. Separate contracts, Design
provider/compiler and runtime; retain MAF inside the first concrete runtime implementation.

**Evidence**: Elsa constitution §E2.1 pins `Elsa.Agent`, not `Elsa.Agents`. Existing
`FoundationAgentAbstractionsFeature`/`AnthropicAgentFeature` and provider registry implement the
single active Weaver harness. M0 pinned MAF 1.27.0 and demonstrated actual source composition,
but M0/M1 observed a repository MEAI abstractions pin of 10.5.1 while MAF requires 10.10.1.
The normal central dependency enabler [#3544](https://github.com/elsa-workflows/elsa-foundation/issues/3544)
now promotes 10.10.1 with affected locks and current Agent composition checks; this does not by itself
qualify the later production specialist runtime/package closure.

**Implementation consequence**: Review a normal central dependency upgrade, locks and nuspec graph;
run existing Agent provider/turn/tool suites and normal CShells/Nuplane package composition. Core
contains only real models/extension/store contracts with allowed abstraction dependencies. Concrete
model connection providers are independent contributions, with no runtime-to-provider feature reference.
Features remain public/inheritable; implementations are internal sealed unless a real package author
needs the type. Add no placeholder API/provider/EF project.

**Alternatives rejected**: A new root domain contradicts the pinned tree. Reusing Weaver's registry
changes an unrelated assistant policy. Private SDK copies/assembly-isolation tricks avoid the real
consumer dependency graph. An empty MAF adapter hierarchy adds a seam without a second implementation.

## R3 — BPMN package separation

**Decision**: Extract neutral semantic Core and an authoring Design feature; keep existing runtime
package/feature identity. Make Interchange specialize the Design feature.

**Evidence**: Design references in `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/` arise in
`ActivitiesBpmnFeature.cs`, `Models/BpmnAuthoredStructure.cs` and `Internal/BpmnStructureHandler.cs`.
Interchange also refers to runtime constants/activity identities; simply moving the three files does
not create an independent authoring deployment. Runtime `BpmnGraphAdapter` consumes retained-condition
refusal vocabulary. The M0 native consumer is Design-free; its BPMN consumer contains two Design.Core
assemblies, accurately recorded `ObservedUnresolved` at its original source. The
[M4 follow-up](../../docs/reports/elsa-agents-publication-research.md#m4-package-boundary-follow-up)
records the independent package separation proof and its exact candidate; the original M0 receipt is not relabelled.

**Implementation consequence**: Core owns shared graph records and frozen identifiers; Design owns
authored structure/projection/lowering; runtime retains heavy interpreter packages. Preserve aliases,
slot/schema identities and JSON semantics. Forward neutral types where safe; document the authored
type's pre-GA package move without a runtime-to-Design forwarder. Update normal host manifests and
authoring/interchange tests. Prove the actual runtime-only package closure independently of tests
that currently reference Publishing.

**Alternative rejected**: Design depending on the concrete runtime could satisfy one runtime graph,
but keeps authoring coupled and makes feature inheritance awkward. Removing only a ProjectReference
or filtering package contents does not establish the required boundary.

## R4 — Durable conversation acceptance and native delivery

**Decision**: Specialist-owned scoped journal plus existing native background work and outbox. A
facade stages an encrypted journal request; a background-safe MAF leaf returns an opaque receipt
handle. Its native typed result uses `IRuntimeActivityCheckpointParticipant.PrepareCompletionCheckpointAsync`;
structural completion alone carries no typed business result. No new generic checkpoint transaction API.

**Evidence**: `ActivityExecutionContext.OperationKey` is stable across retries. Existing
`IBackgroundActivityWorkStore`, `BackgroundActivityWorkLifecycle`, `BackgroundActivityWorker` and
`BackgroundActivityResultApplication` supply durable admission, worker fencing, receipts and native
checkpoint acceptance. Existing Weaver turn/session/proposal stores are process-local, with no
cross-workflow conversation CAS. `RuntimeCheckpointStateChangeSet` is closed; an enricher does not
admit arbitrary domain writes to Runtime EF's transaction.

Current `BackgroundActivityEligibility` requires a background-safe ordinary leaf with a concrete
registered activation capability, persistable result and no stateful/structural protocols. Worker
results cannot suspend. `BackgroundActivityCompletionCodec` rejects sensitive/encrypted result policy.
Therefore a protected result receipt handle plus normal coordinator-side projection is necessary;
simply annotating the M0 custom activity is insufficient. Register the actual CLR capability alias,
result type and descriptor reader, preserving old artifact interpretation.

Native `RuntimeEncryptionWithholding` currently replaces encryption-required values with an
unrecoverable Withheld value; non-hydrating input preparation also refuses secret-reference inputs.
Initial native support is therefore sensitivity/redaction with `RequiresEncryption=false`. Refuse
encryption-required input/result/capture and secret-reference business bindings at publication/placement
when known and again before dispatch for late-bound policy. Encrypted specialist journal storage is
a separate guarantee and cannot establish recoverable native encryption. See [execution](contracts/execution.md).

**Implementation consequence**: Journal acceptance atomically replaces session/result/usage and
operation receipt under exact scope/revision/unexpired fence. Replays return the accepted operation
without rewinding a newer conversation. A dispatch marker precedes provider work; expiry cannot
prove that work stopped. Explicit unknown outcome blocks a fresh request unless an authoritative
provider continuation/idempotency path or deliberate recovery decision is available.

## R5 — Cancellation is a durable handoff, not a shared transaction

**Decision**: Distinguish TurnAccepted, AppliedToProcess and cancellation settlement. Journal first
writer decides turn acceptance; native checkpoint decides process completion/cancellation.

**Evidence**: `Extensions/Etl/src/Workflows/Runtime/Services/EtlCheckpointEnricher.cs` contributes
domain cancellation intents to the native cancellation checkpoint. Existing post-commit durable
source handlers support idempotent outbox delivery/repair. There is no existing atomic journal+workflow
commit seam. Checking a worker claim before another database's CAS leaves a race.

**Implementation consequence**: Exact-operation cancellation intent/tombstone must block delayed
admission, and may return AlreadyAccepted without erasing conversation history. An accepted turn
during delayed cancellation delivery is completed/not-applied-to-process. Tests must force both
journal winners, delayed native-cancel handoff, lease loss, pre-admission cancellation, and crashes
around journal/native/outbox commits. Next turn follows accepted journal revision, not unaccepted
model history. No cross-store atomicity or universal external-effect exactly-once claim.

**Alternative rejected**: New generic checkpoint participant contracts would expand shared runtime
before this domain use proves necessity. Reusing a process-local session store is not durable.

## R6 — Connections and model capabilities

**Decision**: Deliver a durable implementation of existing `IConnectionRepository`; model connection
and capability contributors remain distinct from the assistant adapters.

**Evidence**: `Essentials/src/Connections/Elsa.Connections/Extensions/ConnectionsServiceCollectionExtensions.cs`
registers the only production implementation, `InMemoryConnectionRepository`. The contract already
defines tenant lookup, TryAdd, version-CAS Save and conditional Delete. The built-in connection type
is HTTP. Secrets persistence does not preserve the connection document. Existing Anthropic/Copilot
options expose different provider-specific settings, not verified universal model capabilities.

**Implementation consequence**: Persistent metadata/auth state and secret references must survive
restart with their CAS/revoke semantics. Resolve at point of use through `IConnectionResolver`;
credentials never enter artifacts. Capability records identify provider/model/revision and explicitly
state structured output, tools, streaming, continuation, sampling/effort, limits and usage support.
Unknown means unknown. Provider adapters and live tests justify each supported setting.

## R7 — Studio reuse and missing surfaces

**Decision**: Module-owned `/agents` collection and companion routes, existing provider editor and
generic public-interface/save/conflict/publish flows, plus a narrow occurrence-link contribution.

**Evidence**: SDK `api.activityEditors` keys implementation editor contributions by provider/schema,
with exact draft revision/fingerprint, contract, payload/layout, readOnly and onChange.
`ActivityDefinitionDraftEditor.tsx` owns exact-revision autosave/conflict/history reset. Provider
onChange updates implementation payload/layout; generic contract editing is separate. The create
dialog intersects backend provider capabilities and installed editor contributions. `ActivityDefinitionsPage`
hardcodes its route/filter state and is not a configurable generic page.

`ActivityDefinitionTestRunDialog` already saves/validates the exact revision and generic publishing
APIs expose dispatch/status/reconcile/cancel. It is not a conversation playground or evaluation store.
Top-level editor tabs and runtime activity-detail sections have no arbitrary provider tab registry.
`ActivityPalettePanel` uses exact catalog version IDs; `BpmnElementInspector` routes bound children
through ordinary inspectors. `WorkflowActivityExecutionDetails` already has exact occurrence identity.

**Implementation consequence**: Specialist form contributes settings; schema constraints remain
coherent with the public interface. Module-owned playground/evaluation/inspection routes reuse
production runtime/candidate identity. Add only necessary owner links, not hardcoded specialist
branches in shared inspectors. Cover draft-only discovery, permissions, source-owned/read-only,
save conflicts, undo/history boundaries, real picker refresh, BPMN bindings, keyboard and 320px layouts.

## R8 — Foundry inspiration and team ownership

**Decision**: Borrow discoverable create/test/version/evaluate/publish/inspect progression; use native
team patterns and actual MAF turn execution. Keep #2912's shared production recovery ownership.

**Evidence**: Microsoft's [agent lifecycle](https://learn.microsoft.com/en-in/azure/foundry/agents/concepts/development-lifecycle?view=foundry)
separates testing, versioning, tools/data, evaluation and publishing. [MAF checkpoint documentation](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints)
describes workflow superstep state; it does not establish atomicity with Elsa checkpoints/effects.
The [reviewed #2913 witness](../../docs/reports/unified-execution/witness-review-2026-10-17.md#agent-collaboration-2913)
contains bounded native/MAF semantic evidence, not production model/effect/host acceptance.

**Alternative rejected**: A second workflow runtime or bespoke team graph is not required by the
specialist product. A future MAF workflow adapter needs an explicit single checkpoint/retry owner
and its own process-failure proof. A mandatory Azure control plane is not implied by inspiration.

## Research disposition

The three planning research lenses inspected package/dependency ownership, runtime recovery and
Studio/support source. The packet resolves the identified design questions with D1–D9, plus D10 for configuration-defined specialists (owner decision, 2 Nov 2026); implementation
must prove the choices. The [support contract](contracts/support.md) names the actual matrix and
remaining evidence. No untested provider/store/host or generic background protection capability is
silently declared supported.

## Implementation reconciliation — #3548

The first real consumer uses JsonSchema.Net **9.2.0**, verified against its installed XML/API and
actual evaluation tests. [Upstream basics](https://docs.json-everything.net/schema/basics/) describe
isolated build registries and explicit dialect selection; current upstream documentation also covers
newer APIs, so the pinned package remains the compile/test authority.

The initial `elsa.specialist.json/1` profile explicitly selects draft 2020-12. It accepts a finite
primitive/object/array vocabulary and bounded acyclic local pointers, expands references before
engine construction, and refuses remote/custom dialects, unknown keywords, regex/combinator/dynamic
reference features and validation siblings on references. It applies byte/node/depth bounds, unique
object members and bounded finite numeric lexemes/exponents. Unsupported constructs are diagnostics,
never ignored constraints. Defaults still come from the generic input boundary; schema annotations
do not apply defaults. A broader vocabulary requires an explicit compatible profile and qualification.

Native checkpoint entry already materializes external input envelopes through
`MaterializeCheckpointInputsAsync` before invoking a checkpoint participant. The same activation then
executes its structural callback and commits the staged continuation. The facade can therefore stage
the request from those portable inputs and checkpoint an opaque handle with child scheduling; no
CLR hydration or new generic input/checkpoint API is needed. A missing journal behind an already
checkpointed handle must refuse recovery rather than silently start another provider call.

Normal `ActivityTemplatePlacer` stamps boundary descriptors with exact definition/version/template
identity and rewrites child slots. Runtime identity must use/verify that pinned material, not a
caller-authored agent version ID. Compiler output already carries `ProviderCompatibilityChanges`,
so schema refinements can strengthen the existing generic compatibility floor without a new diff API.

Member reference keys remain binding/constraint identities; the generic public member `Name` supplies
its JSON field name. Name changes already require a major generic version bump. Display name and
description metadata do not become model instructions. Six causal result-read cases reproduced four
failures before correction: direct and portable-expression reads selected nested content instead of
an exact dotted member, and mishandled the whole-result `$` path. Both readers now preserve completion
projection semantics for those cases while retaining ordinary nested traversal. The whole workflow
runtime suite passed 3,062 tests on the earlier kernel baseline and 3,069 after integrating current
main; the initial whole specialist suite passed 89 cases before review additions. Actual specialist execution also covers a dotted public output followed by a causal read.

The real MAF package graph additionally requires DI/logging/caching abstractions 10.0.12 and the
latter's Primitives 10.0.12 dependency. Normal restore reproduced NU1109 against Foundation's 10.0.10
central pins. This slice raises those four patch pins, introduces MAF 1.27.0 normally, and regenerates
owned locks. The external Weaver protocol fixture must also derive its direct DI-abstraction pin
from the selected producer graph; a hardcoded lower pin would reproduce the prior NU1605 regression.

Provider/compiler/discovery registries are scoped to keep model metadata and existing publication
baseline reads inside the selected operation. The real two-partition registry fixture passes; restoring
all three previous singleton registrations fails for the intended captive scoped dependency. Existing
stateless singleton contributors remain valid. No authoring persistence dependency enters Runtime.

The existing operation-key resolver now delegates to computed, nonserialized properties on native
activity state, so the specialist uses the same assigned/first-attempt/invocation fallback without a
Runtime implementation dependency or a second resolver. The existing typed completion checkpoint
participant is the facade's result boundary; structural completion alone is insufficient.

Reviewed publication and real placement are exercised through three actual SQLite-owned contexts,
existing EF CQS commands, the real filesystem lock and generic publisher/compiler/placer. Their
stored template stays immutable, prompt changes alter behavior without a schema major, constraints
strengthen the version floor, stale review and missing runtime/storage refuse without partial rows.
The authoring fixture is disposed and its databases deleted before native actor/background execution;
all identity stamps and child-node remapping in that execution come from the real placer. This is
source/EF/deterministic evidence, not independent package or live-provider qualification.

CLR reconciliation scans internal activity classes too. `Activity(IsDiscoverable=false)` excludes
implementation-only facade/worker types from standalone catalog discovery while runtime alias
registration remains available. The reflection-only scanner tests cover declared/inherited opt-out
and an ordinary discoverable activity. User specialists remain generic provider-owned definitions.

The initial in-memory journal is an explicit development/test feature. It protects scoped request and
result envelopes, fixes the operation deadline at first staging, admits one worker and fences dispatch
and acceptance. Missing restart state, foreign scope/provenance, altered arguments and stale/late writes
are refusals. Fresh MAF sessions are always new; a published continuity capability never implicitly
restores history. Durable providers, explicit continuation, physical SDK/HTTP retry accounting and
accepted-versus-applied settlement remain M3 work. Independent package proof, final review/CI and
resulting-main qualification remain pending for this implementation slice.


### M2 independent review corrections

The first independent kernel review reproduced incompatible scalar and nested finite constraints,
required nullable output completion, and optional nonnullable input presence. A corrected native
fixture supplies an explicit absent binding; removing the presence mapping still causes the native
run to fault before scheduling. Runtime contracts admit optional absence and nullable projections;
the immutable business schema independently enforces required presence and rejects explicit null.
Nested refinements are checked at every merge and finite values must convert to the declared native
alias, including collection and dictionary values. Output names that could fall back to another
member through native dotted-path projection are refused before publication. A dotted name with no
conflicting output remains supported and has a causal native reader fixture.

Authoring consumes `ISpecialistModelCapabilitiesSource`; execution consumes
`ISpecialistModelClientFactory`. Metadata-only contributors no longer implement a throwing runtime
method. The shared JSON library is an internal helper with scoped registrations in each consuming
feature, without a feature identity of its own. Journal authentication failures retain their original
exception as the cause while exposing a fixed business message. The first correction suite passed
105 cases, including explicit-null and encryption-required input refusal before opening a client.

The first immutable package run at `f3d5cb2` failed at Author restore, after actual product and model
packages built. Its independent consumer selected the old vulnerable SQLite native bundle because it
did not inherit the producer's central bundle pin; a redundant Logging reference also raised NU1510
under warnings-as-errors. The fixture now derives the existing native bundle version from the archived
producer and uses the framework's logging reference. The failed receipt is retained; a new immutable
run must qualify the corrected head. This is a consumer-configuration correction, not a product package
or final support-matrix pass.


The second review found case-insensitive projection aliases and the reserved whole-result name,
additional parser wrappers losing their cause, and root-variable capture destinations whose stronger
policy was discovered after dispatch. Before-fix native/schema cases failed four times while the
ordinary sensitive capture completed. Publication now refuses ambiguous names ignoring case and `$`.
The runtime reads capture policies from the scoped committed root frame at entry and on the worker's
native binding checks; encryption-required or withheld destinations refuse before opening a client.
The model and descriptor parser wrappers retain their original JsonException causes.

The whole specialist suite passes 116 cases. Added native cases establish ordinary sensitivity with
redacted inspection evidence, secret business-input refusal, result/projection encryption-policy
refusal and sensitive-versus-encryption-required root captures. Result-policy cases seed a changed
runtime contract as an artifact backstop; the current generic authoring contract has no policy fields.
These cases do not claim a new public specialist policy editor or the final support matrix. The
architecture suite passed all 592 checks after the new metadata interface was tracked; normal maps,
locked restore and all 163 catalog links also passed. The first architecture run's sole untracked-link
failure is retained separately. Final package qualification and independent review remain pending.


The corrected `4f7034828` immutable run passed product/model packaging, every normal and locked
consumer restore, and all six independent public-API authoring checks. Runtime activation then failed
because the manual fixture enumerated serialization startup tasks without the Events dependency. It
remains a failed qualification receipt. The runtime consumer now composes the ordinary CShells
lifecycle with Tasks, Events, Serialization, Expressions and background/resumption dependencies, plus
explicit filesystem locking and the development journal. The fixture's shell contributes ordinary
runtime services and its deterministic provider; normal startup must register the specialist alias.
CShells and SQLite fixture versions derive from the archived producer's central pins. A new immutable
run is required; this change is not yet a passed host-activation or package receipt.


The `7239d56e6` package run activated CShells normally and passed both runtime journeys (including
12 native checks). Its final assets inspection refused two transitive Design Core packages introduced
by the fixture's optional Expressions implementation. No loaded Design assembly was observed, but
that does not satisfy the stricter package-closure contract. Expressions is unnecessary for this
literal-input profile: ordinary activity startup discovers String/JsonElement from the real facade
and worker. The fixture removes that implementation, retains the closure guard, and adds explicit
startup-type and single chat-assembly checks plus central-pin validation in assets inspection.
The prior script exited with a closure failure while its report still said InProgress; the qualifier
now finalizes all caught qualification exceptions as Failed. Earlier receipts remain untouched.

## R9 — Concrete provider protocol preparation (#3592)

**Status:** Executable research, not production M3 acceptance. The isolated
[provider fixture](../../tools/agents/research/providers/README.md) runs twenty-four cases through actual
MAF 1.27.0, Anthropic 12.32.0 and Azure OpenAI 2.1.0 chat-completion clients, with MEAI 10.10.1,
OpenAI 2.14.0 and System.ClientModel 1.15.0. Normal and locked restores agree on the committed graph.
It uses explicit fixture credentials and an injected HTTP handler; no sockets, live inference,
production specialist connection, Elsa journal or durability is exercised.

**Observed retry boundary:** HTTP 429 produces three Anthropic transport requests and four Azure
requests by default. `MaxRetries = 0` and `ClientRetryPolicy(0)` respectively reduce the API-key
profiles to one. However, Anthropic's changing-token `IAccessTokenProvider` refreshes on HTTP 401 and
sends a second request even with `MaxRetries = 0`. This independent path exists in the
[pinned SDK source](https://github.com/anthropics/anthropic-sdk-csharp/blob/38b988d36f4f0a25f4324387573776fac6adcc4b/src/Anthropic/AnthropicClient.cs).
Consequently, an outer MAF-run counter or disabled ordinary retry setting cannot establish a hard
physical-request budget for every authentication profile. T016/T017 must guard each real dispatch
or refuse unsupported profiles, including caller-added retry/fallback handlers.

**Observed request/result boundary:** For the simple supported-object fixture, both clients serialize the exact dynamic JSON schema,
model/deployment, temperature and output-token limit, return visible JSON and preserve observable
11-input/7-output fixture usage. Both also return malformed business text normally despite the schema
request. Valid JSON accompanied by a truncation signal remains incomplete. Anthropic refusal maps to
`content_filter` with text; Azure refusal maps to an `ErrorContent` with `Refusal` code while its finish
reason is `stop`. Production acceptance must inspect content and finish signals before validating the
complete result against the authored contract. Provider schema forwarding is not enforcement proof.

**Observed schema projection boundary:** Additional bounded-integer cases change the conclusion
for richer contracts. Both actual SDK bindings move `minimum: 0` / `maximum: 100` into descriptions,
leaving the original schema intact. They return both 42 and the out-of-range fixture 101 normally.
The earlier exploratory assumption of verbatim forwarding failed for each vendor; those failures are
retained as scope-specific observations. [Anthropic documents this transformation](https://platform.claude.com/docs/en/build-with-claude/structured-outputs).
[Azure's documented subset](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs#json-schema-support-and-limitations)
also excludes numeric bounds and adds required-property, closed-object and depth/size constraints.
M3 must identify and test the actual request projection, preserve the full original schema for Elsa's
result validation and fingerprint the effective provider/API/SDK profile. A constraint placed in a
description is not enforced by the provider's grammar. No wider accepted contract, automatic result
coercion or unbudgeted repair call is authorized; unsupported semantic transformations must fail
explicitly. These injected responses establish no live-model enforcement or production lowerer.

**Observed content/session boundary:** Anthropic thinking plus visible output becomes
`TextReasoningContent` plus `TextContent`. MAF's business text excludes thinking, but raw SDK session
serialization retains it. M2's bounded text-only path is not adequate qualification of this richer
profile. T017/T020 must define allowed visible/non-business content and a minimal protected
continuation representation. Do not flatten every content type into business output or persist raw SDK
sessions by default. The fixture retains only content-type names and booleans, never raw session or
reasoning values in its report.

**Observed cancellation/uncertainty boundary:** An already cancelled call still enters the injected
handler for both SDKs. The fixture's entry cancellation check rejects it before recording a request;
that is a fixture observation, not proof that the SDK avoided the HTTP stack or that a remote provider
did no work. Cancellation after request observation and controlled response loss each occur after one
request, with an SDK exception. T017/T019/T021 must retain the dispatch marker, distinguish interruption
from accepted/terminal failure and preserve unknown outcome without automatic replay. Protocol-server
tests must count real outbound requests and qualify the actual transport/auth configuration.

**Causal controls:** Enabling default retries, dropping the requested schema, omitting cancellation
propagation and flattening reasoning each makes the full executable exit 1 at its corresponding guard.
Normal restores, locked restores and builds remain successful in those controls. Research output
retains logs, complete package versions, fixture hashes and report hashes separately from product
acceptance. Primary source provenance and reproduction commands are in the fixture README.

**Remaining gate:** T016/T017 remain open. Real connection authorization/allowed hosts, capability
drift, native process/journal recovery, production content/session codecs, actual protocol servers,
streaming/Responses and live model behavior require their own implementation evidence. This spike
does not advance M3 execution past M2 or qualify a packaged/custom runtime host.

## M3 streaming signal follow-up

The [pinned streaming probe](../../tools/agents/research/streaming/README.md) observes normally
returned valid JSON without terminal signals from both adapters, Anthropic `pause_turn` mapped to
coarse Stop, and Azure refusal only on its raw streaming update. Anthropic's final delta can also
erase its start input count. Production profiles must validate raw terminal/refusal meaning before
stripping SDK data and preserve actually observed cumulative accounting. The probe and its terminal
guard control use injected SSE responses with no sockets/live inference. Actual provider transports,
native checkpoints and protected history are qualified separately in the owning M3 draft.

Streaming remains a host opt-in using MAF `RunStreamingAsync`; atomic responses remain the default.
A qualified lease is required, and no partial JSON prefix is a business result. Bounds apply before
MAF aggregation to projected SDK updates, not to the SDK HTTP/SSE parser. Studio partial display,
provider-managed conversations and stream resume are separate capabilities. See the implementing
[runtime contract](../../Extensions/Agent/src/Specialists/Elsa.Agent.Specialists/EXTENSION_POINTS.md#optional-streaming-responses).
