# Contract: Executable artifact export endpoint (FR-B-010a v1 target)

**Project**: `Elsa.Workflows.Publishing.Api` · **Pinned for**: elsa-foundation-studio#493

## Endpoint framework: FastEndpoints (architect exception to ADR 0068, 2026-08-17)

**Decided by Joey, 2026-08-17.** The resolution moved three times; all three states are recorded
because the reasoning matters more than the outcome.

1. **2026-08-15 — FastEndpoints, under ADR 0068's capability-gap exception.** No shell-scoped Minimal
   API mapping seam existed for Elsa module features; the seam was owned by #1345 and unlanded.
2. **2026-08-16 — Minimal API.** The 2026-08-16 `main` merge landed the migration's waves 1 and 2 and
   with them the mapping seam (`IWebShellFeature.MapEndpoints`). The capability gap closed, so no
   exception was available under the ADR's own test.
3. **2026-08-17 — FastEndpoints, as a deliberate architect exception.** The step-2 reasoning rested on
   an overstatement: waves 1 and 2 landed, but **`Elsa.Workflows.Publishing.Api` was not in them.** It
   still carries live rows in `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json`
   and every one of its ~20 endpoints is FastEndpoints.

**Why the exception was taken.** One Minimal API route in an otherwise wholly-FastEndpoints module
would differ from its siblings on problem-detail shape, permission mechanism, endpoint metadata and
test approach — for a single route, in a module that will migrate as one wave regardless. The
deviation's whole lifetime would be "until the wave arrives", and it would be the only route in the
module needing its own coexistence proof.

**Be precise about what this is.** ADR 0068 makes Minimal APIs normative for new first-party endpoints
and grants exceptions only on a capability gap. That gap is closed. So **this is not compliance — it is
a recorded architect exception on consistency grounds**, and a reviewer reading only the ADR should
expect to find it flagged. Recorded here for exactly that reason.

**Consequence: [T091a](../tasks.md)'s open rule question is now live.** While the route was a Minimal
API the question was moot. It is not: *does a route added to a module that is already wholly
transitional — inheriting its existing `removalOwner` and wave — require a fresh approved compatibility
exception, or is it bookkeeping under the module's existing entry?* **This feature assumes bookkeeping**
(see the registry section below). If the ADR owner rules otherwise, an approving reviewer and linked PR
must be recorded on the new registry row.

### What the FastEndpoints choice restores

The two structural constraints the Minimal API route would have imposed are gone: the feature class
does **not** implement `IWebShellFeature` (so no dual shell-interface dispatch, and no regeneration of
`docs/maps/feature-map.md`'s authoring-model row), and no `WithOwner` / `WithAuthoringModel` /
`EndpointAuthoringModels.MinimalApi` metadata is required. `ConfigurePermissions(...)` supplies the
security disposition the manifest builder demands, as it does for every sibling.

Two constraints still hold and are unrelated to framework choice: **no member of any class under
`Endpoints/` may be named `Configure`** other than FastEndpoints' own override — `EndpointSecurityTests`
scans that folder and demands exactly one `ConfigurePermissions(...)` call from any class defining one —
and **nothing in the repo cross-checks a capability `Href` against a registered route**, so the
endpoint's own tests must assert that the advertised href and the mapped route agree.

<details>
<summary>Recorded for the archive: the dual-interface question, answered before the reversal made it moot</summary>

The Minimal API attempt required `WorkflowsPublishingApiFeature` to implement both
`FastEndpointsFeatureBase` and `IWebShellFeature` — unprecedented in this repo. **That combination
works.** Proven by a test (green at the time, since deleted): a `HostBuilder().ConfigureWebHost(UseTestServer)`
that ran the feature's `ConfigureServices` once, then called **both** `MapFastEndpoints(...)` and
`((IWebShellFeature)feature).MapEndpoints(endpoints, null)`. Both the new export route and the existing
FastEndpoints `publishing/incident-strategies` route resolved — 401 unauthenticated, where an unmapped
route would 404 — both appeared in `EndpointDataSource` with correct authoring metadata, and an
authenticated GET returned 200 with `Content-Disposition: attachment` and a body that round-tripped
through the production closure codec. Corroborating: `CShells.AspNetCore.dll` references
`IWebShellFeature` and `IMiddlewareShellFeature` and contains **no reference to
`IFastEndpointsShellFeature`**, so the FastEndpoints interface cannot exclude a feature from the web
pass. Caveat: the test drove `MapEndpoints` directly rather than through CShells' own `MapShells()`, so
"CShells itself calls it" still rests on that metadata.

Kept because it settles the question cheaply if the Minimal API route is ever revisited — most likely
when Publishing.Api's migration wave arrives.

</details>

## Capability advertisement

Added to `PublishingApiCapabilities.StaticDeclaration` (capability id **`elsa.api.publishing`**):

```json
{ "rel": "workflow-executable-export",
  "href": "publishing/workflows/{versionId}/executable-export",
  "templated": true }
```

Rel is kebab-case, no dots (sibling of `workflow-executable-provenance`); href is shell-relative and
mirrors the route exactly; `contractVersion` reviewed on add (additive link → no major bump expected).

**Nothing in the repo cross-checks a capability href against a registered route** — `DomainApiCapabilityRegistrationTests`
asserts only that hrefs are non-rooted and non-absolute, and the OpenAPI fragment enumerates capability
*ids*, not rels. A rel whose href resolves nowhere would ship silently, so the endpoint test must assert
the advertised href and the mapped route agree.

## Route

```
GET publishing/workflows/{versionId}/executable-export
```

- `{versionId}` uses the existing `RouteConstants.VersionIdConstraint` (`regex(^(?!drafts$).+$)`); route constant added to publishing `RouteConstants`.
- **GET serves the `download` target only.** GET is a safe method; receipt-producing targets (blob push, folder write) are external side effects that crawlers, retries, and caches may repeat. There is no target selector on this route in v1. When a side-effecting target ships, it arrives with its own **POST command endpoint** carrying an explicit idempotency contract — defined with that feature, not here.

### Permission: reuse `PermissionNames.WorkflowPublishingRead` — no new permission

The task list called for a *new* read-shaped permission distinct from `WorkflowPublishingManage`. Two
findings overrode that, and the second is the substantive one.

1. **A `.export` suffix is forbidden by a gate.** `EndpointSecurityTests.Management_permission_names_are_stable_unique_and_action_scoped`
   pins the exact `PermissionNames` field/value map and asserts every value matches
   `^[a-z][a-z0-9-]*\.(read|manage|execute)$`. Only `read`, `manage` and `execute` are permitted
   actions, so `workflow-publishing.export` cannot exist. A distinct permission would have to be spelled
   `workflow-publishing-export.read` — legal, but it would mean amending a map that exists precisely to
   make permission additions deliberate.
2. **A separate permission would grant nothing new.** Executable content is *already* readable under
   these permissions: `GetWorkflowExecutableInputSourcesEndpoint` serves it under
   `WorkflowPublishingRead`, and `GetWorkflowExecutableEndpoint` / `GetWorkflowExecutableProvenanceEndpoint`
   under `WorkflowRuntimeRead`. Export differs by bundling the transitive closure into one response —
   a convenience-of-retrieval difference, not a capability difference. Gating it separately while
   inspection stays open would look like a boundary and not be one.

**Decision: `RequirePermission(PermissionNames.WorkflowPublishingRead)`**, consistent with every other
read-shaped endpoint in the module. This satisfies T084's actual requirement (distinct from
`WorkflowPublishingManage`) and needs no change to `PermissionNames`, to
`WorkflowPublishingPermissionContributor`, or to the two `EndpointSecurityTests` gates.

**Reverse this if the threat model changes** — specifically, if the executable-inspection endpoints are
ever tightened, export must be tightened with them, or it becomes the way around them. Recorded so the
coupling is visible rather than rediscovered.

## Behavior

1. Resolve the **Published-scope** source reference for `{versionId}`; `TestRun`/draft or missing → 404/409 problem detail (FR-B-011: non-portable references are never exported).
2. `IWorkflowArtifactClosureFactory.CreateAsync(versionId)` → `WorkflowArtifactClosure` (root + transitive dependency closure + references + bindings). A dependency missing from the store → 409 problem detail naming the missing `ArtifactId` (export never emits an incomplete closure).
3. Deliver via the built-in `download` target (`DeliverAsync(closure)` → InlinePayload). The target *contract* stays pluggable (`IWorkflowArtifactExportTarget`, fan-in registration) — this GET route simply binds to one safe target; future receipt-producing targets are invoked through their own POST surface.

The three failure modes must be distinguishable by exception type at the factory boundary, and the
missing-dependency case must expose the missing ids as structured data, so the handler can render the
409 without parsing a message string.

## Responses

| Case | Status | Body |
|---|---|---|
| Success (download) | 200 | The closure JSON (`application/json`) with `Content-Disposition: attachment; filename="{definitionId}-{artifactVersion}-closure.json"` (safe-name rules; filename shape shared with studio#493). |
| Unknown version / no Published reference | 404 | problem detail |
| Non-Published-only version (test-run) | 409 | problem detail: export restricted to published scope |
| Incomplete closure in store | 409 | problem detail naming missing dependency artifact id(s) |
| Cycle in the stored dependency graph | 500 | problem detail. Store corruption, not a client error — no content-addressed compiler can form a back edge, so this is never something the caller can fix by changing the request. |
| Storage or codec fault | 500 | problem detail. §2.23.5 has already wrapped the provider's own exception, so the inner detail stays in the log and never reaches the wire. |

The factory raises **five** exception types and the table above has **five** outcomes: the four
originally specified plus 500 for the two that describe the engine rather than the caller. They must stay
distinguishable by type at the factory boundary, and the missing-dependency case must expose its ids as
structured data, so the handler renders one error entry per unresolved id without parsing a message
string.

**Response mechanics.** There is no FastEndpoints byte-download precedent in the repo, so this endpoint
is the first: `Send.StringAsync(json, 200, "application/json")` with `Content-Disposition` written
explicitly onto the response headers. Both interpolated filename segments come from stored artifact
identity and land in a header this code writes by hand, so both are first reduced to a conservative
`[A-Za-z0-9._-]` alphabet with substituted runs collapsed and leading/trailing dots and dashes trimmed —
a definition id carrying a quote, a path separator or a CRLF would otherwise be echoed straight onto the
wire. That sanitisation is what makes quoting the header value safe.

## OpenAPI: no fragment (decided 2026-08-17, Joey)

**This feature produces no OpenAPI contract fragment.** The reasoning, so it is not relitigated:

- **The practice this section originally cited does not exist.** `specs/148-authoring-schema-endpoints/`
  contains only `spec.md` and `checklists/` — no `contracts/` folder, no fragment. The task list's
  instruction to "mirror spec 148 practice" pointed at nothing.
- **A new spec-151-local fragment would be read by no test.** Only two fragments are consumed —
  `specs/092-domain-owned-apis/contracts/management-api.openapi.yaml` and
  `specs/141-runtime-alterations/contracts/runtime-alterations.openapi.yaml`, copied into the
  Architecture test output by `Elsa.Architecture.Tests.csproj`. A third file nobody reads is
  documentation pretending to be a gate.
- **The 092 fragment is a frozen snapshot, not a living inventory.** It carries 8 publishing paths
  while the module serves roughly 13; `publishing/incident-strategies`, `publishing/workflows/preflight`,
  `publishing/value-conversion/profiles` and the three activity-draft-test-run routes are all absent.
  Adding the export path would make it the 9th of 13 with no rule explaining the selection — it would
  encode this feature's recency, not a contract boundary.

The per-spec fragment idea was started deliberately and never reached a working form. Rather than
extend a half-built mechanism sideways, this feature leaves it untouched; whoever finishes or retires
it should do so as its own piece of work, across all the endpoints at once.

`elsa.api.publishing`'s enumerated capability ids are unchanged regardless — rel additions are data,
not schema. The endpoint's contract is pinned instead by the capability declaration, this document, and
the endpoint tests.

## FastEndpoints transition registry

**The route is inventoried, not exempted.** One new row in
`tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` for
`Elsa.Workflows.Publishing.Api.Endpoints.ExportWorkflowExecutableClosureEndpoint`, carrying the module's
existing `removalOwner: "First-party REST API Consolidation"` and its follow-up wave. It inherits the
exit condition rather than opening a new exception — which is the assumption T091a's open rule question
would overturn if the ADR owner rules the other way.

**The counts in `FastEndpointsTransitionTests` move deliberately: 112 → 113 total, Publishing.Api
23 → 24, and the retirement-mode issue count 112 → 113.** The reason is stated in a comment beside them.
This is a reviewed inventory, so it should change by a stated decision and never as a silent
consequence of adding code.

**No `sourceHash` restamp was needed, and that is measured rather than assumed.**
`TransitionExceptionValidator.Validate` compares `sourceHash` **only when a registration's
`DynamicRoute` is true**. Every Publishing.Api route resolves statically through `RouteConstants`, so the
owner fingerprint is never compared for these 24 rows. Note the consequence honestly: the shared
fingerprint *is* now stale for all 24, because this feature edited `.cs` files in the owning project —
it simply is not a gate for static routes. Do not read the passing suite as evidence that the hash is
current.

(The task list's earlier figures — "19 rows", "46 entries", and "T091 — DELETE" — are all stale; the
real pre-change count was 23.)

## Testing

Two assertions carry weight beyond the response cases:

1. **The 200 body must decode back through the production import codec** — the same
   `IWorkflowArtifactClosureSerializer` that `JsonWorkflowArtifactClosureReader` uses, with the format
   version gate applied. A response that is valid JSON but not a *readable closure* would satisfy a
   shape assertion and fail in production, and export/import sharing one codec is the only thing that
   makes a round trip drift-proof.
2. **The advertised capability href must agree with the mapped route**, since nothing else in the repo
   checks it.

Behavioural tests follow the module's own idiom (`Factory.Create` + `HandleAsync` against a real
`DefaultHttpContext`), and the closure fixture derives identity from the production
`WorkflowExecutableHasher` rather than hand-written ids. Hostile-identifier filtering is covered
explicitly: a definition id containing `../`, a quote and a CRLF must not reach the header.

The end-to-end portability proof is **not** here — it is T093's round trip
(`ArtifactExportImportRoundTripTests`, in `Elsa.Architecture.Tests`), which exercises the factory and
the codec without HTTP.
