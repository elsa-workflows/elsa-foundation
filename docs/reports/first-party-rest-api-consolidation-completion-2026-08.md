# First-party REST API consolidation: program completion report

**Program**: [#1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342) — Consolidate first-party REST APIs on Minimal APIs

**Final unit**: [#1376](https://github.com/elsa-workflows/elsa-foundation/issues/1376) — Retire FastEndpoints from first-party REST APIs

**Decision of record**: [ADR 0068](../adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)

**Spec**: `specs/168-fastendpoints-retirement/`

## Outcome

Elsa's first-party REST surface is entirely ASP.NET Core Minimal APIs. The program began with **164
concrete first-party FastEndpoints registrations across 18 owner assemblies** and ends with **0**.

| Scope | Start | End |
|---|---|---|
| First-party FastEndpoints registrations | 164 | **0** |
| Owner assemblies carrying them | 18 | **0** |
| Approved transition exceptions | — | **0** (registry is `[]`) |

Both figures are guard-held rather than asserted in prose. `FastEndpointsTransitionTests` discovers
the first-party surface and requires it to be empty with an empty exception registry, and it still
runs after this unit precisely so the result stays proven rather than assumed.

## How the program ran

| Unit | Scope |
|---|---|
| #1343, #1352 | ADRs standardizing first-party REST on Minimal APIs |
| #1344, #1356 | Endpoint permission authorization unified on Foundation Identity policies |
| #1345 | Atomic CShells endpoint generation with collision safety |
| #1346 | Migration compatibility and authoring gates |
| #1347, #1348, #1349 | Studio Preferences canary, Secrets representative module, Structured Logs REST and SSE |
| #1364 | Wave 0 — retirement-grade endpoint inventory |
| #1365, #1366 | Foundation tracks H (retained host route metadata) and D (dynamic HTTP publication metadata) |
| #1367–#1375 | Waves 1–9 — small/read, bounded CRUD, Identity protocol, Agent, OpenTelemetry, Workflows Design, Activities Design, Publishing, Runtime |
| #1376 | This unit — final retirement |

## What this unit retired

Per framework constitution §2.25.4, both lists follow. The second is the more useful one.

### Retired

| Artifact | Evidence |
|---|---|
| `src/Elsa/Api/FastEndpoints/` — 6 endpoint base classes, 4 configurators, feature base, API security feature and options, filters, contracts, SSE writer and response extensions | Solution builds with 0 errors; no production consumer existed |
| `tests/Elsa/Api/FastEndpoints/Tests/` — the test project for the above | Its subject no longer exists |
| Four coexistence oracles: `StudioPreferencesApiCoexistenceTests`, `SecretsApiCoexistenceTests`, `StructuredLogsApiCoexistenceTests`, `Wave2MixedHostCoexistenceTests` | Maintainer decision; see the §2.25.2 note below |
| `FastEndpoints` feature entry in `docker/compose/elsa-workbench.shells.json` | Composition had drifted from `shells.json` and `shells.baseline.json`, which had already dropped it |
| Stale prose in 12 files | Read after removal; see below |

Net effect: **79 files changed, +186 / −2697 lines**.

### Examined and deliberately kept

This is the list that stops the next consolidation review re-deriving these conclusions.

| Artifact | Why it stayed |
|---|---|
| `CShells.FastEndpoints` in `Elsa.Foundation.Host` (package, allowlist entry, `Program.cs` rationale) | **This is the retained third-party compatibility boundary.** The Foundation Host provides the FastEndpoints runtime seam so third-party feed features can mount FastEndpoints endpoints. Removing it would withdraw a capability, not retire dead code. |
| `EndpointAuthoringMetadata` / `EndpointAuthoringModels`, including the `FastEndpoints` constant | The typed authoring and ownership metadata the program was built to add. ~28 test files assert on it, and the `FastEndpoints` model still describes a real authoring choice available to third parties. |
| `TransitionExceptionValidator` and `FastEndpointsTransitionTests` | The mechanism proving the first-party surface is empty. Retiring it in the same unit that performs the retirement would delete the evidence for the unit's own claim. |
| ~28 test-only endpoint types across 11 files (Foundation Identity permission adapters, per-wave authorization guards, contract guards) | They exercise Foundation Identity permission evaluation and endpoint security. Re-anchored, not removed — see below. |
| 36 frozen `*fastendpoints*.json` baselines and both `tools/compatibility/*FastEndpointsCapture` tools | The capture tools build clean once the first-party project reference is dropped; their only remaining dependency is the third-party package. The frozen wire evidence therefore stays **regenerable**, so no archival was forced. |
| `OpenTelemetrySseStreamWriter`'s rationale comment, `WorkflowsPublishingApiFeature`'s "former FastEndpoints web defaults" | Both remain accurate statements after removal. |
| All `specs/**` and prior `docs/reports/**` mentions | Historical record of the program. Rewriting them would falsify it. |

## The finding that shaped this unit

The unit's plan assumed removal would be straightforward, because no `src/` project referenced
`Elsa.Api.FastEndpoints`. That was true and it was misleading, in exactly the way §2.25.3 predicts a
census will mislead.

**28 test-only endpoint types across 15 files derived from the Elsa FastEndpoints bases.** Only four
were the oracles slated for deletion. The rest included the six endpoints covering Foundation Identity
permission evaluation — single, any, all, implied, wildcard, and unrelated-policy — and the retirement
guard's own fixtures.

Those bases delegated to `ElsaEndpointPermissions`, which owned the wildcard-plus-action OR rule as a
single canonical `Any` policy, specifically because separate policy names would make FastEndpoints
compose them as **AND**. Deleting the bases and reimplementing that composition in test code would
have left those guards asserting a copy of the rule instead of the rule — green, and protecting
nothing.

So the rule moved rather than died:

- `PermissionNames` → `Elsa.Api.AspNetCore`, whose own comment already described it as the shared
  endpoint security convention that hosts and identity providers use without referencing a domain API.
- `ElsaEndpointPermissions` → `Elsa.Foundation.Identity.Abstractions.Authorization` as
  `EndpointPermissionPolicy`, beside the `PermissionPolicyCodec` it formats with. `StandardMetadata`
  now takes the authoring model as a parameter instead of hardcoding FastEndpoints.

Neither move required a new project reference. The retained guards got a test-local base in
`Elsa.Api.Compatibility.Testing` deriving from the third-party FastEndpoints bases and delegating to
the relocated rule, so they keep asserting production behavior. The bases kept their old type names,
so each guard changed only its `using` directive.

## Residual third-party compatibility boundary

A third-party consumer can still author FastEndpoints endpoints and mount them beside first-party
Minimal APIs:

- `Elsa.Foundation.Host` ships the FastEndpoints runtime seam and shares
  `CShells.FastEndpoints.Abstractions`, so a feed feature implementing `IFastEndpointsShellFeature`
  resolves the same type as the host.
- `EndpointAuthoringModels.FastEndpoints` remains a recognized authoring model in endpoint metadata.
- `EndpointPermissionPolicy` remains public, so a third-party endpoint can compose Elsa's permission
  policy exactly as the first-party bases used to.

What Elsa no longer ships is its own FastEndpoints endpoint bases and hosting plumbing.

## Constitutional notes

**§2.25.2 deviation.** The clause grants standing to delete a guard test "provided the report names
the gate that replaced it". No gate replaces the four coexistence oracles, so the precondition is not
met. They were deleted on the maintainer's decision, recorded on #1376. Claiming the architecture
suite supersedes them would be false: that suite asserts the first-party surface is *empty*, which is
the opposite assertion.

The capability is preserved by construction, as set out above; what is withdrawn is automated
coverage of it. The re-anchoring narrows the gap — several retained guards still prove a
third-party-based FastEndpoints endpoint receives correct Foundation Identity permissions beside
first-party Minimal APIs — but no test now asserts the specific mixed-host scenario the four oracles
covered. This paragraph is the dated record of when that coverage was withdrawn and by whose decision.

**§2.13 packaging.** `Elsa.Api.FastEndpoints` ceases to be produced. The repository is pre-release, so
no compatibility shim or deprecation period is owed.

**Known defect deliberately not fixed.** `IdentitySeeder` documents its all-access literal as
mirroring `Elsa.Api.FastEndpoints.Constants.PermissionNames.All`, a type this unit moved to
`Elsa.Api.AspNetCore.PermissionNames`. The comment is now a dangling reference. It is **not** fixed
here: the file sits under the frozen ASP.NET Core Identity EF oracle owned by the Zero-EF program,
whose ratchet permits no source change before its own approved removal unit. The correct fix is a
one-line comment update, and it belongs to that unit.

## Risks and rollback

The principal risk is the withdrawn mixed-host coverage described above. A regression that prevented
third-party FastEndpoints endpoints from coexisting with first-party Minimal APIs would not be caught
by an automated guard; it would surface as a consumer report.

Rollback is reverting the merge commit. This is a subtractive unit with no data migration and no
persisted state to unwind. A partial rollback that restored the endpoint bases without restoring their
registrations would recreate the zero-assembly activation failure documented in the Wave 8 report, so
a revert must be whole.

## Verification

| Gate | Result |
|---|---|
| `Elsa.Server.slnx` build | 0 errors |
| `Elsa.Foundation.Identity.Tests` | 256 passed / 0 failed / 0 skipped — **identical to its pre-change baseline**, confirming every permission guard survived re-anchoring |
| `Elsa.Architecture.Tests` | 507 total after the oracle removals, all passing |
| `FastEndpointsTransitionTests` | green — first-party surface empty, exception registry `[]` |
| `EfCoreSurfaceRatchetTests`, `FrozenAspNetCoreIdentityEfOracleRatchetTests` | green |
| Generated maps | `-- check` green |

Preserved guards were verified by diffing executed test *names* before and after, not by reading a
passing summary, because a deleted guard and a passing guard produce identical green output. Every
disappearance maps to a recorded removal.

Two gates pushed back during execution and both were right to. The frozen Identity EF oracle rejected
a correct comment fix that belonged to another program, and the EF surface ratchet failed on missing
restore assets for three `BeforeCapture` projects that sit outside `Elsa.Server.slnx` and are
therefore not covered by a solution restore.
