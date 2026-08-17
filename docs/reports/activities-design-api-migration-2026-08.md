# Activities Design API Minimal API migration (2026-08)

Issue: [#1373](https://github.com/elsa-workflows/elsa-foundation/issues/1373)
Spec Kit work unit: [`specs/166-activities-design-api-migration`](../../specs/166-activities-design-api-migration/)
Decision: [ADR 0068](../adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)

## Result

The complete 38-registration `Elsa.Activities.Design.Api` owner now uses one explicit public
`ActivitiesDesignApi.MapActivitiesDesignApi(IEndpointRouteBuilder)` mapping surface. Production no
longer references `Elsa.Api.FastEndpoints`; all routes are standard ASP.NET Core Minimal API
endpoints with stable operation, owner, tag, security, accepts/produces, and OpenAPI metadata.

The owner transition count is 38 to zero. The program ratchet moves from 61 to 23 first-party
FastEndpoints registrations, leaving only the separately owned Publishing transition surface.
FastEndpoints remains in Activities Design tests only for the immutable historical oracle and the
same-evaluator coexistence canary.

## Immutable before evidence

The historical service was captured from pre-migration commit
`883b691eb8fdd4515b378e0d89b19c2185ea9e00` by the clean-content-guarded checked-in runner. The
receipt identifies every runner dependency, requires the source commit to be an ancestor, and is
reproducible from a detached checkout. Its runner fingerprint is
`8a33cd90658ed7b6ad68b5a688452fe5b6f3e7148d5237110b93006590eed3a3`.

| Evidence | Count / SHA-256 |
| --- | --- |
| FastEndpoints registrations | 38 |
| HTTP observations | 84: all 38 anonymous challenges plus authenticated success, binding, domain, cancellation, and the captured historical defect |
| Projected OpenAPI operations | 38 |
| HTTP fixture | `f1e9417227eec8f4b1c8adc46e7388cebf0af8450d5b4afba4a36807277cfdf7` |
| Projected OpenAPI fixture | `4c8b34f31c145ce887d5fc5bf76d87d98a4736ce4b4d0a73b29bad20ef52f1bf` |
| Raw OpenAPI fixture | `ee5ef34ceecea0d339b903d0a20eed943481c2883dd93c07f12a459ccb080f4b` |
| Initial empty approvals | `37517e5f3dc66819f61f5a7bb8ace1921282415f10551d2defa5c3eb0985b570` |

Independent review found that the original corpus exercised only valid typed query values. A separate
historical supplement—without modifying the 84-case oracle—now captures four real FastEndpoints
failures for malformed enum, integer, nullable-integer, and Boolean query values. Its four-case fixture
hash is `b1dc3df083bd73f257ec753b4f344eca411cc412d9f9f9b67d99a9c128b00f8f`, its runner fingerprint is
`1a2863db2483bc655098dd6bf4a372004261ade1357c366679e173230b2ba516`, and a detached checkout
reproduces the fixture and receipt byte-for-byte. The after replay matches the historical structured
`400 application/problem+json` response exactly, while an eight-route regression test proves every
typed-query mapping rejects malformed values before mediator dispatch.

Bidirectional comparison consumes 88 approval entries representing 44 reviewed deltas. Thirty-eight
conceptual OpenAPI approvals record the owner-owned `Activities Design` tag and the truthful
source-generated string-enum schemas; each has an exact reverse entry. The remaining six conceptual
HTTP approvals correct the captured `Forks.GetStatus` FastEndpoints route-only DTO binder failure:
the Minimal route executes the existing handler and returns its established receipt contract. They
cover body, headers, JSON, media type, status, and terminal state, again with exact reverse entries.
All other HTTP behavior compares exactly. Unused, duplicate, one-sided, wrong-value, stale, and
unknown approval mutations fail the executable comparer.

## Route-by-route disposition

The canonical [38-route manifest](../../specs/166-activities-design-api-migration/contracts/activities-design-route-manifest.md)
is the route-by-route evidence table. Every row is mapped exactly once with its frozen method,
template, stable operation identity, action, and success contract:

- seven rows retain `201` plus `Location`;
- `Drafts.Discard` retains `204` with no body;
- the other 30 successful operations retain `200`;
- all 38 routes are protected and publish exactly one `activity-design.read` or
  `activity-design.manage` action;
- all 38 consume a historical OpenAPI operation and publish stable owner/tag/Minimal-authoring,
  request/response, content-type, and standard `401`/`403` metadata; and
- `Forks.GetStatus` is the sole reviewed HTTP correction described above. The OpenAPI metadata
  improvements apply consistently to all 38 operations.

The mapper preserves route-over-body identity, case-insensitive JSON input, camel-case output and
dictionary keys, camel-case string enums, explicit nulls, opaque provider payloads, paging/cursors,
ProblemDetails, sanitization, logging, and same-instance cancellation behavior. Existing mediator,
provider, store, and domain handlers remain the behavioral authority.

## Stable contracts and source generation

`Elsa.Activities.Design.Api.Core` owns 120 formerly API-local public types: 73 model records, 44
request/command records, and three enums. The former API assembly forwards every type, including the
open generic page contract, and member-signature hashing protects source and binary compatibility.
Implementation helpers, authorization contexts, providers, stores, codecs, and handlers remain in
the implementation assembly and do not appear in endpoint/OpenAPI contract metadata.

The owner JSON context covers every accepted and produced contract with web-compatible options. The
effective resolver chain resolves the stable contracts through source generation before any default
resolver. Request and response metadata completeness, dictionary/enum casing, nullable/required
members, route-field omission, and provider-payload opacity are mutation-tested.

## Authorization

The owner contributes four catalog actions:

- `activity-design.read` and `activity-design.manage` are the only route-owned actions;
- `activity-design.author-provider` and `activity-design.read-provider-payload` remain inner,
  resource-specific service decisions.

The real-host matrix covers anonymous `401`, trusted denied `403`, exact, implied, evaluator-level
wildcard, normalized external, untrusted external, ambiguous identities, malformed claims,
absent/mismatched tenant, route-resource mismatch, provider-authoring denial, payload present or
redacted, replacement evaluators, and cancellation. Denial is proven before provider, store, or
sender invocation. Representative Minimal routes and a retained test-only FastEndpoints canary use
the same dynamic policy provider, claims normalizer, permission evaluator, and resource handlers.

## Composition and unloadability

A representative combined host maps the 38 Activities Design routes exactly once alongside already
migrated Minimal API owners and retained Publishing FastEndpoints routes. The public, non-sealed
feature has virtual service/mapping seams and idempotently registers dynamic API Explorer refresh.

Three collectible owner generations each execute real authorization; representative catalog,
authoring, availability, dependency, lifecycle, and upgrade delegates; configured providers,
stores, and adapters; source-generated binding/serialization; and native OpenAPI in alternating
OpenAPI-first and serialization-first orders. Endpoint removal and four service disposals are
asserted before forced compacting collections. Weak references for the load context, assembly,
feature/mapper, endpoint delegates, resolver/context/type metadata, OpenAPI services, providers,
stores, sender, authorization state, and service provider all become unreachable without sleeps,
private/global cache clearing, production GC, or omitted OpenAPI.

This proof found a compiler-generated async state-machine attribute escaping through handler
metadata. The shared `RequireStableOpenApi` convention now removes only compiler/debugger handler
attributes before lifetime validation; a mutation test proves arbitrary owner metadata is still
rejected rather than silently stripped.

## Live Workbench evidence

The executable checkpoint was
`70d690bd08ef9431d9a0507a0733f8ea476cfec5` on macOS 26.5.2, .NET SDK 10.0.300, and PowerShell
7.4.2. The previous test database was moved to the recoverable directory
`/tmp/wave7-workbench-db.O2e2Dg`; the reference-composition schema was applied to a fresh
`src/Apps/Elsa.Workbench/elsa-groundwork.db` with `outcome=applied`, `targetMutated=true`, and no
diagnostics. Workbench ran at `http://localhost:5095` from the rebuilt executable.

Durable Git identities for the tested checkpoint are:

| Input | Git object |
| --- | --- |
| Commit tree | `59bcc6e5867d696753987ebfd8742c9d4d12d05a` |
| Workbench project file | `bcbadbdc4bb7b2081f56a780e02f20ff932f6f05` |
| Activities Design API tree | `96270e71b1842a5840fb821ef7fc92e31b33edca` |
| Activities Design Groundwork tree | `bd0e00aeca3bd0bb584c78f1448d066a26ac6cbe` |

| Journey | Result |
| --- | --- |
| `get-endpoints/Test-DesignActivityGets.ps1` | 13/13 |
| `write-endpoints/Test-DesignActivityWrites.ps1` | 10/10 |
| Existing reusable activity scripts | 8/8 scripts green: author/publish/execute, three-level composition, pinning, two draft-test-run flows, outcome routing/limits, and sequence nesting |
| `reusable-activities/Test-ActivityUpgradePlan.ps1` | Green: persisted create/get, staged apply, receipt, exact B publication, refresh/successor, final apply/receipt, exact A publication, and authoritative B-to-C-v2 / A-to-B-v2 pinning |

The live gate found two product defects that in-process endpoint parity did not expose:

1. `definitions/picker` returned `500` because its exhaustive Groundwork route treated required
   `entity.definitionId` as nullable and failed `GW-ROUTE-007`. The manifest now declares that
   projection non-nullable; the rebuilt GET suite passes 13/13.
2. Upgrade-plan creation returned `500` because plan and receipt storage projected generic
   `entity.id` although their canonical identities are `plan.planId` and `receipt.receiptId`. The
   manifest now projects the actual paths; the complete persisted upgrade journey passes.

Both corrections have focused manifest regressions and passed the complete 76-test Activities
Design Groundwork persistence suite. They are product fixes, not stale-test accommodations.

The repository-wide gate exposed two further transition-infrastructure defects. The solution folder
still grouped the new Activities Design API Core project at the owner root, and an older catalog
test project still reflected over deleted FastEndpoints classes. The solution topology now follows
the domain tree, historical `*.BeforeCapture` executables are explicitly treated as evidence rather
than product projects, and the catalog suite inspects the real Minimal API route and stable request/
response contracts. A separate Wave 9 receipt assertion also incorrectly required every later
`src/` or solution change to reproduce the historical Runtime E2E tree; it now verifies the
historical receipt's identities, uniqueness, result counts, and composite digest without rewriting
that old run as if it had executed against later program waves.

## Verification record

- Complete Activities Design owner suite after review correction: 618/618.
- Legacy Activities Design catalog/support suite migrated to the real mapper: 26/26.
- Activities Design Groundwork persistence suite after the live fixes: 76/76.
- Immutable before/after HTTP, OpenAPI, malformed-query, compatibility, and behavior subset: 69/69.
- Focused compatibility/security/collectibility/transition/combined-host Architecture gate: 36/36.
- Authorization/security/context gate: 53/53; architecture security/transition gate: 10/10.
- Full Architecture suite after a complete solution restore: 509/509.
- Full `Elsa.Server.slnx` build: 0 errors, 176 aggregate existing warnings (package vulnerability/pruning,
  nullable/obsolete/compiler, analyzer, and pre-existing Workbench style warnings); no new warning
  was introduced by the final Wave 7 corrections.
- Workbench rebuild: 0 errors; 17 existing repository warnings (package-pruning advisories and one
  pre-existing Workbench style warning).
- Public contract compatibility is included in the 69-test immutable compatibility/behavior subset
  and the 618-test owner suite.
- PowerShell parser, changed-C# formatter, generated-map freshness, and `git diff --check`: green.

Independent review round 1 reported no Critical findings, one Required malformed typed-query parity
defect, and two advisories. The Required finding is closed by the receipt-pinned historical supplement,
exact before/after replay, complete typed-query call-site regression, and shared fail-closed binder. The
unused Wave 9 test helper advisory is also removed; the intentionally historical Wave 9 receipt scope
remains documented rather than being represented as current-HEAD execution evidence.

Independent review round 2 examined exact clean commit
`8c442d846911ec9a61f4e1578524849a364f439a`. Its product/code gate was clean: 0 Critical findings,
0 product/code Required findings, and 1 accepted advisory for the intentionally historical Wave 9
receipt scope. The reviewer reran the complete Activities Design suite (618/618), the focused immutable
contract/baseline/behavior suite (35/35), and the Architecture security/collectibility/transition suite
(105/105); map freshness, `git diff --check`, and the clean-worktree check also passed. Its sole release
bookkeeping requirement was to record these final totals and close T049, which this update completes.

The first PR CI run then found a repository gate integration defect rather than an endpoint defect:
the container-free solution-filter generator discovered the two historical `*.BeforeCapture` evidence
executables even though they are intentionally absent from `Elsa.Server.slnx`, and MSBuild correctly
rejected the inconsistent filter. The workflow now applies the same root-anchored evidence-project
classification as the architecture guard before filtering Testcontainers projects. The exact generated
filter loads successfully and excludes both historical capture projects; the subsequent PR CI rerun is
the release gate for this correction.

## Risks, rollback, and follow-up

- Publishing still owns the final 23 first-party FastEndpoints registrations. Removing the shared
  package/runtime and the retained test oracle belongs to the program's final retirement wave.
- Historical fixtures and approval registries are intentionally strict. A future public contract
  change must update them through an explicit reviewed decision, not by recapturing the before
  service after migration.
- The stable Core project is now a compatibility boundary. Moving implementation seams into it or
  adding reflection fallback would reintroduce owner-generation retention risk.
- Framework constitution §2.24 and Elsa constitution §E2.9 remain provisional. This migration
  follows accepted ADR 0068 and does not ratify either section.
- Reverting the mapper/retirement commits restores the 38 FastEndpoints endpoints and dependency
  without changing domain data. The two Groundwork corrections are independently valuable fixes
  for pre-existing live routes and should remain unless their persistence contracts are separately
  redesigned.

Recommendation: keep the owner on Minimal APIs. Contract, security, coexistence, source-generation,
native OpenAPI, collectibility, and live persistence/execution evidence are all positive; no
Activities Design capability gap justifies a FastEndpoints exception.
