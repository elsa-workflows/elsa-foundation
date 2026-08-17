# Publishing API Minimal API migration (Wave 8)

Issue: [#1374](https://github.com/elsa-workflows/elsa-foundation/issues/1374)
Owner: `Elsa.Workflows.Publishing.Api`
Scope: 23 first-party Publishing registrations
Spec Kit work unit: [`specs/167-publishing-api-migration`](../../specs/167-publishing-api-migration/)

## Outcome

Publishing now exposes one public, owner-local ASP.NET Core Minimal API mapper for all 23 operations.
`WorkflowsPublishingApiFeature` is a public, non-sealed `IWebShellFeature` with virtual service and
endpoint composition seams. The production owner and Workbench no longer depend on or activate
FastEndpoints. Historical FastEndpoints fixtures and the authorization canary remain test-only until
the shared retirement work in [#1376](https://github.com/elsa-workflows/elsa-foundation/issues/1376).

API Explorer-visible contracts live in the stable `Elsa.Workflows.Publishing.Api.Core` assembly.
The implementation assembly forwards all 63 pre-existing public contract types and adds five stable
problem contracts, while `WorkflowsPublishingJsonContext` supplies source-generated metadata for
every accepts/produces contract. The owner mapper publishes standard route, name, tag, authoring,
security, request, response, and OpenAPI metadata without an Elsa endpoint DSL.

## Immutable before evidence

The real FastEndpoints owner was captured from ancestor source
`cd9fa1743fb982ca60c33384762644b02fdd95ed` before production migration. The checked-in capture
runner is content-guarded and records all source/runner dependencies in
`publishing-before-capture-receipt.json`; it does not depend on an intermediate commit surviving a
squash merge.

| Evidence | Value |
|---|---|
| Registrations / OpenAPI operations | 23 / 23 |
| HTTP cases | 74: 23 anonymous, 23 authenticated success, 10 binding, 17 domain/lifecycle, 1 cancellation |
| Runner fingerprint | `12eef0c1cb94c2d167add51b221b3b80615f57b5aab46e520b88ef3469a47eef` |
| HTTP SHA-256 | `1a6182737dfac07dbd2681558155be31b36ccddd38fa505a613402a937962e7d` |
| Projected OpenAPI SHA-256 | `ba42d7c95f52f968a0a3cad3f4837d5a53726cdb4d25386e6f4e8d047b8760b1` |
| Raw OpenAPI SHA-256 | `38b9ab5bc6b1b27906fa0af9cb8bf9b1e39b422495e5ebc037e822f6110a9bcc` |
| Initial approval SHA-256 | `37517e5f3dc66819f61f5a7bb8ace1921282415f10551d2defa5c3eb0985b570` |

Two independent captures reproduced byte-for-byte. The independent review requested broader failure
coverage, so the runner expansion was committed first and the expanded fixture was then recaptured
twice from the same historical FastEndpoints source before the production migration commit. Baseline
tests verify source ancestry, committed dependency bytes, case/operation counts, hashes, and exact
mutation bites for HTTP and OpenAPI. The added cases cover missing and absent-content-type bodies,
JSON `null`, 422/500/503 ProblemDetails, slot lifecycle, and activity test-run lookup/cancellation.
The workflow-preflight sender-failure scenario is explicitly named as a successful non-invocation
control because that operation uses the compiler directly rather than `IRequestSender`.

## Route and compatibility disposition

The canonical request/response/action inventory is the
[Publishing route manifest](../../specs/167-publishing-api-migration/contracts/publishing-route-manifest.md).
Every row below replays the immutable HTTP case set exactly. OpenAPI keeps the historical operation
IDs, methods, templates, parameters, request/response metadata, media types, and security disposition;
the reviewed difference is the owner-owned `Publishing` tag plus truthful string-enum schemas from
the effective source-generated JSON options.

| Method and route | Permission | HTTP | OpenAPI |
|---|---|---|---|
| `GET /publishing/activities` | read | Exact | Reviewed owner/schema disposition |
| `GET /publishing/activities/{activityId}/construct` | read | Exact | Reviewed owner/schema disposition |
| `GET /publishing/incident-strategies` | read | Exact | Reviewed owner/schema disposition |
| `GET /publishing/value-conversion/profiles` | read | Exact | Reviewed owner/schema disposition |
| `POST /publishing/workflows/{versionId}/preflight` | read | Exact | Reviewed owner/schema disposition |
| `POST /publishing/workflows/preflight` | read | Exact | Reviewed owner/schema disposition |
| `GET /publishing/workflows/{definitionId}/slots` | read | Exact | Reviewed owner/schema disposition |
| `GET /publishing/workflows/{definitionId}/slots/{slotName}` | read | Exact | Reviewed owner/schema disposition |
| `DELETE /publishing/workflows/{definitionId}/slots/{slotName}` | manage | Exact | Reviewed owner/schema disposition |
| `POST /publishing/workflows/{definitionId}/slots/{slotName}/restore` | manage | Exact | Reviewed owner/schema disposition |
| `GET /publishing/workflows/{definitionId}/policy` | read | Exact | Reviewed owner/schema disposition |
| `PUT /publishing/workflows/{definitionId}/policy` | manage | Exact | Reviewed owner/schema disposition |
| `POST /publishing/workflows/{versionId}/publish` | manage | Exact | Reviewed owner/schema disposition |
| `POST /publishing/workflows/{versionId}/test-runs` | manage | Exact | Reviewed owner/schema disposition |
| `POST /publishing/workflows/drafts/test-runs` | manage | Exact | Reviewed owner/schema disposition |
| `POST /publishing/preflight` | read | Exact | Reviewed owner/schema disposition |
| `POST /design/activities/drafts/{draftId}/publication-preflight` | read | Exact | Reviewed owner/schema disposition |
| `POST /design/activities/drafts/{draftId}/publish` | manage | Exact | Reviewed owner/schema disposition |
| `GET /design/activities/publications/{idempotencyKey}` | read | Exact | Reviewed owner/schema disposition |
| `POST /publishing/activity-drafts/{draftId}/test-runs` | manage | Exact | Reviewed owner/schema disposition |
| `GET /publishing/activity-test-runs/{testRunId}` | manage | Exact | Reviewed owner/schema disposition |
| `GET /publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}` | manage | Exact | Reviewed owner/schema disposition |
| `POST /publishing/activity-test-runs/{testRunId}/cancel` | manage | Exact | Reviewed owner/schema disposition |

The approval registry contains 46 entries: one forward and one reverse approval for each of the 23
operations. The comparer binds every approval to the real before/after documents and rejects unknown,
duplicate, unused, stale, no-op, one-sided, and wrong-value approvals. No HTTP difference is approved.

## Authorization and semantic evidence

Each endpoint owns exactly one catalog action: `workflow-publishing.read` or
`workflow-publishing.manage`. Wildcard and configured implication remain evaluator behavior and are
absent from route metadata. The real-host matrix covers anonymous 401, trusted 403, exact,
implication, wildcard, normalized and malformed claims, ambiguous and untrusted identities,
evaluator replacement/cancellation, absent/mismatched tenants, route-resource mismatch, activity
publication denial, payload redaction, and denial before sender/store/compiler/publisher/test-run
invocation. A retained test-only FastEndpoints route and the Minimal routes use the same Foundation
Identity policy provider and evaluator.

The owner suite preserves compiler, activation, preflight, projection, conversion/expression,
slot/policy, activity publication, upgrade, workflow test-run, and activity test-run semantics. Route
values remain authoritative over conflicting body identities; cancellation is rethrown rather than
translated; historical 200/201/202, Location, ProblemDetails, and idempotent replay behavior is
covered through the real mapper. The immutable 74-case corpus is the transport oracle; stateful
receipt, compensation, expiry, conflict, and unavailable-capability families remain covered by the
owner semantic suites and the live lifecycle journey rather than being misrepresented as one-shot
HTTP capture cases.

## Composition and unloadability

Combined-host architecture evidence publishes each of the 23 routes exactly once beside the already
migrated Activities Design, Workflows Design, and Runtime owners. Workbench now has no zero-assembly
FastEndpoints feature: its package, shared-assembly declaration, feature catalog entry, shell feature,
and obsolete API-security catalog reference were removed after the live host gate exposed the stale
composition.

The three-cycle collectible-host test alternates OpenAPI-before-serialization and
serialization-before-OpenAPI. Every cycle executes mapped catalog, preflight, policy, publication,
slot, and test-run delegates through authentication and Foundation authorization; resolves configured
stores, compilers, publishers, resource handlers, and authorizers; binds and serializes with the owner
context; generates native OpenAPI; removes endpoints; disposes services; unloads; and proves weak
references collect. The proof passed five consecutive runs without sleeps, global/private cache
mutation, production GC, or omitted OpenAPI.

## Live Workbench evidence

The Workbench was rebuilt from the current Wave 8 source, a fresh SQLite schema was deployed from the
rebuilt reference-composition manifest, and the server was started at `http://localhost:5095`. The
first live start found and drove the removal of the empty FastEndpoints shell feature; the corrected
host activated 73 features and registered all 23 Publishing routes through the standard shell mapper.

| Suite | Result |
|---|---:|
| `e2e-tests/get-endpoints/Test-PublishingGets.ps1` | 8/8 |
| `e2e-tests/write-endpoints/Test-PublishingWrites.ps1` | 10/10 |
| `e2e-tests/reusable-activities/Test-PublishingLifecycle.ps1` | 23/23 |
| Reusable activity basic/deep/pinning/upgrade/test-run/outcome/sequence/set-outcome scripts | 9/9 scripts |

The new lifecycle journey covers snapshot review/token-bound publish, Runtime Evidence preflight,
policy update and stale CAS, slot get/unpublish/restore, route-over-body authority, activity receipt
replay, both activity test-run lookup identities, and cancellation. Its initial policy failures were a
test payload enum mismatch (`replaceDefaultSlot` versus the public `replace` value); correcting the
test made the exact journey pass. No product behavior was weakened. The generated database, WAL/SHM,
and schema lock were removed after shutdown.

## Verification record

- Publishing API owner suite: 551/551.
- Focused transition, security, combined-host, contract-lifetime, and collectibility architecture set: 47/47.
- Collectibility/race repetition: 2/2 per run, five consecutive runs.
- Workbench build: green after removing the stale FastEndpoints host composition; warnings are the
  existing `Elsa.Http` NU1510 package-pruning notices and `ElsaModuleManagementApi` IDE0019 style notice.
- Full Architecture suite: 513/513 after refreshing the representative endpoint manifest, placing the
  stable Core project in the canonical solution folder, and restoring the repository-wide assets
  required by the EF surface ratchet.
- Full `Elsa.Server.slnx` build: 0 errors and 184 repository warnings. The warnings are inherited
  NU1903/NU1510, analyzer/style, and obsolete-compatibility diagnostics; no branch-introduced warning
  remains.
- Generated maps: `all` refreshed the committed facts and `check` reports that they describe the tree.
- Changed-file formatter and `git diff --check`: green.
- Independent five-axis review round 1 found no Critical issues and identified compatibility-proof,
  legacy 500 ProblemDetails, E6 naming, domain-tree, and transition-guard corrections. The expanded
  oracle caught two additional wire defects before publication: JSON `null` on activity publication
  had become a 400 instead of the historical 201, and generic 500 ProblemDetails had changed their
  historical title/type. Both now replay exactly. Round 2 found the final request-binding exception
  boundary and evidence-bookkeeping gaps; `IOException` and `NotSupportedException` are now logged
  and translated to redacted legacy 500 responses while cancellation still propagates, with direct
  HTTP regression bites. Independent round 3 reports 0 Critical, 0 Required, and 0 Advisory findings
  after the report/checklist closeout recorded here.

Exact local gate commands:

```text
capture_a=$(mktemp -d /tmp/elsa-wave8-recapture-a.XXXXXX)
capture_b=$(mktemp -d /tmp/elsa-wave8-recapture-b.XXXXXX)
PUBLISHING_BEFORE_COMMIT=cd9fa1743fb982ca60c33384762644b02fdd95ed bash tools/capture-publishing-before.sh "$capture_a"
PUBLISHING_BEFORE_COMMIT=cd9fa1743fb982ca60c33384762644b02fdd95ed bash tools/capture-publishing-before.sh "$capture_b"
diff -rq "$capture_a" "$capture_b"
sha256sum "$capture_a"/*
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --no-restore --nologo
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~Wave8PublishingMinimalApiCollectibilityTests|FullyQualifiedName~FastEndpointsTransitionTests|FullyQualifiedName~DomainManagementApiCompositionTests|FullyQualifiedName~EndpointSecurityTests|FullyQualifiedName~OpenApiLifetimeBoundaryTests"
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --nologo
dotnet build Elsa.Server.slnx --no-restore --nologo
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
changed_cs=(${(f)"$(git diff --name-only cd9fa1743fb982ca60c33384762644b02fdd95ed -- '*.cs')"})
dotnet format Elsa.Server.slnx --no-restore --verify-no-changes --include $changed_cs
git diff --check cd9fa1743fb982ca60c33384762644b02fdd95ed
```

## Risks, rollback, and handoff

The principal remaining compatibility risk is future drift between the stable API Core contracts and
implementation metadata. Type-forward, public-surface hash, resolver completeness, mutation, native
OpenAPI, and collectibility tests are the guard. A rollback must restore the 23 endpoint classes,
owner FastEndpoints dependency, Workbench FastEndpoints feature, transition registry entries, and the
previous route mapper composition together; partially restoring discovery would recreate the
zero-assembly activation failure.

Wave 8 leaves no first-party FastEndpoints registrations. Issue
[#1376](https://github.com/elsa-workflows/elsa-foundation/issues/1376) owns removal of shared/test-only
FastEndpoints infrastructure, historical coexistence canaries after their evidence is archived, and
the program completion report. GitHub pipeline availability is an external publication concern; local
gates remain mandatory and are not replaced by waiting for remote checks during the reported outage.

Framework constitution §2.24 and Elsa constitution §E2.9 remain provisional. This migration follows
the accepted [ADR 0068](../adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md) and proposed
[ADR 0069](../adr/0069-openapi-contract-types-use-stable-api-core.md) without treating those provisional
constitution sections as newly ratified.
