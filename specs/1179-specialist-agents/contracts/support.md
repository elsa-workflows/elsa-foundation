# Supported composition and qualification contract

**Status**: Proposed finite release target. Every cell below is **unqualified for the specialist
product** until its implementation run is retained. Existing module support and M0 fixtures are not
specialist acceptance. FR-023–FR-026 and Program A13–A15 own completion.

**Proposed ownership qualification**: [ADR 0094](../../../docs/adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md) adds W1–W8 in [agent-ownership.md](agent-ownership.md) to each affected source/store/host profile before it can qualify the new model. Existing matrix dimensions and baseline receipts are retained; none is recertified by a design edit. Use owned fresh stores for the new model. Include both Workflow/Agent adapters, unavailable source storage during artifact execution, independent-store publication repair, reference-pin retention and explicit clean initialization/republish after a pre-GA break. Old-schema adoption/rollback is not required.

## First-party scope

| Axis | Named target | Required evidence |
|---|---|---|
| Shared revision/publishing persistence through Workflow and Agent adapters | SQLite, PostgreSQL, SQL Server, MySQL | Shared CAS/snapshot/history/pin storage, publication reservation/repair, revision/release separation and clean-store readiness W1–W7 |
| Activity/publication/runtime persistence | SQLite, PostgreSQL, SQL Server, MySQL | Existing real provider contexts/migrations plus exact specialist publication, runtime checkpoint and restart behavior |
| Specialist journal/evaluation persistence | EF implementations for those same four providers | Tenant/CAS/fence/acceptance/receipt/protection/retention/recovery tests and actual native handoff |
| Connection metadata | New durable existing-repository implementation for those four providers | Restart, create/version-CAS/revoke/delete/transaction-rollback/secret-reference behavior; durable Secrets composed separately |
| Model providers | Anthropic; Azure OpenAI | Official SDK protocol fixtures through actual MAF, verified capability profiles and authorized live editorial examples |
| Assistant regressions | Existing Weaver Anthropic and GitHub Copilot | Existing turn/tool/session/provider suites and normal single-harness composition after MEAI upgrade |
| Normal development/product journey | Elsa.Workbench + Elsa.Studio.Web | Real create/test/publish/use/evaluate/inspect, permissions and browser evidence |
| Packaged backend host | Elsa.Foundation.Host, ordinary package-fed composition | Independent package installation/feature activation/tenant scope/readiness/runtime proof |
| Independent custom consumer | Package-only authoring-only, runtime-only and combined hosts | Retained packages/locks, no product source/frontend tool shortcut, actual registration/execution |
| Custom Studio host | Package-consumed Studio hosting + specialist module SDK output | Shared assets/import closure, authorized backend connection and real browser journey |
| Native value profile | Sensitivity/redaction, RequiresEncryption=false; encrypted specialist journal is separate | Sensitive flow works end to end; encryption-required native values/captures and secret-bound business inputs are explicitly refused before dispatch |

In-memory repositories are disposable deterministic/dev support. They do not qualify restart or durable
connection support. No MongoDB/Dapper, other model provider, blanket Foundry service, arbitrary team
runtime or all-existing-extension compatibility is implied.

Native encryption-required values are not currently recoverable: existing runtime withholding is not
an encryption codec. The initial specialist profile does not claim to repair that generic capability.
Refusal must be visible in publication/placement and late-bound admission, with no policy downgrade
or model dispatch. Journal ciphertext/key rotation and native sensitivity/redaction have separate
tests and documentation; broader native encryption requires an explicitly delivered/qualified dependency.

## Finite matrix and status accounting

The deterministic/protocol integration matrix is four EF providers × three backend host profiles
(Workbench, Foundation Host, independent combined package host) × two concrete model SDK protocol
fixtures: **24 named cells**. Every cell proves publication, fresh/explicit continuation, accepted
result replay, native/BPMN delivery and connection persistence under the same schema/reader contract.
Tests can share immutable package production; every consumer retains its actual closure/configuration.

Additional shape gates are independent package-only authoring-only and runtime-only native/BPMN profiles,
and the custom Studio package host. They must prove their own dependency envelopes, not inherit a
combined-host success. Authoring-only means no concrete model execution/runtime interpreter feature;
Publishing may reference stable runtime contracts. Runtime-only native/BPMN closures contain zero
Design/Publishing assemblies. The existing allow-listed Runtime.JavaScript Design dependency is not
part of the specialist-only runtime profile and is not claimed repaired by this work.

Live-provider proof is two authorized provider/model deployments × native/BPMN editorial example:
**four named live journeys** on the qualified durable normal host. Use the same adapters/contract as
protocol cells, with bounded declared model/tool budgets. Record exact deployment/model/capability
identities, version/hash, observed output and usage. Do not describe the other store/host cells as
live-provider runs. A supported protocol/store cell and an observed live model journey are different claims.

Saved evaluation quality is stochastic evidence with explicit rubrics/versions, not a replacement for
deterministic schema assertions. Missing credentials, skipped/cancelled jobs, a wrong-cause expected failure,
partial matrix, quarantine and a failed retry keep their actual status. No cell is implicitly green.

## Independent package witness

1. Produce actual packages and Studio distribution from the reviewed source, retaining source IDs,
   archive/DLL/static-asset hashes and normal dependency declarations.
2. Build separate provider/module authors from those packages through public Core/SDK seams. Product
   source references, hidden project imports and consumer frontend builds are disallowed.
3. Restore consumers with isolated caches, retained locks and exact candidate identity checks. Verify
   archives and loaded assembly/static-asset identities, not only project XML.
4. Start normal hosts through public lifecycle/readiness, actual shell activation and tenant-bound services.
   A listening port is not readiness. Use private databases/configuration and normal authorization.
5. Run disabled-new-admission, restart and incompatible-profile transitions within the supported deployment, retaining required readers/workers and accepted results. Separately qualify the declared clean-store pre-GA initialization/recreation/republish; no cross-break old-data support is implied.
6. Run positive journeys and causal negatives: missing consumer/codec, wrong schema/version/scope,
   unsupported settings, source/closure substitution, denied/revoked access and corrupted receipts.
   Wrong-cause failure fails the qualification driver.

M0 establishes bounded external provider/compiler/runtime contracts and a research composition, not
this normal host matrix. Keep its existing command as a regression witness and replace the recorded
BPMN gap only after a fresh actual Design-free closure qualifies.

## Persistence and pre-GA breaking deployment

- Own journal/evaluation tables in the Agent persistence feature; Connections metadata stays with Connections. Shared source revision storage and publishing have their own persistence boundaries. Use real provider mappings and consumer-owned DbContexts; no IQueryable/provider SQL escapes the boundary.
- Deliver required schema/model/codec profiles before admitting work. Within a supported deployment, disabling new admission does not unload readers required by retained artifacts or accepted operations. Ordinary retry/restart preserves journal, scope and receipt guarantees.
- The owner permits clean database recreation for pre-GA breaking changes. No expand/backfill, old-reader aliases, historical hash/ID preservation, rolling mixed-schema transfer or cross-break rollback matrix is required by this amendment. Declare affected source/catalog/artifact/journal/connection stores and packages explicitly; retained dependencies cannot silently reference reset data.
- A future reset procedure stops/drains affected hosts, excludes incompatible binaries, initializes the coordinated schema/configuration profile and recreates sources/republishes before admission. Qualify on owned disposable stores. Never drop stores automatically on startup/publication failure. This design changes no hosts or databases.
- BPMN/Core and provider/package clean changes still require Design/Interchange and runtime-only closure qualification; no runtime-to-Design dependency is permitted. Historical implementation receipts remain historical, not a promise to load every old serialized fixture.
- The central MEAI upgrade still requires normal lock/public API/nuspec/shared-assembly qualification and assistant regressions; private assembly loading is not supported.

## Failure and threat analysis

| Failure/boundary | Required protection and causal proof |
|---|---|
| Forged tenant/owner/handle or same-ID cross-scope records | Trusted authority, exact normalized scope/residual identity checks at every read/CAS/receipt; zero unauthorized disclosure/effect |
| Concurrent draft/save/publish | Existing revision/head/review/idempotency protocol; preserve winner and losing local content |
| Prompt/result schema drift or unsupported provider setting | Exact compiled hashes/validation and actionable preflight/runtime diagnostics; no silent fallback |
| Worker/journal lease loss | Independent exact fences; dispatch marker prevents replacement provider call after uncertain work |
| Cancellation vs accepted turn | Durable exact-operation tombstone/settlement, both winners and delayed delivery tested; accepted/not-applied displayed truthfully |
| Crash after acceptance before receipt/checkpoint/ack | Journal result replay + normal native fenced delivery/outbox repair; model call count remains unchanged |
| Changed approval or effect-before-ack | #2912 exact canonical authorization/effect identity and connector-specific reconciliation; no universal exactly-once claim |
| Protected content/key loss/provider reasoning | Existing protection/key rotation, scope/type/codec envelope, minimal session whitelist/redacted evidence; missing keys/readers fail explicitly |
| Purge or feature disable | Active pins and terminal dedup/cancel authority survive required replay window; old operations cannot become new calls |
| Capability/connection revocation | Point-of-use authorization and source/versioned capability evidence; no credential in artifact/UI/log |
| Budget/SDK retries | Underlying request/tool dispatch reservations, bounded loops/deadlines, actual SDK response-loss controls; unknown usage is not zero |

## Release and evidence gate

User/module-author/operator docs, runnable examples and configuration/reset/recreation/recovery guidance ship
with implementation. Each leaf posts meaningful tests, complete affected suites, discovery/count
reconciliation, architecture/maps/tier/catalog/API/lock evidence, independent review and causal controls.
Required checks must pass on the exact reviewed head; verify resulting descendant main and requalify
after relevant integration changes. Program completion requires observed A1–A15 receipts and reconciled
hierarchy/project status, not merged plans or a collection of green fixture summaries.
