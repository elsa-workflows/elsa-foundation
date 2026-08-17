# Contract: Executable artifact export endpoint (FR-B-010a v1 target)

**Project**: `Elsa.Workflows.Publishing.Api` · **Pinned for**: elsa-foundation-studio#493

## Endpoint framework: Minimal API (ADR 0068)

**This endpoint is an ASP.NET Core Minimal API, not FastEndpoints.** The original draft of this
contract named FastEndpoints before [ADR 0068](../../../docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)
was accepted (2026-08-15), and the resolution changed twice; this section records the final state and
why, so the choice reads as examined rather than defaulted.

**First resolution (2026-08-15): FastEndpoints, under the ADR's capability-gap exception.** The
evidence was that no shell-scoped Minimal API mapping seam existed for Elsa module features — every
`IEndpointRouteBuilder` usage in `src/` was a host or root surface, and the per-shell seam was owned by
issue #1345 and unlanded. Building it inside this feature would have pulled program work in.

**Final resolution (2026-08-16): Minimal API. The gap closed.** `main` merged in the first-party
Minimal API migration (waves 1 and 2, #1382/#1383, plus #1359's compatibility gates). Module features
now map Minimal APIs directly through `MapEndpoints(IEndpointRouteBuilder, IHostEnvironment?)` on
`IWebShellFeature` — see `ActivitiesBpmnInterchangeFeature` delegating to `BpmnInterchangeApi`, and the
same shape in `FoundationAgentApiFeature` and `ApiCapabilitiesFeature`. ADR 0068 grants an exception
only where a Minimal API is *impossible*; it is now demonstrably possible, so **no exception is
available, and none is needed.**

`Elsa.Workflows.Publishing.Api` has not itself migrated — it still holds 23 rows in
`tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` — but ADR 0068 explicitly
permits coexistence during a bounded migration wave. A new Minimal API route may sit beside the
module's existing FastEndpoints routes, and the module's eventual wave moves the rest.

### Verified consequences of the framework choice

Established by reading the code on 2026-08-17; each is a constraint on implementation, not a
preference.

1. **`WorkflowsPublishingApiFeature` must implement `IWebShellFeature` alongside its
   `FastEndpointsFeatureBase` inheritance.** No feature in `src/` currently implements two shell
   interfaces at once, so this combination is unprecedented here. It is dispatched safely:
   `CShells.AspNetCore` walks features by interface in separate passes (`RegisterShellEndpoints`,
   `RegisterShellMiddleware`) and contains no reference to `IFastEndpointsShellFeature` at all —
   FastEndpoints support lives in a different assembly, where the shell-level `FastEndpointsFeature`
   consumes module features only as an *assembly source*. That feature is itself an `IWebShellFeature`,
   so both authoring models already map through one seam.
   **This rests on assembly metadata, not on a passing test — it must be proven by a coexistence test
   before the endpoint is considered done** (see Testing below).
2. **The route is relative, with no leading slash.** The `IEndpointRouteBuilder` handed to
   `MapEndpoints` is already shell-scoped, so `MapGet("publishing/…")` lands where a FastEndpoints
   `Get("publishing/…")` lands. Follow `BpmnInterchangeApi`'s relative form, not
   `StudioPreferencesApi`'s absolute one. `DomainApiCapabilityRegistrationTests` independently requires
   capability hrefs to be non-rooted.
3. **The class's base list must stay on one line.** `HostShellFeatureVisibilityTests` regex-matches the
   declaration to require every direct `IWebShellFeature` to appear in `docs/maps/feature-map.md` and
   `docs/maps/feature-dependency-map.md`, and does not handle multi-line base lists. The feature is
   already in both maps, but both record it as a FastEndpoints feature and **must be regenerated**.
4. **`WithOwner(...)` and `WithAuthoringModel(EndpointAuthoringModels.MinimalApi)` are mandatory**, plus
   a security disposition — `RequirePermission(...)` supplies the third by adding
   `EndpointSecurityDispositionMetadata`. `EndpointManifestBuilder` throws on any endpoint missing one
   of the three wherever a host manifest is captured.
5. **Do not name any member of the mapper `Configure`.** `EndpointSecurityTests` scans
   `src/Elsa/Workflows/Publishing/Api/Endpoints/**` for classes with a `Configure` method and demands
   exactly one `ConfigurePermissions(...)` call in each. A static Minimal API mapper is skipped only as
   long as it has no such method.

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

**The FastEndpoints `Send.StringAsync` + response-helper note in the original draft is void.** A Minimal
API returns the payload through `Results`/`TypedResults`, and the framework emits `Content-Disposition:
attachment` from the file-download-name argument rather than from a manually written header. The single
precedent in `src/` is `ExtensionBuilderApi`'s `Results.File(path, contentType, fileName)`; it uses the
*path* overload, so an in-memory closure payload has no exact precedent — pick the byte/stream overload
that keeps the filename argument, and do not hand-write the header. Response shape is otherwise unchanged.

## OpenAPI

**The practice this section originally cited does not exist**: `specs/148-authoring-schema-endpoints/`
contains only `spec.md` and `checklists/` — there is no `contracts/` folder and no OpenAPI fragment.

The fragments that are actually consumed are `specs/092-domain-owned-apis/contracts/management-api.openapi.yaml`
and `specs/141-runtime-alterations/contracts/runtime-alterations.openapi.yaml`, both copied into the
Architecture test output by `Elsa.Architecture.Tests.csproj` and read by `ManagementApiContractTests`
and `ManagementApiOperationInventoryTests`. `ManagementApiContractTests` asserts the **exact** canonical
path inventory and schema inventory, and Publishing's paths are already in that list.

**So there are two real options, and they are not equivalent.** Adding the export path to the 092
fragment makes it a *tested* contract but requires updating that test's `ExpectedPaths` in the same
change. Writing a new spec-151-local fragment produces a file no test reads. Prefer the 092 fragment:
an untested contract fragment is documentation pretending to be a gate. `elsa.api.publishing`'s
enumerated capability ids are unchanged either way — rel additions are data, not schema.

## FastEndpoints transition registry

**No new row.** The registry inventories FastEndpoints registrations; a Minimal API is not one. The
counts hard-coded in `FastEndpointsTransitionTests` (112 total, 23 for `Elsa.Workflows.Publishing.Api`)
therefore stay as they are.

Note the task list's figure of 19 Publishing.Api rows is **stale — it is 23**. The `sourceHash` in that
registry is an *owner fingerprint* over every `.cs` file in the owning project, so this feature's edits
to Publishing.Api invalidate all 23 rows at once. Whether that requires restamping depends on what
`TransitionExceptionValidator.Reconcile` actually compares — verify before assuming either way.

## Testing

Beyond the response cases, two assertions carry the weight of the framework decision:

1. **A coexistence test proving one feature serves both authoring models.** Compose
   `WorkflowsPublishingApiFeature` once into a test host that calls both `MapFastEndpoints(...)` and
   `((IWebShellFeature)feature).MapEndpoints(endpoints, null)`, then assert an existing FastEndpoints
   publishing route *and* the new export route both resolve. The pattern to copy is
   `tests/Elsa/Studio/Preferences/Tests/Support/StudioPreferencesCanaryHost.cs`. Without this, the
   dual-interface claim rests on assembly metadata alone — and a silently-ignored `MapEndpoints` yields
   an endpoint that compiles, advertises a capability rel, and 404s.
2. **The advertised href resolves to the mapped route**, since nothing else in the repo checks it.

Note that `PublishingHttpContractTests` cannot host this endpoint: it introspects FastEndpoints
`Definition.Routes` via `FastEndpoints.Factory.Create` and never starts a server.
