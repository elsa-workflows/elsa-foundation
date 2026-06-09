# CShells Composition Evidence

Status: planning/evidence report for making Feature Composition Explorer and CShells Appsettings Generator safe to implement later.

## Zoom-Out Check

Program milestone advanced: feature composition.

This is still a high-value next step because CShells appsettings generation would otherwise guess feature IDs, dependencies, and settings semantics. The result belongs in reports and maps: maps capture discovered facts; this report captures current gaps and the next work-unit plan.

Source-of-truth boundary:

- `src/Apps/Elsa.Server` is a test shell, not composition policy.
- `src/Apps/Elsa.Server/appsettings.json` is usable only as evidence for the IConfiguration registration shape.
- Feature identity comes from concrete feature classes and their `ShellFeature` metadata.
- Dependency evidence comes from direct project/package references and extension-point catalogs, then needs architecture classification before generation.

## Evidence Added

- [Feature dependency map](../maps/feature-dependency-map.md) records feature IDs, feature classes, public feature properties, project/package reference evidence, and observed IConfiguration shape.
- Concrete `IShellFeature` classes now have explicit `ShellFeature` IDs. Abstract feature bases may remain unannotated unless base metadata becomes useful.
- The new map reports zero concrete features missing an explicit feature ID.

## Existing Evidence

Feature identity:

- Concrete feature IDs are discoverable from `[ShellFeature(...)]`.
- Concrete feature IDs are unique after splitting workflow JavaScript design/runtime activation into `JavaScriptWorkflowsDesign` and `JavaScriptWorkflowsRuntime`.

Feature activation:

- IConfiguration shape is `CShells:Shells:{shellName}:Features:{featureId}`.
- Feature values can contain nested settings, including `Options`, as seen in the test shell.
- Assembly scanning/loading is a first-class capability and should be treated separately from feature selection.

Dependencies:

- Direct project references show compile-time dependency evidence.
- Direct package references show external package/version compatibility evidence.
- Extension-point catalogs explain contributor/replacement/event surfaces, but they do not yet classify feature activation requirements.

Configuration keys:

- Public settable properties on feature classes are the best current evidence for JSON-bindable feature settings.
- Some required/default semantics are visible in code through defaults and validation guards.
- This report now carries a provisional settings-kind vocabulary below, but the repo does not yet apply those labels as approved generator policy.

Optional vs required:

- Requiredness is currently scattered in code guards and docs.
- Direct project references do not prove that a referenced feature must be activated in the same shell.
- Source/contributor features, provider/default features, bridge features, and endpoint features need explicit dependency-kind classification.

External package compatibility:

- Package map and feature dependency map show direct package versions.
- No direct package ID currently has multiple direct versions in the map evidence.
- Compatibility policy for selected feature sets is not yet defined beyond direct package/version visibility.

## Missing Or Ambiguous

- Whether future duplicate feature IDs should fail map generation or remain report-only findings.
- Which project-reference edges are true feature activation requirements.
- Which concrete feature settings are required, optional, defaulted, secret, connection strings, filesystem paths, type-name selectors, collections, shell-wide, host-loading, or feature-bound.
- The exact generated output shape for assembly scanning/loading prerequisites.
- Whether feature dependencies should be declared manually, inferred from code, generated from registration tests, or a mix.
- How Nuplane loading/shared assembly settings should be modeled for generated appsettings.

## Reviewed Classification v1

Status: accepted as provisional planning guidance for agents and future composition tooling. This is not a ratified constitution gate and not generator behavior.

Use this section as the current boundary for Feature Composition Explorer and CShells Appsettings Generator work:

- A future explorer may use these labels to explain dependency and settings evidence.
- A future generator may consume these labels only after the relevant edges/settings are classified by evidence or architecture review.
- Unknown, disputed, or merely inferred cases stay pending review and must not be guessed into generated appsettings.
- These labels remain report-level architecture knowledge until the Configuration & Infrastructure follow-up decides whether any part belongs in the constitution, skill catalog, map schema, or generated output contract.

### Dependency Kinds

Accepted provisional labels:

- `required activation`: selecting one feature requires another feature to be activated in the same shell.
- `optional companion`: another feature enhances or extends behavior, but the selected feature can still start and operate meaningfully without it.
- `provider/default implementation`: a concrete implementation of a contract where a shell may need one selected provider/default.
- `source/contributor`: contributes declarations, sources, handlers, catalog entries, or other fan-in inputs to another feature.
- `bridge`: connects two domains, contracts, or host surfaces without making either side own the other.
- `endpoint/API`: exposes HTTP/API/endpoints for an underlying capability and may require host routing/API infrastructure separately.
- `compile-time-only reference`: reference evidence required to build or type-check, but not evidence that another feature ID must be activated.

Rules:

- A dependency edge may carry multiple labels.
- Direct project/package references are evidence, not activation policy.
- Required activation needs direct registration evidence, tests, docs/catalog confirmation, or explicit architecture classification.
- Project references to `.Core`, helper, or provider libraries can identify runtime prerequisites, but do not by themselves identify a CShells feature ID to activate.
- Source/contributor and endpoint/API features are not automatically required just because they reference the capability they extend or expose.

### Settings Kinds

Accepted provisional labels:

- `required`: absent value prevents valid startup or intended operation.
- `optional`: absent value is valid.
- `defaulted`: code supplies a usable default.
- `secret`: sensitive value that generated output must not inline as a real value.
- `connection string`: database or service connection material, often also secret or deployment-specific.
- `filesystem path`: file or directory location controlled by the host/deployment.
- `type-name selector`: string/type value used to load, select, or instantiate an implementation.
- `collection`: array, list, set, or dictionary value.
- `shell-wide`: belongs to shell-level `Configuration` or host configuration instead of one feature key.
- `host-loading`: controls package, assembly, or scanning behavior separately from feature activation.
- `feature-bound`: belongs under `CShells:Shells:{shellName}:Features:{featureId}`.

Rules:

- A setting may carry multiple labels.
- Public settable feature properties are configuration evidence, not enough to prove requiredness.
- Required/default/secret/path/type-name classification should come from code defaults, validation guards, docs, tests, or explicit review.
- Secret and connection-string values must be represented as placeholders or external references, never as real generated values.
- Host-loading settings stay separate from selected feature IDs, even when a feature-bound setting points at loading inputs such as folders or type names.

### Duplicate Feature IDs

Accepted provisional rule:

- Duplicate concrete `ShellFeature` IDs block appsettings generation.

Rationale:

- Current evidence shows no duplicate explicit feature IDs after splitting workflow JavaScript design/runtime activation into `JavaScriptWorkflowsDesign` and `JavaScriptWorkflowsRuntime`.
- The observed IConfiguration shape keys selected features by `{featureId}` under a shell, so duplicates are ambiguous.
- Do not allow duplicates "by context" unless the CShells configuration model gains an approved namespace/context mechanism.
- Renaming is the preferred resolution when two concrete features are independently selectable.

Map-generation behavior remains separate: duplicates may remain report findings until a later map-generator implementation unit decides whether maps should fail hard.

### Assembly Scanning And Loading

Accepted provisional rule:

- Treat assembly scanning/loading as `host-loading` output, separate from selected feature IDs.

Rationale:

- Selected features answer which CShells features activate.
- Host-loading output answers which packages, assemblies, shared assemblies, or folders the host must make available or scan.
- Feature-bound settings may still point to scanning inputs, such as folder paths or type-name selectors.
- Nuplane loading/shared assembly settings remain host-loading evidence until architecture approves the exact generated output shape.

### Open Questions

- Whether dependency labels should eventually live in generated map schema, hand-authored extension-point catalogs, feature registration tests, or a mix.
- Whether settings labels should become explicit metadata near feature classes/options, stay report-level, or be generated from code/docs evidence.
- The exact appsettings output shape for Nuplane package loading, shared assemblies, and scan folders.
- Whether map generation should fail on duplicate feature IDs or continue producing maps with a blocking finding.

## Next Work Units Enabled

The classification review is complete enough for agents to stop guessing and start applying the boundary in smaller follow-up units.

### Feature Classification Pass

Goal: classify a selected slice of feature dependency edges using the dependency kinds above.

Scope:

- Start with one bounded shell goal or domain cluster, not the whole repo.
- Use feature map and dependency map evidence, then classify only edges backed by registration evidence, docs/catalog confirmation, tests, or explicit architecture review.
- Leave disputed edges pending review.
- Produce docs/report guidance only unless a later implementation unit explicitly updates map schema or generator behavior.

### Settings Classification Pass

Goal: classify selected public feature properties and observed CShells settings using the settings kinds above.

Scope:

- Start with settings needed by the selected feature slice.
- Identify required/default/secret/path/type-name evidence from code defaults, validation guards, docs, tests, or explicit review.
- Represent secrets and connection strings as placeholders or external references only.
- Leave unknown setting values pending review.

### Generator Readiness Pass

Goal: decide whether the current evidence is strong enough to implement a narrow CShells Appsettings Generator.

Scope:

- Confirm duplicate feature IDs remain absent.
- Confirm required activations and required settings are classified for the selected slice.
- Confirm host-loading output shape for the selected slice.
- Only then plan implementation of generator behavior.

Current no-code boundary:

- Do not implement the CShells Appsettings Generator.
- Do not change source feature registration, options, or activation code.
- Do not update map generator behavior while map-generator work is assigned elsewhere.
- Do not promote the classification language into the constitution until the Configuration & Infrastructure follow-up closes.

Out of scope:

- Do not implement the CShells Appsettings Generator.
- Do not treat `src/Apps/Elsa.Server` as a canonical shell composition.
- Do not infer operationally required features solely from project references.
