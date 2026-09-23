# Resource and module selection in persistence tooling

Status: engineering design for #1967, under review. This completes the selection rules for the [configuration-context proposal](tooling-configuration-context.md); no implementation is claimed.

## Operator selection

Add `--resource <name>` alongside `--configuration-context <mode>` and the existing explicit `--shell`. Resource names are case-insensitive configuration identities, not physical database identities. A resource invocation requires one shell and one selected resource. Environment selection keeps the existing explicit selector and Production default. The selected resource must exist and be used by at least one enabled enrolled participant; definitions alone do not select consumers.

Selection uses a **declared target group**: the selected resource's canonical provider and ConnectionName in this shell's configuration snapshot. Include all resource aliases with that same pair, using configuration's case-insensitive name comparison. This is a reference-level grouping, not a claim of physical database identity. It is identical for offline and live commands and never depends on a secret value. Modules are candidates when at least one enabled enrolled owner resolves to that pair; validation still includes every owner of each selected module/context as described below.

A module whose owners all use another connection reference is outside this declared group even if the references might identify the same physical database. Its refusal states a selection-scope limitation, not physical incompatibility. Select that declared resource separately, or author the same named reference when the resources intentionally form one migration group. This preserves deterministic offline/live module selection without looking up secrets to discover extra modules.

| Existing module selector | With an explicit resource |
|---|---|
| `--from-host` | Select every candidate module in the declared target group, including aliases with the same provider/reference pair. Include dependency-enabled features before deriving modules. |
| `--modules A B` | Preserve the exact requested module set; every module must be a candidate in the declared target group. Refuse an unrelated, disabled-only, unenrolled or outside-group module rather than silently dropping it. |
| `--all` | Retain the existing meaning of all discovered modules. Refuse unless every discovered module is a candidate in the declared target group. Never reinterpret all as a filter. |
| No module selector on `list` | Return the same resource-scoped set as from-host. Other commands retain the existing requirement for a module selector. |

`--resource` without a supported explicit context refuses. A context with no resource may inspect the whole shell through list/plan, reporting each module's resource membership and unresolved prerequisites. Script and live operations that would include any resource participant require an explicit resource; they never infer one from the supplied connection or from there being only one resource definition. A context whose selected modules remain wholly legacy follows legacy target behavior and reports that no resource target check occurred.

Preserve the existing module dependency order/check. A dependency under an alias with the same provider/reference pair is eligible like any other group member, but an explicit --modules list must still include it. From-host already includes all enabled group members. Never add a module from a different declared group implicitly; refuse the incomplete selection and name the required module and its reference-scope limitation. This decision does not change migration dependency declarations or introduce a cross-database migration operation.

## A module cannot be partially validated

After selection, inspect every enabled participant that owns the selected module/context, not just the feature whose binding caused it to enter the set. In particular, the eight Runtime feature identities all contribute to one Runtime context. An explicit module selector must not hide an incompatible participant of the same context.

Resource name equality is neither necessary nor sufficient for physical target equality. For aliases that resolve to the same provider and connection reference in the same context, reference agreement can be established offline. When different references might resolve to the same database, report target affinity as unverified offline; do not declare an invalid layout merely because the reference or resource names differ.

Live operations compare the actual env/stdin connection with every distinct expected named connection required by the selected modules, including aliases. Resolve each reference once in the invocation snapshot, use the existing strict value equality, and retain the values only inside the trusted host operation. This is the same lookup-and-compare rule applied to the selected set, not a database identity discovery mechanism. Differently formatted values or separate migration credentials do not establish equality.

Known shared-context options and operation-specific transaction requirements remain additional checks. A legacy participant sharing a context with a resource participant cannot be ignored. If its effective options cannot be established without running feature code, report the mixed layout as unresolved and refuse live execution; do not substitute initialized defaults or claim that the wholly resource-based shared/split proof covers it. Pure legacy configurations retain their existing behavior.

## Offline and live evidence

List and plan resolve membership and report checks/prerequisites without opening a database or resolving expected secret values. Plan may describe an unresolved target-affinity prerequisite. Script emits only the selected modules' existing migration artifacts; unresolved physical affinity must be represented explicitly in its evidence and must not be promoted to live target verification or deployment readiness. Invalid references here mean missing/malformed resource identity, Provider or ConnectionName, not an absent named connection value. Named connection-value availability is a live prerequisite and is never looked up by offline commands. Invalid reference identities, unsupported membership or known inconsistent context options refuse before any artifact is written.

Apply, validate and post-migrate refuse unresolved/mismatched target affinity before constructing contexts or opening connections. A successful check establishes agreement with the supplied configuration snapshot for that module set. It proves neither parity with a separate running host nor success of a subsequent operation.

The internal listing used to obtain script package facts must use the same host-owned context and selection as script. The host selects and validates modules; the worker supplies package provenance for that selected set. Legacy projected shell/provider data must not enter a context-aware invocation as a competing authority.

## Acceptance cases

- Shared resource plus from-host produces exactly the enabled Runtime, Workflows Design, Activities Design and Publishing modules of the shared fixture.
- Diagnostics resource plus from-host produces exactly its enabled Structured Logs and OpenTelemetry modules; the primary database receives no diagnostics module operation.
- Explicit modules containing one module from another target refuse; all does not silently become a filtered selector.
- An incompatible Runtime participant remains visible when another Runtime participant matched the selected resource.
- Aliases with one connection reference agree at reference level; aliases with different references remain unverified offline and require strict expected-value agreement live.
- A missing dependency never adds an operation against another target.
- File changes between internal list and script do not change the invocation's snapshot, module set or package facts. A later invocation reads the new files.
- No context or module selection is reconstructed from the actual supplied connection value.

The authored schema and participant contract must make these inputs concrete. The live shared/split fixtures and negative cases remain implementation gates in #1968/#1969.
