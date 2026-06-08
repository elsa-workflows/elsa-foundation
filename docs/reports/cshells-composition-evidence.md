# CShells Composition Evidence

Status: planning/evidence report for making Feature Composition Explorer and CShells Appsettings Generator safe to implement later.

## Zoom-Out Check

Program milestone advanced: feature composition.

This is still a high-value next step because CShells appsettings generation would otherwise guess feature IDs, dependencies, and settings semantics. The result belongs in reports and maps: maps capture discovered facts; this report captures current gaps and the next work-unit plan.

Source-of-truth boundary:

- `src/Server` is a test shell, not composition policy.
- `src/Server/appsettings.json` is usable only as evidence for the IConfiguration registration shape.
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
- Which provisional settings labels should be accepted, renamed, split, or merged before generator work.
- How to represent assembly scanning/loading prerequisites independently from selected features.
- Whether feature dependencies should be declared manually, inferred from code, generated from registration tests, or a mix.
- How Nuplane loading/shared assembly settings should be modeled for generated appsettings.

## Recommended Next Work Unit

Name: Configuration and Feature Dependency Classification Review.

Goal: review and refine the provisional classification rules that a future Feature Composition Explorer and CShells Appsettings Generator must consume.

Status of the classification language: provisional architecture knowledge. The labels below are a planning vocabulary, not ratified gates and not generator behavior. The first accepted version should be useful enough for the next composition work unit, but it must remain amendable. Architects may add, merge, split, or rename kinds as new providers, deployment models, security requirements, and shell-loading behavior are reviewed. Until ratified elsewhere, these labels belong in reports/maps-facing planning material, not in the constitution as frozen gates.

Scope:

- Review feature dependency kinds: required activation, optional companion, provider/default implementation, source/contributor, bridge, endpoint/API, compile-time-only reference.
- Review settings kinds: required, optional, defaulted, secret, connection string, filesystem path, type-name selector, collection, shell-wide, host-loading, feature-bound.
- Review whether duplicate feature IDs should fail map generation or remain report-only findings.
- Review how assembly scanning/loading evidence participates in composition output.
- Produce docs/report guidance only. Update the feature dependency map generator only in a later implementation unit after the classification language is approved.

Current no-code boundary:

- Do not implement the CShells Appsettings Generator.
- Do not change source feature registration, options, or activation code.
- Do not update map generator behavior while map-generator work is assigned elsewhere.
- Do not promote the classification language into the constitution until the Configuration & Infrastructure follow-up closes.

Proposed dependency kinds:

- `required activation`: selecting one feature requires another feature to be activated in the same shell.
- `optional companion`: another feature enhances or extends behavior, but the selected feature can still start and operate meaningfully without it.
- `provider/default implementation`: a concrete implementation of a contract where a shell may need one selected provider/default.
- `source/contributor`: contributes declarations, sources, handlers, catalog entries, or other fan-in inputs to another feature.
- `bridge`: connects two domains, contracts, or host surfaces without making either side own the other.
- `endpoint/API`: exposes HTTP/API/endpoints for an underlying capability and may require host routing/API infrastructure separately.
- `compile-time-only reference`: reference evidence required to build or type-check, but not evidence that another feature ID must be activated.

Proposed settings kinds:

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

Classification rule:

- A dependency edge or setting may carry multiple labels.
- Direct project/package references are evidence, not activation policy.
- Required activation should need direct registration evidence, tests, docs/catalog confirmation, or explicit architecture classification.
- Unknown or disputed cases should stay marked pending review rather than guessed by the generator.

Duplicate feature ID recommendation:

- Current evidence shows no duplicate explicit feature IDs after splitting workflow JavaScript design/runtime activation into `JavaScriptWorkflowsDesign` and `JavaScriptWorkflowsRuntime`.
- Future duplicate concrete `ShellFeature` IDs should be modeled as ambiguous pending review and should block appsettings generation.
- Do not allow duplicates "by context" unless the CShells configuration model gains an approved namespace/context mechanism; the observed IConfiguration shape keys selected features by `{featureId}` under a shell.
- Renaming is the preferred resolution when two concrete features are independently selectable.

Assembly scanning/loading recommendation:

- Treat assembly scanning/loading as host-loading output, separate from selected feature IDs.
- Selected features answer which CShells features activate.
- Host-loading output answers which packages, assemblies, shared assemblies, or folders the host must make available or scan.
- Feature-bound settings may still point to scanning inputs, such as folder paths or type-name selectors.
- Nuplane loading/shared assembly settings remain host-loading evidence until architecture approves the exact generated output shape.

Out of scope:

- Do not implement the CShells Appsettings Generator.
- Do not treat `src/Server` as a canonical shell composition.
- Do not infer operationally required features solely from project references.
