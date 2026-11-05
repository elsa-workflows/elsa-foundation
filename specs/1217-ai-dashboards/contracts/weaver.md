# Draft contract: Dashboard authoring through Weaver

**Status:** Draft proposal for [spec1217](../spec.md), not an approved architecture decision, implemented API or provider selection. This contract consumes delivered main Weaver extension points; it does not establish completion of Weaver M4 or depend on unmerged #3845 effects. Candidate identifiers below require plan/contract review before implementation and public API freeze.

## Ownership and composition

The saved-question/dashboard owner supplies its native discovery, validation, preview/execution, definition read/save/history, sharing, drill and supported export operations. Dataset contributors own dataset/field/metric declarations, source authorization, time/coverage semantics, native reads and evidence readmission. The same native operations serve ordinary Studio controls and the Weaver integration; the assistant receives no privileged alternative data path.

The Dashboard Studio module supplies authoring context, prompts, result presentation and the explicit control that uses a validated proposal in its own editor. Backend tools contribute through `IAgentTool`, optional `IAgentToolAuthorization`, `IAgentContextProvider` and `IAgentCapabilityProvider` using the existing named collecting events. The [current Agent catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md) and [SDK contribution guide](../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/README.md) govern discovery and lifecycle.

Weaver owns sessions, transport, policy, review, cancellation, disclosure checkpoints and audit. The host owns generic surface lifecycle and result/review framing. Provider adapters preserve typed module payloads opaquely. No generic engine, host or provider adapter branches on dataset identities, business fields or query operators. An independent dataset contribution uses the same native query and evidence contracts without a new assistant tool per dataset.

Existing Home remains under its current module, layout and preference contracts. This feature adds question-authoring contributions; it neither replaces Home contributions nor takes ownership of #3443's existing Home explanation/layout work.

## Proposed identities and exact binding

The following strings are candidate contribution/protocol identities, not new package or namespace requirements. Use one reviewed resource owner identity consistently in backend descriptors, context, result access and Studio registration. The candidate owner is the existing Studio resource identity `Elsa.Studio.Dashboard`; it creates no backend dependency on a Studio implementation assembly.

| Identity | Proposed value and meaning |
| --- | --- |
| Context-source descriptor | `dashboards.authoring` |
| Context kind/schema | `dashboards.authoring` / `1` |
| Installed guidance kind/schema | `dashboards.guidance` / `1` |
| Saved-question resource type | `question`, with exact persisted question ID and concurrency revision |
| Unsaved resource type | `question-draft`, with random editor draft ID and local working revision; the caller cannot claim it is persisted |
| Dataset discovery tool | `dashboard_discover_datasets`, contract `1`, read-only/server |
| Query preview tool | `dashboard_preview_question`, contract `1`, read-only/server |
| Discovery result payload | `dashboards.dataset-catalog` / `1` |
| Preview result payload | `dashboards.question-preview` / `1` |
| Preview renderer | `dashboards.question-preview` |
| Explicit native editor control | `dashboards.question-preview.use` |

Every authoring operation binds the exact resource owner, type, ID and revision plus originating `{clientId, generationId}` when browser working state is involved. Display names and routes are presentation, never lookup or authority keys. The server stamps authenticated actor/tenant; tool arguments and caller attachments cannot replace them.

Any resource/working-revision input to a tool must match its admitted originating context. Model-authored arguments cannot select a different saved question, browser editor or revision by overriding that binding; a mismatch is a refusal with no draft effect.

For a saved question, the context includes its exact saved revision and, separately, any local working revision. A new unsaved draft uses a distinct draft identity. Preview metadata carries both the exact submitted definition fingerprint and originating working revision/binding. An older preview may be inspected as an older proposal but cannot replace a newer draft or be labeled the current preview. Opening another question or a second editor cannot silently retarget a pending operation.

## Authoring context and trusted guidance

The Studio context provider is narrowly scoped to the question editor and supported named-dashboard authoring surface. It contributes only the active resource reference, current bounded definition/presentation draft, explicit parameter values, relevant selection and client binding. It excludes other tiles' results, unrelated resources, cached author results, credentials and hidden browser state.

The module keeps the most recent proposed definition and user-confirmed interpretation in a separate, unsaved authoring draft envelope, independently from observed result facts. Receiving a preview does not apply or save it, but a subsequent refinement can refer to that proposal before **Use this question**. The envelope records exact originating bindings, parameter values and confirmations attributable to the user's actual choices; generated prose cannot create a user confirmation. Before reuse or transfer to a fresh Weaver session, the native module revalidates this definition/intent envelope against current source/field disclosure. Copy only admitted declarative meaning and parameter values, never old result values, assistant summaries or protected transcript. If part is no longer admissible, explain what must be clarified again while preserving independently admissible native work. Test preview → refine → preview without an intervening Use, including forced old-result readmission refusal and fresh-session recovery.

The installed backend source:

1. Validates its own kind/schema/resource/client bindings and bounded shape; caller origin, sensitivity and labels never establish trust or permission.
2. Resolves the genuine session actor/tenant. For a saved resource, reads the named revision through current native definition authority; client content never substitutes for that read. Unsaved drafts remain client-asserted inputs and go through native query validation.
3. Reads current authorized dataset support through the same native discovery operation as the ordinary editor. It admits only explicitly available query/field/metric capabilities and safe current definition facts.
4. Supplies installed guidance describing query grammar and bounds, module-owned metric/time/coverage semantics, clarification requirements, explicit Save, unsupported behavior and provider-free refresh. Installed guidance is distinct from untrusted question names, user prose and data values.
5. Implements `AdmitDisclosureAsync` for retained context and tool results as defined below. Missing support defaults to refusal, as in the delivered Core contract.

Required owner failures remain required diagnostics. A source unable to decide reports the delivered owner-unavailable form with a safe module code; definite denial is distinct from outage. Discovery failure must never become an empty catalog or a fabricated claim that a module is absent. Permission-filtered discovery must not expose forbidden field names, role membership, record counts or error details.

## Tool contracts and native operation reuse

Both tools use strict versioned JSON input/result schemas and native validation. They declare exact module ownership, disclosure source, required permissions, admitted agent scopes and payload versions. Tool visibility is an availability hint: prepare, execute and result disclosure each retain the platform's current authority checks. Query-side field/record/aggregate authorization remains the dataset owner's responsibility even after host tool permission succeeds.

| Tool | Accepted input | Native work and output | Effects |
| --- | --- | --- | --- |
| `dashboard_discover_datasets` | Optional bounded dataset selection/search and page token within the native discovery profile. No actor/tenant, raw connection, SQL or arbitrary expression arguments. | Invoke native authorized dataset discovery. Return bounded declarations of available fields, dimensions, metrics, operations, relationships, time/coverage support and version identities. Explicitly mark paging and incomplete discovery. | None. Does not resolve model credentials, query hidden sources or create definitions. |
| `dashboard_preview_question` | One definition in the native versioned query/presentation grammar plus typed parameter values and exact originating resource/working revision. The definition can be newly composed; it need not name a developer widget. | Invoke the same native validation and preview/execution operations as the ordinary editor under the authenticated viewer. Return validated definition/fingerprint, authoritative interpretation, effective windows, source/schema/metric versions, observed result/coverage/outcome, bounds and evidence provenance. | Read-only. No Save, ACL change, tile placement, source mutation or provider-triggered refresh schedule. |

The tools do not implement a second query validator or independently calculate module metrics. Saved-resource preview reads the requested current/revisioned definition only through native authority. Unsaved proposals are not evidence that the actor owns, may edit or may share any resource.

Semantic ambiguity that materially changes an answer requires a specific clarification or explicit user confirmation of the proposed meaning before that preview is treated as answering the original question. Native validation may reject an unsupported field, operation, comparison or metric. The tool returns an actionable structured limitation rather than silently selecting another source or inventing a formula. A failure, no denominator, empty result and observed zero retain their native distinct outcomes.

Preview results expose only facts admitted for this viewer and the bounded presentation profile. Provider-visible content includes the admitted interpretation and facts; private authorization provenance is not an execution credential and is never sufficient to fetch another result. Opaque evidence handles, if used, are bound server-side and must not act as bearer authorization.

## Result rendering, explicit use and native Save

The preview renderer consumes the typed native preview payload. It shows proposed versus current definition, source/metric meaning, effective time/comparison/filters, coverage, last successful read, actual result and limitations. It distinguishes generated explanation from authoritative facts and provides an accessible value table/list for charts. Unsupported payload versions use a safe structured fallback rather than fabricated rendering.

The module-owned `dashboards.question-preview.use` control offers an explicit **Use this question** action. The handler:

- Rechecks that the result belongs to the current viewer, module, originating editor/draft generation and applicable working revision, and that it has not been superseded or cancelled.
- Applies the validated query/presentation and exact admitted preview parameter values to the owning ordinary editor. Those accepted values become proposed saved-question defaults, shown explicitly before Save; required parameters cannot disappear into an implicit default. The definition/value fingerprint must still match the preview. Any changed value/default requires revalidation and an explicit repreview/confirmation before it is treated as the accepted answer. Do not import result cache, conversation history, ownership or audience.
- Refuses stale/conflicting targets with a reload/repreview or explicit new-copy path. Copy creates a new editor identity after native access checks; it never silently retargets the original proposal.
- Marks the editor dirty, maintains its normal undo/draft behavior, preserves explicit save semantics and shows that nothing has been saved.

This is an ordinary explicit user action in the native editor. The first profile supplies no model-invoked persistence tool and no AI sharing/delete tool. **Save question**, **Save changes** and **Save a copy** call the same native Save operation used when Weaver is disabled, with server validation, current owner/editor rights and expected revision/create conditions. Save success is established by the native persisted resource/revision response/readback; neither model prose, preview success nor editing the draft establishes persistence. Failed/conflicting Save preserves editable work and never silently falls back to local shared storage.

If a later design adds a model-invoked working-state operation, it must use the existing reviewed `ClientWorkingState` contribution path: exact prepared arguments/target/revision/client binding, `clientTargetAvailability`, observed `executeClient` completion, module-admitted acknowledgement and native undo. It cannot bypass that path by dispatching a browser event and reporting success. Such an addition requires its own contract review; it is not implied by this native control.

## Retained results and provider redisclosure

The source's `AdmitDisclosureAsync` must establish current permission for the **original evidence supplied**. Returning a narrower fresh context or rerunning a query under narrower access does not authorize older aggregate values, labels, rows or assistant text still retained by the provider/session.

The native query evidence contract must bind bounded owner-verifiable provenance to the originating viewer/tenant, exact query/question/working revision and parameter fingerprint, dataset/schema/metric identities, observed time/effective windows and the original disclosed facts/fields. An opaque evidence handle is disclosure provenance, not a shared result cache or permission grant. Its owner must detect stale, missing, removed-module or incompatible provenance and fail closed. Private proof retention/expiry and recovery behavior must be declared by the reviewed query contract rather than assumed here.

The candidate native event is `DashboardQueryEvidenceReadmitting`, aggregated once by `ReadmitDashboardQueryEvidence` from `IDashboardQueryEvidenceReadmissionExtension` contributions. These are proposed names, subject to the native query contract. The event only decides whether original retained facts may be disclosed now; it executes no effects and cannot bless arbitrary provider-authored text. A dataset without verified historical-evidence support refuses reuse beyond its fresh admitted request. No implementation may replace this decision with “the same query still validates.”

Readmission checks current definition visibility when the evidence depends on a saved question; current dataset, field, record and aggregate visibility; exact original evidence provenance; and current host contribution/context policy. Dataset owners decide aggregate visibility, including whether removed record access invalidates a previous group/count. Shared definition permission never substitutes for these checks. History/copy/sharing APIs do not expose retained conversation or result evidence.

On definite loss of access, stop redisclosure and clear protected client content under the native result rules. On inability to decide, report owner unavailable and retain a safe retry path; do not turn it into permission granted or successful empty data. On expired/unsupported evidence, offer a fresh-authoring path.

When historical evidence cannot be admitted, a fresh query in the same provider session is insufficient if that provider retains the old transcript. Start a fresh Weaver session using only the currently authorized definition/draft and installed guidance, then run a fresh query. Do not copy old results or summaries, imply provider-side erasure, or replay accepted Save operations. This recovery must remain explicit and must not discard unsaved native work. Durable session recovery is limited to the composition's declared Agent store/recovery support; default process-local storage supplies no restart guarantee.

The separate proposal/confirmed-intent envelope above supplies safe refinement continuity even before Use. Starting fresh preserves its currently admitted query, interpretation and exact parameter values without treating a protected historical result as authorized. A non-default-parameter preview → Use → Save → reopen test must show the same accepted defaults and effective query meaning; a difference blocks acceptance and requires visible repreview rather than silent fallback.

## Requests, cancellation, retry and recovery

- Use the existing provider-neutral Agent session/prompt/stream/cancel endpoints and prompt dispatcher. Preserve user attachments, correlation, selected resource and originating client binding; no browser call goes directly to a provider.
- The normal native query timeout, row/group/shape/source-traversal and concurrency limits apply equally to Weaver preview. Propagate foreground cancellation through discovery and the owning query operations. A deadline/cost refusal is actionable and never a partial successful aggregate.
- Preview is a bounded read. Cancel, navigation, a newer editor revision or a superseding preview prevents an older response from replacing the current editor/result. A read may already have occurred; cancellation is not a promise to retract provider/network disclosure or undo persisted native work.
- The first profile performs no model-directed persistence, so retrying a failed read cannot duplicate a Save. A Save with an uncertain native transport outcome follows that API's idempotency/readback protocol; the assistant must not repeat it automatically.
- On disconnect/reconnect, inspect native editor and existing session/turn state. Show pending, cancelled, failed, unavailable and completed outcomes truthfully. Do not infer completion from closed transport or replay the original prompt automatically.
- Context or module removal during a pending operation yields explicit unsupported/unavailable/refused behavior. An absent renderer falls back safely; an absent editor prevents applying a proposal; neither condition silently targets Home or a different question.
- Provider failure leaves the native draft and previously saved compatible definition available. Retrying conversational authoring does not retry completed native actions or replace unsupported source references. Recovery may require a fresh session when disclosure authority changed.

## Provider and Connections/Secrets requirements

**Provider decision remains unresolved.** This contract selects no provider, model, endpoint, credential or account and authorizes no credential setup/inference. It contains no OpenAI API-dependent implementation design. A provider decision must identify a genuinely supported setup and its available access before provider-dependent implementation or live qualification.

The chosen integration must preserve the single-active-provider rule and its existing host-driven or delegated loop. An available composition has one active Weaver provider/harness; zero is explicitly unavailable, and multiple selections fail composition. Studio calls only provider-neutral Agent APIs. Provider packages contain SDK-specific transport/authentication; dashboard and dataset modules remain provider-neutral.

Current main Weaver adapters use configured backend authentication, while connection-backed specialist packages do not activate Weaver. Consequently using Elsa Connections/Secrets for Weaver is a real scoped integration requirement or explicit dependency, not an existing capability to assume. The selected adapter/integration must use normal tenant-scoped connection resolution and backend secret access, with a declared supported connection/authentication profile. It must not repurpose specialist orchestration as another dashboard assistant.

Before any provider send, the selected implementation must honor the current connection's supported tenant/owner/status, type/authentication, creation identity/revision, endpoint/allowed-host and credential lifecycle according to its reviewed transport profile. Revoked, removed, expired or incompatible configuration fails meaningfully. A revocation cannot retract an already sent request; qualification must state the actual boundary. Avoid caching plaintext credentials or treating an old successful connection test as current inference authorization.

Secret material never enters Studio requests/configuration, query/dashboard definitions, prompts, context, preview results, audit payloads, screenshots, traces or error detail. Diagnostics disclose only safe provider/setup availability facts. No production credentials, local login, provider account, proxy behavior, SDK retry policy or model capability is assumed supported without inspection and qualification of the chosen configuration.

Provider unavailability or removal affects ask/refine/generation only. Native discovery/edit/validate/Save/reopen/refresh/drill/share and supported export continue through their normal APIs for compatible definitions, subject to current access/source availability. A normal dashboard refresh invokes no Weaver turn and sends no provider request.

## Required evidence

Existing [conformance probe evidence](../../../tools/studio/Weaver.Conformance.Probe/README.md) exercises actual adapters with deterministic protocol clients. It proves relevant integration mechanisms under its recorded source/configuration, not a live model's interpretation or #3604 completion.

| Evidence class | Required observation |
| --- | --- |
| Module/component | Same native validation/execution/Save services serve ordinary controls and Weaver; malformed/unsupported/unauthorized input fails; different scopes and old evidence cannot disclose broader results. |
| Provider protocol | Actual supported adapter and generic engine transport tools/payloads opaquely, preserve exact targets and cancellation, and distinguish previews from persisted outcomes. Deterministic responses remain labeled deterministic. |
| Independent extension | Separate package contributes real business fields/metrics and authorized query/drill/evidence behavior through the same native contracts; no core/host/adapter dataset branch. |
| Live model | Real supported provider asks or confirms a consequential interpretation, composes a novel workflow query, refines grouping/test exclusion, invokes installed validation/preview tools and returns a proposal grounded in independently checked results. Repeat for the explicitly exposed business dimension. |
| Native composed journey | Actual Studio ask → clarify/refine → preview → explicit Save → arrange → reopen → refresh → share → authorized drill. Retain current-commit downloadable screenshots/traces, native persisted definitions/revisions and independent UX inspection. |
| No-provider operation | Remove/disable the provider after Save; ordinary edit/Save/reopen/refresh still succeeds with independently checked values. Observe the chosen adapter/transport's request count and prove it increases by zero during these native operations. |
| Lifecycle/access | Different users/tenants, sharing/edit rights/revocation, current field/record/aggregate scope, historical redisclosure after access changes, source/schema removal, failure/empty/zero/incomplete/stale/cancelled states and current module unload behavior retain explicit results. |

Live evidence identifies exact source/PR head, owned host and disposable stores, safe provider/model/configuration identity, request/tool correlation, declared transport/request-counting boundary and numerical input/result oracle. It excludes credentials and protected production data. A fixture screenshot, successful connection test, adapter unit test, schema validation or unmerged PR cannot satisfy live-model acceptance. Required checks/review, merge and resulting-main qualification remain separate mandatory gates.
