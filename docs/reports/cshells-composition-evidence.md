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
- The map currently reports one duplicate explicit ID: `JavaScriptWorkflows` appears in both design and runtime JavaScript features. This may be intentional reuse or an ambiguity to resolve before generation.

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
- The repo does not yet classify settings as required, optional, defaulted, secret, filesystem path, type-name selector, collection, shell-wide, host-loading, or feature-bound.

Optional vs required:

- Requiredness is currently scattered in code guards and docs.
- Direct project references do not prove that a referenced feature must be activated in the same shell.
- Source/contributor features, provider/default features, bridge features, and endpoint features need explicit dependency-kind classification.

External package compatibility:

- Package map and feature dependency map show direct package versions.
- No direct package ID currently has multiple direct versions in the map evidence.
- Compatibility policy for selected feature sets is not yet defined beyond direct package/version visibility.

## Missing Or Ambiguous

- Whether duplicate feature ID `JavaScriptWorkflows` should be split, allowed, or resolved by assembly/context.
- Which project-reference edges are true feature activation requirements.
- How to classify settings requiredness and sensitivity.
- How to represent assembly scanning/loading prerequisites independently from selected features.
- Whether feature dependencies should be declared manually, inferred from code, generated from registration tests, or a mix.
- How Nuplane loading/shared assembly settings should be modeled for generated appsettings.

## Recommended Next Work Unit

Name: Configuration and Feature Dependency Classification.

Goal: define the classification rules that a future Feature Composition Explorer and CShells Appsettings Generator must consume.

Scope:

- Decide feature dependency kinds: required activation, optional companion, provider/default implementation, source/contributor, bridge, endpoint/API, compile-time-only reference.
- Decide settings kinds: required, optional, defaulted, secret, connection string, filesystem path, type-name selector, collection, shell-wide, host-loading, feature-bound.
- Resolve or explicitly allow duplicate feature IDs, starting with `JavaScriptWorkflows`.
- Decide how assembly scanning/loading evidence participates in composition output.
- Update the feature dependency map generator only after the classification language is approved.

Out of scope:

- Do not implement the CShells Appsettings Generator.
- Do not treat `src/Server` as a canonical shell composition.
- Do not infer operationally required features solely from project references.

