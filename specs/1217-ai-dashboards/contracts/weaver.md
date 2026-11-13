# Contract: Dashboard authoring through Weaver

**Status:** Implementation contract for [spec1217](../spec.md). On 6 November the owner resumed #3604 and selected one model-agnostic dashboard author over Weaver, using the existing Copilot SDK adapter. The subsequent owner configuration choice is backend options/configuration, including appsettings/environment. This is design and implementation scope; live provider, composed UX, review, merge and resulting-main acceptance remain separate gates.

## Ownership and composition

`IDashboardAuthoringService` and its SDK-free draft/context/proposal/event records live in Studio Dashboard Core. Exactly one optional `WeaverDashboardAuthoringService` implements it. The service prepares current native context and delegates conversation to the existing Weaver session/streaming services. It neither creates a model loop nor implements an author per model. The existing Copilot `IWeaverProvider`/`IWeaverHarness` owns SDK orchestration. Other Weaver adapters can serve the same module contracts without another dashboard author implementation. Semantic Kernel and another orchestration layer are outside this change.

The optional backend package is `Elsa.Weaver.Dashboard`; shell feature `StudioDashboardWeaver`, capability `elsa.api.studio-dashboard-authoring` contract `1`. It contributes through existing `IWeaverContextProvider`, `IWeaverTool` and `IWeaverToolAuthorization` contracts. The owning Dashboard Studio module registers public SDK context/prompt/result contributions and its explicit native Use control. No frontend satellite is needed for those owning-editor contributions. See the [Weaver catalog](../../../Extensions/Weaver/src/Elsa.Weaver/EXTENSION_POINTS.md) and [SDK guide](../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/README.md).

Dataset modules own declarations, validation, execution, source authorization and interpretation. Generic Weaver, the host and the provider do not branch on dataset identities or business fields. Home widgets/layout/preferences remain under their existing contracts; #3604 does not establish completion of Weaver M4/#3443 or reactivate its separate work.

## Provider disclosure profile

The first authoring profile sends the model authorized dataset declarations and declarative questions only. It sends **no source records, computed aggregates, result tables, cached results or numerical summaries**. A tool validates the proposed question; Studio then executes and renders its actual preview natively as the current viewer. The programmatic authoring facade can enrich a caller event with a native preview, but never writes that preview back into the Weaver transcript. The model discusses supported meaning and query composition, not numerical findings or causes.

This profile deliberately requires no historical aggregate-evidence readmission extension. It must reject a retained proposal carrying a non-null result preview. If a future feature sends protected result facts to a model, it needs a reviewed original-evidence provenance/readmission contract; fresh validation or a narrower query never authorizes older broader values. No such extension is silently assumed here.

## Identities and binding

| Contribution | Identity/version |
| --- | --- |
| Resource owner | `Elsa.Studio.Dashboard` |
| Context source and draft kind | `dashboards.authoring` / `1` |
| Installed guidance | `dashboards.guidance` / `1` |
| Saved resource | `question`, exact saved ID and resource revision |
| Unsaved resource | `question-draft`, random editor generation ID and local revision |
| Tools | `dashboard_discover_datasets`, `dashboard_preview_question`, `dashboard_request_clarification`; contract `1`, read-only/server |
| Payloads/renderers | `dashboards.dataset-catalog`, `dashboards.question-preview`, `dashboards.clarification`; schema `1` |

Every operation binds module/type/ID/resource revision and originating `{clientId,generationId}` plus separate working revision. Display name/route are presentation. Session ownership is checked on plain owner columns before session hydration. Weaver's authentication subject and the canonical Dashboard user ID are distinct: the adapter compares each only in its owning contract. Actor and tenant are server-derived; model arguments cannot select them or a different editor.

Input/result data is bounded at 24 KiB, versioned and validated strictly. Missing/ambiguous origin, malformed shape, changed saved revision, unavailable definition disclosure or stale browser revision refuses authoring or Use. A source unable to decide emits a required owner-unavailable diagnostic, distinct from definite denial. Discovery failure cannot become a successful empty catalog.

## Native context and tools

The client contributes one active question editor snapshot. Retained tabs cannot win by insertion order; an ambiguous active snapshot refuses collection. Context includes the bounded declaration/draft, explicit parameter values and actual user-confirmed interpretation, never result data. A per-registration envelope can retain the latest proposed definition before Use only while its exact originating editor/working revision remains unchanged. Navigation, access loss and superseding work cannot retarget it. User names, descriptions, labels and prose are untrusted data, not installed instructions.

Backend collection rechecks session identity, saved resource ACL/current revision, native query/field/metric validity and currently authorized discovery. It contributes its own installed grammar/meaning/clarification guidance. Caller origin/sensitivity/contributor strings confer no authority.

| Tool | Input | Output/native work |
| --- | --- | --- |
| Discover | Optional exact `datasetId`; no actor, tenant, connection or executable input | Current authorized bounded dataset/field/metric/version/time/coverage declarations. No records/counts. Oversize is an explicit refusal, never silent truncation. |
| Preview question | `question`: native query, visualization and parameter values | Native validation under the current actor. Unsaved proposal with exact target/client/working revision, definition and fingerprint, supported interpretation. `preview` is absent. No effect. |
| Clarify | One bounded question and two to four distinct `{id,label,description}` choices | A bound clarification. Success is not user confirmation. Only a subsequent actual user answer supplies confirmed interpretation. |

Prepare freezes the admitted origin and strict arguments; execution rechecks actor/tenant/session/target/client and current native support. Host tool policy is additional to the module's authority. The tools calculate no metrics, change no definitions/grants, save nothing and expose no SQL/scripts/filesystem/shell/external database operation.

Clarify meanings that materially alter an answer: started-cohort current failure outcomes versus failure events, current folder versus folder totals, elapsed periods/timezone, run kind and explicit business dimensions. Never invent missing fields/history or substitute another source. Native source limitations, zero/empty/no-denominator and unknown coverage remain visible.

## Preview, Use and Save

The renderer binds the declarative proposal to its originating live editor. It calls ordinary native preview as the current viewer; a saved-resource proposal uses `POST questions/{id}/preview` with `If-Match` so definition revocation/revision is checked before execution. Source authorization is independently current. Actual result values, coverage, effective periods, refresh time and accessible tables remain native UI state and are never attached to a Weaver request.

Cancel, unmount, access/scope loss, superseding preview and working-revision change abort/clear protected preview state. An absent originating editor prevents Use. A renderer cannot reuse old results as a current result or infer an answer from transport completion. Compatible native results/edits remain available when the provider is unavailable.

**Use this question** is an explicit native user action after current validation/preview. It checks exact origin and working revision, applies the whole validated definition and parameter defaults to the native draft as one Undo step, and reports nothing saved. The server SHA fingerprint and the client's local definition identity are different formats; do not compare them as equivalent. Changed defaults require revalidation/repreview. No result cache, ownership, ACL or transcript is copied.

**Save question/Save changes** use the ordinary native resource API with current authorization, validation and CAS/readback. **Copy saved version** clones the exact authorized persisted revision privately. **Save draft as new question** preserves dirty/conflicted authored work through fresh source access/native validation followed by a keyed private native Create. It is a new question, not an atomic clone of the source revision; execution always has independent current source authority. Nothing imports source grants, results or history.

The first profile contains no model-invoked browser mutation or persistence tool. Future direct AI effects must use the existing reviewed ClientWorkingState/server proposal contracts rather than browser events that claim success.

## Retained disclosure and recovery

Before each provider continuation the original installed source re-admits retained declarations/drafts/proposals. It checks current session ownership, saved-definition access/current revision and exact original declarations against currently allowed metadata. Withdrawal or changed meaning of any previously disclosed declaration refuses the old transcript. Retained proposals must match provenance/target/definition fingerprint, contain no result preview and pass current native validation. A fresh narrower catalog is not proof that older broader declarations remain safe.

When retained disclosure is refused or its owner unavailable, stop provider entry. Offer a fresh conversation using only currently admissible native draft/intent and installed guidance. Do not copy prior results or summaries, imply provider-side erasure, retry Save or discard independent native work. Module/schema removal and provider outages stay actionable. Weaver store composition defines conversation durability; the default process-local store gives no restart guarantee.

## Provider configuration and server boundary

The owner selected the existing GitHub Copilot SDK adapter and backend options/configuration. The earlier draft requirement for mandatory Elsa Connections/Secrets integration is superseded. Reuse its `GitHubToken`, `GitHubTokenEnvironmentVariable` (default `COPILOT_GITHUB_TOKEN`), explicitly selected `UseLoggedInUser`, or explicitly configured runtime URL/token. No local login, production credential, account entitlement or unset environment variable is assumed usable. Secrets stay backend-only and out of logs, prompts, definitions, browser configuration and evidence.

Preserve the single-active-Weaver-provider rule. Studio calls generic Weaver APIs. SDK types stay in the Copilot adapter; the Dashboard Core/service/tools stay model agnostic. The adapter uses the pinned SDK's Empty mode and an explicit list of installed policy-guarded custom tools, disabling ambient CLI instructions/tools/plugins/memory/environment. Spawned SDK runtime state is partitioned by trusted tenant beneath the configured backend base directory; diagnostics uses a distinct partition. For an explicitly configured external runtime, the SDK ignores that base directory and the runtime operator owns its storage isolation. Session owner authorization remains required before every resume.

Configuration is read at the adapter's supported options/request boundary. Disabling the provider prevents subsequent conversational turns; it cannot retract an in-flight network request. SDK-internal requests/retries are not claimed to enforce per-physical-request credential revocation. Refresh/edit/save/reopen/drill/share use native operations and increase provider calls by zero.

## Qualification

Keep deterministic contracts/engine tests, independently checked native numeric results, actual provider turns, composed Studio journeys, independent visual acceptance, exact-head CI/review/merge and resulting-main evidence distinct. A fake provider, successful ping/schema, fixture screenshot or unmerged PR cannot satisfy live-model acceptance. Include the independent business dataset witness and every story's forbidden-data/recovery controls. Evidence records safe provider/model/configuration metadata and observed counting boundaries, never credentials.
